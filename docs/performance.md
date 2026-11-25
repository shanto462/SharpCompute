# Performance

Measured numbers, and how to read them. The full benchmark report, including how each number was
measured and the machine load, is [`benchmarks/RESULTS.md`](../benchmarks/RESULTS.md).
`scripts/run-benchmarks.sh` runs the suite and writes that file.

- [GPU against CPU](#gpu-against-cpu)
- [How to read this](#how-to-read-this)
- [Zero-copy data paths](#zero-copy-data-paths)
- [Unified memory: when it helps and when it doesn't](#unified-memory-when-it-helps-and-when-it-doesnt)
- [Numbers on other pages](#numbers-on-other-pages)

## GPU against CPU

Apple M4 Pro, GPU against the **fastest** CPU variant (multi-threaded, and SIMD where it applies):

| Workload | Size | Fastest CPU | GPU | GPU speedup |
|---|---|---:|---:|---:|
| Mandelbrot (512 iterations) | 1920×1080 | 9.16 ms | 0.54 ms | **17×** |
| Mandelbrot (512 iterations) | 256×256 | 1.00 ms | 0.15 ms | **6.8×** |
| N-body step | 16,384 bodies | 13.8 ms | 3.95 ms | **3.5×** |
| Matrix multiply (naive) | 1024×1024 | 13.7 ms | 4.39 ms | **3.1×** |
| Gaussian blur, radius 8 | 3840×2160 | 2.25 ms | 1.68 ms | **1.3×** |
| SAXPY `y = a·x + y` | 16M floats | 0.85 ms | 1.01 ms | 0.84× (CPU wins) |

## How to read this

- GPU times are one synchronous dispatch, and exclude uploading inputs and reading results back.
- Every dispatch has a fixed cost of roughly 0.1 ms.
- Apple Silicon's CPU and GPU share the same memory.
- Compute-heavy work wins big; memory-bound work like SAXPY doesn't.
- The generated SAXPY kernel runs at the same speed as hand-written MSL (ratio 0.99–1.01).

## Zero-copy data paths

Moving and converting data can cost more than the kernel, and unified memory means it can be
skipped: decode into the GPU buffer, convert and filter on the GPU, encode from the buffer.

| Data path | Size | Before | Zero copy | Speedup |
|---|---|---:|---:|---:|
| RGBA8 image → blur → RGBA8, CPU conversions + Float4 copies (fastest CPU: 21.8 ms) | 3840×2160 | 12.2 ms | 3.10 ms | **3.9×** (7.0× vs CPU) |
| Same kernels, only the copies of the bytes in and out removed | 3840×2160 | 4.28 ms | 3.10 ms | 1.4× |
| File of floats → GPU sum (`MapReadOnlyBuffer`) | 256 MB | 42.0 ms (`ReadAllBytes` + upload) | 4.06 ms | **10.3×** |

## Unified memory: when it helps and when it doesn't

On Apple silicon the CPU and the GPU share the same memory, so there is no upload or download over a
bus. The gains come from not copying at all and from overlapping work. They don't come from extra
bandwidth: both sides use the same memory controller.

| Helps | Measured | Doesn't help | Why |
|---|---|---|---|
| Decode or read straight into GPU memory, convert on the GPU | 4K image pipeline 12.2 → 3.10 ms | Memory-bound kernels (SAXPY) | Same bandwidth; the CPU ties or wins |
| `MapReadOnlyBuffer` instead of reading a file | 256 MB: 42.0 → 4.06 ms | Tiny one-off dispatches | ~0.1 ms floor per dispatch; the CPU finishes first |
| Many small dispatches in one `ComputeContext` | ~99 µs → ~1.9 µs per dispatch | Single heavy kernels | Nothing to copy or batch |
| `ForAsync` double-buffering, CPU and GPU in parallel | 1.38× (7 % above the ideal) | GPU-private buffers | Same speed as shared on Apple silicon |
| | | Splitting one grid between CPU and GPU (`ForSplit`) | 0.97–1.09× of GPU-only: scalar C# on the CPU, and the GPU slows while the CPU is busy |

The full guide, with how to choose an API and the lifetime rules that come with zero copy, is
[Unified memory](unified-memory.md).

## Numbers on other pages

Feature pages carry their own measurements (all on an Apple M4 Pro):

| Topic | Where |
|---|---|
| Fast math and `FpContract = false` | [Shader language: compile options](shader-language.md#compile-options) |
| 64-bit integer arithmetic | [Shader language: integers](shader-language.md#integers) |
| `Half4` against `Float4` | [Half precision: what it buys](half-precision.md#what-it-buys) |
| Local arrays in registers and spilled | [Local arrays: rules](local-arrays.md#rules) |
| Privatised histograms | [Atomics on threadgroup memory](atomics.md#atomics-on-threadgroup-memory) |
| SIMD-group emulation on the CPU | [SIMD groups: on the CPU](simd-groups.md#on-the-cpu) |
| Indirect dispatch and compaction | [Dispatch: indirect dispatch](dispatch.md#indirect-dispatch) |
| Span copies against `device.Copy` | [Resources: copy, fill and clear](resources.md#copy-fill-and-clear) |
| First-frame compile, `Prepare`, precompiled shaders | [Precompiled shaders: what it saves](precompiled-shaders.md#what-it-saves) |
| Image decode and encode | [Imaging: speed](imaging.md#speed) |
| A native Metal host with SharpCompute passes | [Interop: example app](interop.md#example-app) |
