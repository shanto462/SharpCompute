# CPU execution: ForCpu and ForSplit

The C# of a shader is real code. `ForCpu` runs it on all CPU cores, and `ForSplit` runs part of the
grid on the CPU and the rest on the GPU, in one dispatch.

- [Running on the CPU](#running-on-the-cpu)
- [Splitting between the CPU and the GPU](#splitting-between-the-cpu-and-the-gpu)
- [What matches the GPU](#what-matches-the-gpu)
- [Feature by feature](#feature-by-feature)

## Running on the CPU

`ForCpu` runs a shader on all CPU cores, with `ThreadIds`, `DispatchSize` and the other built-ins
returning per-thread values, and buffers read and written in place. `ForSplit` runs the leading rows
of the grid on the CPU while the GPU runs the rest, on the same shared buffers, and returns when both
are done:

```csharp
device.ForCpu(width, height, new Blur(source, destination));       // CPU only
device.ForSplit(width, height, new Blur(source, destination));     // adaptive CPU share
device.ForSplit(width, height, new Blur(source, destination), 0.25); // a quarter of the rows on the CPU
device.ForEachSplit(texture, new Gradient(time: 1.5f));            // pixel shaders too
```

`device.ForIndirectCpu` runs an [indirect dispatch](dispatch.md#indirect-dispatch) on the CPU.

`ForCpu` is a correct fallback and a debugging aid rather than a fast CPU path. It is also the way to
find out-of-bounds indexing: the GPU has no bounds checks, while the C# throws
`IndexOutOfRangeException`.

## Splitting between the CPU and the GPU

Both parts of a split see the whole grid (`ThreadIds`, `DispatchSize`, `ThreadIds.Normalized`). Only
split shaders whose threads write their own elements.

Don't expect much from the split. The CPU runs the shader's scalar C#, and the GPU slows down while
every CPU core is busy. So the adaptive mode only splits while that measures faster than the GPU
alone. On the benchmarked workloads it lands between 3 % slower and 9 % faster than GPU-only
([`benchmarks/RESULTS.md`](../benchmarks/RESULTS.md#cpu-execution-and-cpugpu-split), and the table
in [Unified memory](unified-memory.md#splitting-one-dispatch-between-the-cpu-and-the-gpu)).

Split results are not bit-reproducible across calls unless you pass an explicit CPU fraction,
because the CPU and the GPU round floats differently. `device.Prepare<T>(PrepareOptions.IncludeSplit)`
also compiles the pipeline that `ForSplit` runs on the GPU (see
[Precompiled shaders](precompiled-shaders.md#warm-up-with-prepare)).

## What matches the GPU

- Results match the GPU up to floating-point rounding.
- Integer results are identical: conversions from float saturate, shift counts are masked, and
  arithmetic wraps on both, signed and unsigned (see [Integers](shader-language.md#integers)).
- The CPU implementations of `Gpu.*` are bit-for-bit for arithmetic, rounding functions, `sqrt` and
  `fmod`, and within a few ULP for transcendentals. With `[ShaderOptions(FpContract = false)]`,
  arithmetic that uses only `+ - * /` and `Gpu.Sqrt` gives the same bits on both (subnormal results
  aside, which the GPU flushes to zero).
- `checked` arithmetic throws `OverflowException` under `ForCpu` but wraps on the GPU.

## Feature by feature

| Feature | On the CPU | Details |
|---|---|---|
| `[GroupShared]` memory and barriers | One dedicated thread per thread of a threadgroup (at most 1,024), meeting at every barrier, one group after another. Exact, but thousands of times slower than the GPU | [Shared memory](shared-memory.md#on-the-cpu) |
| SIMD-group operations | The 32 lanes of a group run on threads of their own and meet at every operation. Needs uniform control flow; about a quarter of a millisecond per operation and SIMD group | [SIMD groups](simd-groups.md#on-the-cpu) |
| Atomics | `Interlocked` operations; final values match. A shader with atomics on buffer elements is never split | [Atomics](atomics.md#on-the-cpu-and-between-devices) |
| Textures | The same reads, writes and address modes, exact where the GPU rounds | [Textures on the CPU](resources.md#textures-on-the-cpu) |
| Local arrays | Real C# arrays and spans, bounds-checked. `new T[N]` allocates per thread: prefer `stackalloc` | [Local arrays](local-arrays.md#rules) |
| Half precision | Reproduces the GPU's half arithmetic bit for bit, subnormals included | [Half precision](half-precision.md#what-it-computes) |
| Shared code | The same methods, unchanged | [Sharing code](shared-code.md#rules) |
