# Performance

Measured numbers, and how to read them. The full benchmark report, including how each number was
measured and the machine load, is [`benchmarks/RESULTS.md`](../benchmarks/RESULTS.md).
`scripts/run-benchmarks.sh` runs the suite and writes that file.

- [GPU against CPU](#gpu-against-cpu)
- [How to read this](#how-to-read-this)
- [Against native Metal](#against-native-metal)
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
- The matrix product is the naive kernel. Written with [`WaveMatrix8x8`](matrix-units.md) for the
  GPU's matrix units, the same 1024×1024 product takes 0.42 ms of GPU time instead of 4.15 ms (the table below).

## Against native Metal

SharpCompute against native Metal on the same GPU, by the GPU's own time for one dispatch
([`LastGpuTime`](dispatch.md#gpu-time-of-a-dispatch); Apple M4 Pro, medians of 5 passes of 25 dispatches):

| Single-precision matrix multiply, 1024×1024 | Written as | GPU time | TFLOP/s |
|---|---|---:|---:|
| `MPSMatrixMultiplication` | Apple's kernel | 0.403 ms | 5.33 |
| `simdgroup_matrix`, 2×4 accumulators | hand-written MSL | 0.417 ms | 5.15 |
| `simdgroup_matrix`, 2×4 accumulators ([`WaveMatrix8x8`](matrix-units.md)) | generated from C# | 0.422 ms | 5.08 |
| threadgroup-tiled 16×16 | generated from C# | 2.01 ms | 1.07 |
| naive | generated from C# | 4.15 ms | 0.52 |
| naive | hand-written MSL | 4.11 ms | 0.52 |

A generated kernel takes the same GPU time as the same kernel written by hand in MSL, to about
1 %: the ratio is 1.008 to 1.012 for the naive matrix multiply, 1.013 to 1.014 for the one on the
matrix units, and 1.000 for an N-body step, for SAXPY and for an integer hash.

The scalar kernels'
5× and 10× to MPS are not the generator's: MPS and the kernels next to it use Metal's matrix units
(`simdgroup_matrix`), which a C# shader reaches as `WaveMatrix8x8`, and that kernel is within 5 % of
MPS.

The same section of the report places every kernel under the machine's measured ceilings:
6.22 TFLOP/s of FP32 arithmetic, of which MPS reaches 86 %, and 249 GB/s of memory bandwidth (244 to
255 across the passes; Apple quotes 273), measured on 1 GiB arrays. SAXPY, on 64 MB arrays, runs at
the memory-bandwidth ceiling (within 2 %): it is bound by memory, and no kernel will make it faster.
See [Native baselines and roofline](../benchmarks/RESULTS.md#native-baselines-and-roofline).

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
| Matrix units against MPS and hand-written MSL | [Matrix units: how fast](matrix-units.md#how-fast) |
| Indirect dispatch and compaction | [Dispatch: indirect dispatch](dispatch.md#indirect-dispatch) |
| Span copies against `device.Copy` | [Resources: copy, fill and clear](resources.md#copy-fill-and-clear) |
| First-frame compile, `Prepare`, precompiled shaders | [Precompiled shaders: what it saves](precompiled-shaders.md#what-it-saves) |
| Image decode and encode | [Imaging: speed](imaging.md#speed) |
| A native Metal host with SharpCompute passes | [Interop: example app](interop.md#example-app) |
