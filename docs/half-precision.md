# Half precision

`Half`, `Half2`, `Half3` and `Half4` are MSL's `half`, `half2`, `half3` and `half4`: 16-bit floats.
This page covers what they support, what they compute (measured on the GPU), and what they buy.

- [The half vectors](#the-half-vectors)
- [Writing half constants](#writing-half-constants)
- [What it computes](#what-it-computes)
- [Functions that take a scalar Half](#functions-that-take-a-scalar-half)
- [What it buys](#what-it-buys)
- [What it costs](#what-it-costs)

## The half vectors

The half vectors have the surface of the float vectors:

- constructors and every swizzle,
- `+ - * / %` and the comparisons (which give [bool vectors](shader-language.md#bool-vectors)),
- conversions to and from the float and integer vectors,
- every `Gpu.*` function the float vectors have (`Gpu.Sqrt`, `Lerp`, `Dot`, `Normalize`, `Sin`,
  `Pow`, `Radians` and the rest),
- the [SIMD-group operations](simd-groups.md).

A half has 11 significant bits, about 3 decimal digits, and reaches from 6e-8 to 65504. A half
vector is half the bytes of a float vector wherever it is stored: a `Half3` in a buffer, a constant,
a struct field or a `[GroupShared]` array is the 6 bytes of a `packed_half3`, aligned to 2.

```csharp
[GeneratedComputeShaderDescriptor]
public readonly partial struct Tone(ReadWriteBuffer<Half4> pixels, Half3 gain) : IComputeShader
{
    private static readonly Half3 Luma = (Half3)new Float3(0.299f, 0.587f, 0.114f);

    public void Execute()
    {
        Half4 pixel = pixels[ThreadIds.X];
        Half3 color = pixel.XYZ * gain;
        Half level = Gpu.Dot(color, Luma);

        pixels[ThreadIds.X] = new Half4(Gpu.Select(color > (Half)1f, new Half3(level), color), pixel.W);
    }
}
```

## Writing half constants

C# has no half literal, and neither a `float` nor an integer constant converts to `System.Half`
implicitly. Write `(Half)0.5f` and `(Half)2`, keep constants in `static readonly` fields, or convert
a whole vector: `(Half3)new Float3(...)`.

A `static readonly` half or bool value can be initialized with constructors, casts and operators,
but not with a function call (`SCG0024`): the compile-time evaluation only does 32-bit arithmetic.

## What it computes

This was measured on an Apple M4 Pro (every function of one argument over all 65,536 halves, the
others over 262,144 operand sets), and it is what `ForCpu` reproduces.

- `+ - * /`, negation and every conversion are IEEE half operations, rounded once to the nearest half
  (ties to even), and identical on the GPU and the CPU bit for bit. That includes subnormals: the GPU
  flushes float subnormals to zero, but not half ones.
- The GPU fuses `a * b + c` into one half fused multiply-add, as it does for floats. With
  `[ShaderOptions(FpContract = false)]` it does not, and half arithmetic then gives the same bits on
  both sides throughout.
- Bit for bit as well: `Abs`, `Min`, `Max`, `Clamp`, `Saturate`, `Sign`, `Floor`, `Ceil`, `Round`,
  `Trunc`, `Fract`, `Fmod`, `Sqrt`, `Rsqrt`, `Fma`, `Lerp`, `Step`, `Smoothstep`, `CopySign`, `Dot`,
  `Cross`, `Length`, `Distance`, `Normalize` and `Reflect`. The one exception is which zero `Min`,
  `Max` and `Clamp` return for zeros of both signs.
- The transcendental functions (`Pow`, `Exp*`, `Log*`, trigonometric and hyperbolic) agree within
  one half ULP. The GPU's `tan` and `atan` are that one ULP off for 3.5 % and 5.7 % of all halves,
  the others for at most 7 of the 65,536.
- Three things differ from the float overloads, because the GPU treats halves differently:
  - `Length`, `Distance` and `Normalize` are not rescaled, so they overflow to infinity for a length
    above about 255.9 (its square does not fit), and `Normalize` of a zero vector is NaN.
  - A zero remainder from `%` and `Fmod` is `+0`, also for a negative dividend.
  - `Sin`, `Tan` and `Asin` of `-0` are `+0`.
- `Gpu.Radians` and `Gpu.Degrees` of a half are one half multiplication by the constant rounded to
  half (0.017456055 and 57.28125). They are bit for bit on both sides and within about one half ULP
  of the exact product.
- The SIMD-group operations take a `Half` and half vectors like they take floats
  (`Gpu.WaveActiveSum`, the scans, the shuffles, the quad operations and the rest). The exchanges,
  minima and maxima are bit for bit. A sum or a product is too when its partial results are all
  halves; otherwise it shows the order of the lanes, which is the GPU's own (the CPU's is lane
  order). Sums of 32 values of magnitude up to 4 came out up to 3/64 apart, products up to 4 half
  ULP. An exclusive scan of sums starts from `-0`, as it does for floats.

## Functions that take a scalar Half

The math functions of `Gpu.*` take half *vectors*. For a scalar `Half`, convert to `float` or put it
in a vector. Only a few functions take a scalar `Half`:

- `Gpu.Radians` and `Gpu.Degrees`,
- the SIMD-group operations,
- the bit casts `Gpu.AsUShort(h)`, `Gpu.AsShort(h)` and `Gpu.AsHalf(bits)` (MSL's `as_type`, also
  written `BitConverter.HalfToUInt16Bits`, `HalfToInt16Bits`, `UInt16BitsToHalf` and
  `Int16BitsToHalf`). They keep the bits of every number, `-0` and subnormals included, and keep a
  NaN a NaN.

Where a float or int overload exists as well, that one is preferred, so `Gpu.Radians(90)` and a
SIMD-group operation on a `byte` mean what they meant before.

The bits of a half reinterpret as `ushort` and `short`, which are scalars. A vector form of the bit
casts waits for 16-bit integer vectors (`Short2..4`, `UShort2..4`).

## What it buys

That depends on what limits the kernel. Each kernel below was written once on `Float4` and once on
`Half4` (`SharpCompute.Benchmarks --half-precision`; Apple M4 Pro, GPU time from commit to
completion, the median of 31 dispatches with the kernels taking turns, the middle of three runs):

| Kernel | `Float4` | `Half4` | `Half4` is |
|---|---:|---:|---:|
| Streaming pass, `y = x * 0.5 + y` over 16M elements (memory-bound: 512 MB against 256 MB) | 4.01 ms | 2.29 ms | 1.75× as fast |
| Blur of 289 taps per pixel, 1024 × 1024 | 2.74 ms | 1.92 ms | 1.4× as fast |
| The same blur, reading halves and summing in `float` | | 2.07 ms | 1.3× as fast |
| Arithmetic only: 64 rounds of a folding map per thread, 2048 × 2048 | 3.33 ms | 3.06 ms | 1.09× as fast |

Half the bytes is where the time goes. A pass that is bound by memory bandwidth is close to twice as
fast, and one that is all arithmetic gains under 10 %.

The blur is in between, and less steady: its `Float4` time was 2.1 ms, 2.7 ms and 3.0 ms in the
three runs where the `Half4` time stayed at 1.9 ms, so its gain was between 1.1× and 1.6×. Reading
halves and computing in floats (`(Float4)image[i]`) keeps most of that gain and gives up none of the
accuracy until the result is stored.

## What it costs

What it costs is the 11 bits. ComputeLab's *Half precision* page runs that blur both ways on the same
image, whose 8-bit channels a half holds exactly, and compares each with the blur in `double`:

- With 289 taps the largest error of the half blur is 8.3e-3 of the range and the mean 8.9e-4
  (4.0e-3 with 81 taps), against 3.0e-8 for the float blur.
- The running sum is what does it: it grows to several thousand, where halves are 4 apart.

The page's kernels are compiled with contraction off, and the check is that both match `ForCpu` bit
for bit:

![ComputeLab's "Half precision" page: the source image, the same image blurred with 289 taps in Half4 arithmetic, the error of that blur against the blur in double as a heat map, and a mask of the blurred image made with Gpu.Select(color > threshold, color, 0)](screenshots/computelab-half-precision.png)
