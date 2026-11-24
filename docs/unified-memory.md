# Unified memory: when it helps, and when it doesn't

On Apple silicon the CPU and the GPU share one pool of memory. Every SharpCompute buffer is
`MTLStorageModeShared`: the bytes the GPU reads are the same bytes `AsSpan()` gives you. There is no
PCIe bus and no separate VRAM to upload to. That changes which costs exist, and so which
optimizations pay off.

This page explains what you gain from that, and where it makes no difference. Every number comes
from [`benchmarks/RESULTS.md`](../benchmarks/RESULTS.md) (Apple M4 Pro, BenchmarkDotNet); that
report also says exactly what each timing includes.

## What unified memory changes

| Cost | Discrete GPU | Apple silicon + SharpCompute |
|---|---|---|
| Getting input to the GPU | A copy over PCIe into VRAM | None, if the data is produced in the buffer. Otherwise a `memcpy` within the same RAM |
| Reading results back | A copy back over PCIe | None: `buffer[i]` / `AsSpan()` read the GPU's output directly |
| Per-dispatch overhead | Command submission + wait | The same: about 0.1 ms per synchronous `For` |
| Memory bandwidth | VRAM is separate from system RAM | CPU and GPU share one memory controller |

Two consequences follow:

- **Copies don't disappear by themselves.** Copying a managed array into a buffer is still a
  `memcpy`, about 0.6 ms for a 33 MB image on this machine. The win comes from not making the copy
  at all.
- **Bandwidth is shared.** A kernel that only streams memory gets no extra bandwidth from the
  GPU.

## Where it helps

### 1. Produce data in GPU memory, and consume it there

Write input straight into a buffer instead of into an array that is then copied. For example, an
image decoder can draw into `AsSpan()`, and `RandomAccess.Read` can read a file into it. Read
results the same way: the encoder, the network stack or your own code reads `AsReadOnlySpan()`
directly.

| 4K RGBA8 image → blur → RGBA8 | Time |
|---|---:|
| CPU (Parallel.For + SIMD) | 21.8 ms |
| GPU, bytes copied into and out of the buffers | 4.28 ms |
| **GPU, decoded into and encoded from the buffers (zero copy)** | **3.10 ms** |

Removing the two copies alone is worth 1.4× here, because the kernel itself is fast. The heavier the
kernel, the smaller this share becomes.

### 2. Do the format conversion on the GPU

Converting bytes to floats and back on the CPU costs more than the copies do. When the decoded bytes
are already in GPU memory, the conversion can run on the GPU, fused into the first and last kernel.

| Same pipeline | Time |
|---|---:|
| CPU converts bytes ↔ `Float4`, copies 133 MB in and out, Float4 kernels | 12.2 ms |
| **GPU reads and writes the bytes itself** | **3.10 ms (3.9×)** |

The `imgproc` sample does exactly this; see `ImageFile.LoadRgba8`, `UnpackRgba8`, `PackRgba8` and
`ImageFile.SaveRgba8`.

### 3. Put files on the GPU without reading them

`device.MapReadOnlyBuffer<T>(path)` memory-maps a file and wraps the mapping as a buffer, so the GPU
reads the OS page cache directly.

| Sum a 256 MB file of floats on the GPU | Time |
|---|---:|
| `File.ReadAllBytes` + `AllocateReadOnlyBuffer(data)` (two copies) | 42.0 ms |
| `RandomAccess.Read` into a buffer's `AsSpan()` (one copy) | 30.3 ms |
| **`MapReadOnlyBuffer`** | **4.06 ms (10.3×)** |

These numbers assume the file is in the page cache. A cold read from disk is limited by the disk
either way, but it still skips the extra copies.

### 4. Batch small dispatches

Every synchronous `device.For` pays about 0.1 ms to create, commit and wait for a command buffer.
Unified memory doesn't remove that cost. A `ComputeContext` pays it once for many dispatches:

| Cost of each extra dispatch | Time |
|---|---:|
| Another `device.For` | ~99 µs |
| Another `ForAsync`, issued back to back | ~9 µs |
| **Another dispatch in the same `ComputeContext`** | **~1.9 µs** |

100 chained small dispatches drop from about 9.9 ms to about 0.28 ms. A two-pass 4K blur gains only
11 %, because its kernels dominate the total.

### 5. Overlap CPU and GPU work

Because the CPU reads GPU output in place, the CPU can process frame N while the GPU computes frame
N + 1. Use `ForAsync` with two buffers:

| 8 frames: GPU render (8.4 ms) + CPU analysis (26.5 ms) | Time |
|---|---:|
| Synchronous: render, wait, analyze | 39.1 ms |
| **Double-buffered `ForAsync`** | **28.4 ms (1.38×)**, 7 % above the ideal max(GPU, CPU) |

The `Pipeline` sample (`scripts/demo-pipeline.sh`) runs the same pattern on heavier frames and gets
1.62×. Overlap can never beat the slower of the two sides, so it only pays when both sides have real
work.

## Where it doesn't help

### Memory-bound kernels

SAXPY (`y = a·x + y`) moves 12 bytes for every 2 floating-point operations, so it is limited by
memory bandwidth, not by arithmetic. At 16M floats the multi-threaded CPU runs it in 0.85 ms (about
226 GB/s) and the GPU in 1.01 ms (about 190 GB/s). Both sides use the same memory controller, so
moving the work to the GPU adds a dispatch without adding bandwidth. Keep streaming element-wise
work on the CPU (`TensorPrimitives`), or fuse it into a kernel that also does real arithmetic.

### Tiny one-off dispatches

A 4,096-element SAXPY takes 184 ns on one CPU core and about 98 µs as a GPU dispatch. Below roughly
0.1 ms of work, a single dispatch can't win. Batching helps when there are many dispatches; for a
single one, the CPU wins.

### Single heavy kernels

When the kernel itself takes most of the time, removing copies or submissions barely changes the
total. Mandelbrot at 1080p is already 17× faster on the GPU than on the CPU. Zero copy, batching and
async add nothing to that, because there are no copies to remove and only one dispatch.

### GPU-private storage for buffers

On discrete GPUs, private memory is faster than shared memory. Here, `MTLStorageModePrivate` and
`MTLStorageModeShared` buffers took the same time (1.91 ms) on a 4K `float4` blur-style kernel, in
every combination of input and output. Private storage on Apple silicon helps textures (lossless
compression), not buffers, so SharpCompute keeps every buffer shared and CPU-visible.

### Using the same memory at the same time

Shared memory is not shared access. Don't read or write a buffer from the CPU while a dispatch that
writes it is in flight: wait for `For` to return, for the context to execute, or for the `ForAsync`
task to complete. Double-buffering is how you overlap safely.

## Choosing an API

| Situation | Use |
|---|---|
| Data comes from a decoder, a parser or a socket | Allocate the buffer first and write into `AsSpan()` |
| A large file of raw values | `MapReadOnlyBuffer<T>(path)` |
| Memory owned by a native library, page-aligned | `WrapReadWriteBuffer` / `WrapReadOnlyBuffer` with a `release` callback |
| Bytes in, bytes out (images, packed data) | Unpack and pack on the GPU, inside the first and last kernel |
| Many small or chained dispatches | One `ComputeContext` |
| CPU work on each GPU result (frames, batches) | `ForAsync` with two buffers |
| Element-wise streaming math (SAXPY-like) | The CPU (`TensorPrimitives`) |
| Less than ~0.1 ms of work, once | The CPU |

Lifetime rules that come with zero copy:

- A wrapped or mapped buffer's memory is released only after every dispatch that uses it has
  finished, even if you dispose it earlier. Its `release` callback can therefore run on the thread
  that calls `Dispose`, on the finalizer thread, or on a Metal completion thread.
- Allocate wrapped memory in whole pages: round the byte size up to `GraphicsDevice.PageSize`.
- `MapReadOnlyBuffer` memory is read-only on the CPU as well. `IsCpuWritable` is false, and
  `AsSpan()` throws; use `AsReadOnlySpan()`.
