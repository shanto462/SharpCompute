# Matrix units

Apple GPUs have hardware for small matrix products, and Metal reaches it through
`simdgroup_matrix`: an 8 × 8 matrix that the 32 lanes of a SIMD group hold *together* (two elements
each) and multiply in one step. In SharpCompute that is `WaveMatrix8x8` (`float`) and
`WaveMatrixHalf8x8` (`Half`). Nothing in the names is Apple's: the same shape exists as a
cooperative matrix in Vulkan (`KHR_cooperative_matrix`) and as WMMA fragments in CUDA.

- [Example: an 8 × 8 block per SIMD group](#example-an-8--8-block-per-simd-group)
- [The members](#the-members)
- [Rules](#rules)
- [On the CPU](#on-the-cpu)
- [How fast](#how-fast)
- [Demo](#demo)

## Example: an 8 × 8 block per SIMD group

```csharp
// One SIMD group per threadgroup computes an 8 x 8 block of c = a x b, for n x n row-major matrices.
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MatMulBlocks(ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadWriteBuffer<float> c, int n) : IComputeShader
{
    public void Execute()
    {
        int row = ThreadGroupIds.Y * 8;                    // the same in all 32 lanes
        int column = ThreadGroupIds.X * 8;
        WaveMatrix8x8 sum = default;                       // the zero matrix

        for (int k = 0; k < n; k += 8)
        {
            WaveMatrix8x8 left = WaveMatrix8x8.Load(a, row * n + k, n);       // 8 x 8 of a, rows n apart
            WaveMatrix8x8 right = WaveMatrix8x8.Load(b, k * n + column, n);
            sum = WaveMatrix8x8.MultiplyAccumulate(left, right, sum);         // left x right + sum
        }

        sum.Store(c, row * n + column, n);
    }
}

device.For(n / 8 * 32, n / 8, new MatMulBlocks(a, b, c, n)); // a threadgroup (32 threads) per block
```

## The members

| Member | Meaning | Metal |
|---|---|---|
| `WaveMatrix8x8.Filled(value)` | every element `value` | `make_filled_simdgroup_matrix<float, 8, 8>(value)` |
| `WaveMatrix8x8.Diagonal(value)` | `value` on the diagonal, zero elsewhere (`Diagonal(1f)` is the identity) | `simdgroup_float8x8(value)` |
| `default`, `new WaveMatrix8x8()` | the zero matrix | `make_filled_simdgroup_matrix<float, 8, 8>(0.0f)` |
| `WaveMatrix8x8.Load(source, offset, stride, transpose = false)` | element (*r*, *c*) is `source[offset + r * stride + c]`; transposed, `source[offset + c * stride + r]` | `simdgroup_load` |
| `matrix.Store(destination, offset, stride, transpose = false)` | writes the same 64 elements | `simdgroup_store` |
| `WaveMatrix8x8.Multiply(a, b)` | `a × b` | `simdgroup_multiply` |
| `WaveMatrix8x8.MultiplyAccumulate(a, b, c)` | `a × b + c` | `simdgroup_multiply_accumulate` |

`WaveMatrixHalf8x8` has the same members on `Half`.

## Rules

What they mean, measured on an Apple M4 Pro and pinned by the tests:

- **Load and store** take a `ReadOnlyBuffer<T>` or `ReadWriteBuffer<T>` (or a view of one), which
  is `device` memory, or a `[GroupShared]` array, which is `threadgroup` memory; a store needs one it
  can write. `T` is the matrix's element type: `float` for `WaveMatrix8x8`, `Half` for
  `WaveMatrixHalf8x8` (anything else is build error SCG0049). `stride` is the number of elements
  from one row to the next, so a matrix can be cut out of a larger one, and `transpose` reads or
  writes columns as rows.
- **Mixed precision.** The operands of `Multiply` and `MultiplyAccumulate` may each be either type,
  and the type the method is called on is the precision of the result, in every combination Metal
  allows: `WaveMatrix8x8.MultiplyAccumulate(halfA, halfB, floatSum)` multiplies half operands into a
  float accumulator, which is the useful one (half the memory traffic, no half overflow in the sum).
- **A matrix belongs to the whole SIMD group.** Every operation has to be reached by all 32 lanes,
  with the same arguments in each: compute offsets from `ThreadGroupIds`, `SimdGroupIds.Index`,
  loop counters and constants, never from `ThreadIds` or the lane index. The GPU checks nothing: a
  group with fewer lanes (a threadgroup or grid that is not a multiple of 32, lanes that returned
  early) loads and stores only the elements its lanes hold, and lanes that disagree each go their own
  way. SCG0051 warns about a `[ThreadGroupSize]` that is not a multiple of 32.
- **A matrix has no layout in memory.** It is a local, a parameter or a return value of a helper
  (`ref` and `out` included). It cannot be a buffer, `[GroupShared]` or local array element, a field of a struct
  or of the shader (SCG0048), or a `static readonly` field (SCG0050).
- **Partial tiles are the caller's job.** A matrix is loaded and stored whole. For sizes that are
  not a multiple of 8 (or of the tile a kernel works in), pad the data with zeros, which add nothing
  to any sum; ComputeLab's `WaveMatMul.Pad` and `Unpad` do that for its kernel.
- **Offsets are added term by term.** `Load(a, row * n + k, n)` becomes `a + row * n + k` in MSL:
  each term of a sum of `int`s is one pointer addition, which the GPU compiler turns into far better
  addressing than a 32-bit sum that is then widened (which measured about 15 % slower for the
  product below).
  This is the one place where signed arithmetic is not C#'s: every term wraps as usual, but their
  sum is formed in 64 bits and does not. The two differ only where the terms add up to more than an
  `int` holds, which is no valid offset in C# either: a sum past `int.MaxValue` wraps to a negative
  offset, and `ForCpu` refuses that as out of range (`ArgumentOutOfRangeException`). Only a sum that
  wraps all the way round (below `int.MinValue`, or past 2³²) gives C# an offset inside the buffer
  that the GPU does not use.
- **Results** agree with a reference within rounding: the order in which the eight terms of an
  element are added is the GPU's. Compare with a tolerance.

## On the CPU

With `ForCpu` and `ForSplit`, the lanes of a SIMD group meet at every matrix operation, as
they do for the `Wave*` operations, and the matrix is storage the group shares. The meeting checks
what the GPU does not: it throws `NotSupportedException` when the lanes are at different calls,
when they bring different arguments, or when fewer than 32 take part, and
`ArgumentOutOfRangeException` when a load or store reaches outside its buffer or array. It is for
checking a kernel at small sizes, not for bulk work.

## How fast

`C = A × B` for random floats, by the GPU's own time for one dispatch
([`LastGpuTime`](dispatch.md#gpu-time-of-a-dispatch)), in TFLOP/s (2 n³ operations per product), on an Apple
M4 Pro (16-core GPU, macOS 27.2):

| Kernel | Written as | 1024 × 1024 | 2048 × 2048 |
|---|---|---:|---:|
| Naive, a thread per element | C# | 0.52 (4.15 ms) | 0.52 (33.1 ms) |
| 16 × 16 tiles in shared memory | C# | 1.07 (2.01 ms) | 1.08 (15.9 ms) |
| **Wave matrices, 2 × 4 accumulators** | **C#** | **5.08** (0.422 ms) | **5.19** (3.31 ms) |
| `simdgroup_matrix`, 2 × 4 accumulators | hand-written MSL | 5.15 (0.417 ms) | 5.27 (3.26 ms) |
| `MPSMatrixMultiplication` | Apple's kernel | 5.33 (0.403 ms) | 5.51 (3.12 ms) |

These are the SGEMM rows of the benchmarks' [native baselines](../benchmarks/RESULTS.md#native-baselines-and-roofline)
(`scripts/run-benchmarks.sh --native-baselines`: medians of 5 passes of 25 dispatches, every kernel
on the same seeded random buffers). The C# kernel takes 1.013 to 1.014 times the GPU time of the
same kernel written by hand in MSL and 1.05 to 1.06 times that of MPS, and `--verify` finds its
product bit for bit that of the hand-written kernel and of MPS, and within 4 × 10⁻⁷ of a CPU
reference that sums in another order.

The kernel is `MatMulWaveMatrix` in
[`MatrixUnitsPage.cs`](../samples/SharpCompute.Samples.ComputeLab/Pages/Simd/MatrixUnitsPage.cs), and
its shape is what the speed depends on:

- **Eight accumulators per SIMD group** (2 × 4 matrices, a 16 × 32 block of C). Sixteen spill out of
  the registers: the same kernel with 4 × 4 measured about seven times slower.
- **Load operands straight from the buffers.** Staging them through `[GroupShared]` memory first,
  the way the scalar tiled kernel does, made that 4 × 4 kernel slower again, by almost half.
- **Four SIMD groups per threadgroup** (128 threads, a 32 × 64 tile of C), and the loop over the
  shared dimension unrolled twice by hand.
- **Write offsets as sums**, as above.

## Demo

`scripts/demo-matrixunits.sh` opens ComputeLab on the page (V switches the kernel, N the size); in
the window the bars are the machine's own, in the picture they are the rates above:

![ComputeLab's Matrix units page: a corner of the product under the grid of 8 by 8 wave matrices, SIMD-group blocks and threadgroup tiles that computed it, bars of GFLOP/s for the wave-matrix, shared-memory and naive kernels, and the three operations of the kernel](screenshots/computelab-matrix-units.png)

The 32-lane SIMD groups a matrix belongs to are described in [SIMD-group operations](simd-groups.md).
