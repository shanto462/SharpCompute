# Shader language

The C# a shader is written in, and the MSL it becomes. Everything described here is checked against
the GPU by the test suite. The full list of `Gpu.*` methods, with the MSL each one maps to, is in
their doc comments.

- [Scalar types](#scalar-types)
- [Integers](#integers)
- [Vectors and matrices](#vectors-and-matrices)
- [Bool vectors](#bool-vectors)
- [Matrices](#matrices)
- [Bits](#bits)
- [Float helpers](#float-helpers)
- [unorm8 packing](#unorm8-packing)
- [Compile options](#compile-options)
- [Evaluation order](#evaluation-order)

Related pages: [Half precision](half-precision.md), [Local arrays](local-arrays.md),
[Sharing code between shaders](shared-code.md), [Atomics](atomics.md).

## Scalar types

All of these can be:

- locals, parameters and return values of helpers (shared ones too, and type arguments of generic
  ones),
- `const` and `static readonly` fields,
- elements of local arrays and of `[GroupShared]` arrays,
- buffer elements, captured constants, and fields of your structs (with methods, operators and
  constructors on them).

The exception is `bool`, which has no stable layout and stays inside the shader.

| C# | MSL | Bytes | Notes |
|---|---|---|---|
| `float` | `float` | 4 | Vectors: `Float2..4` |
| `Half` | `half` | 2 | Vectors: `Half2..4` |
| `int`, `uint` | `int`, `uint` | 4 | Vectors: `Int2..4`, `UInt2..4` |
| `byte`, `sbyte` | `uchar`, `char` | 1 | MSL's `char` is signed |
| `short`, `ushort` | `short`, `ushort` | 2 | |
| `long`, `ulong` | `long`, `ulong` | 8 | Aligned to 8 bytes in buffers, structs and constants |
| `bool` | `bool` | | Locals, parameters and return values only |

`double`, `decimal`, `char`, `nint`/`nuint` and `Int128` are build errors that name the type to use
instead.

A `ReadWriteBuffer<byte>` is one byte per element:

```csharp
[GeneratedComputeShaderDescriptor]
public readonly partial struct Equalise(ReadWriteBuffer<byte> image, ReadOnlyBuffer<byte> lut) : IComputeShader
{
    public void Execute() => image[ThreadIds.X] = lut[image[ThreadIds.X]];   // one byte per pixel, in place
}

using ReadWriteBuffer<byte> image = device.AllocateReadWriteBuffer(grayscalePixels);   // byte[]
device.For(image.Length, new Equalise(image, lut));
```

## Integers

The integers behave as they do in unchecked C#, on the GPU as on the CPU (`checked` arithmetic is
build error `SCG0052`).

- **Promotion.** `byte + byte` is an `int` in C#, and `-uint` and `int + uint` are `long`. The MSL
  carries the conversions the C# expression has (`uchar(int(a) + int(b))` for `(byte)(a + b)`), so a
  result is as wide as C# says, and a cast back to the small type wraps at its width. Compound
  assignments and `++`/`--` on a small integer truncate on every step, as in C#.
- **Shifts.** The count is masked with 31 for `int` and the smaller integers (which are `int`s by
  the time they are shifted), and with 63 for `long` and `ulong`: `1L << 65` is 2. `>>>` works on
  `int` and `long`.
- **Overflow wraps** at the width of the type, signed and unsigned. `int`, `long` and `IntN`
  arithmetic is exact modulo 2^n, so `int.MaxValue + 1` is `int.MinValue`, `a + 1 > a` is false for
  `int.MaxValue`, and `(a * b) / b` divides the wrapped product.

  Signed overflow is undefined in MSL, and the Metal compiler optimizes on the assumption that it
  does not happen. So the generator computes a signed `+`, `-`, `*` or negation (and their compound
  assignments and `++`/`--`) in the unsigned type of the same width and converts back: `a * b + c`
  on `int`s is `int(uint(a) * uint(b) + uint(c))`. That costs no instructions, because signed and
  unsigned arithmetic are the same machine operations. Program-scope constants wrap the same way.
- **Negating an integer vector** (`-v` on an `IntN`, and the operators that amount to it: `0 - v`,
  `v * -1`, `v / -1`, `v *= -1`, also on a `UIntN`) is emitted as a prelude helper, `__ms_neg(v)`,
  which negates each lane on its own. Apple's Metal compiler miscompiles the vector operator: when
  one lane of the negation is never read and the same vector is subtracted in a chain elsewhere
  (`t += c; t *= 3; t -= v`), the chain silently loses that lane. A constant that negates only some
  lanes does the same when the other lanes are the unread ones, so `new Int3(0, 5, 0) - v` is
  emitted as `C + __ms_neg(v)` and `v * new Int3(1, -1, 1)` as `__ms_mul(v, C)`, a helper that
  multiplies lane by lane. The compound forms (`v *= -1`, `v /= -1`, `v *= new Int3(1, -1, 1)`)
  assign the helper's result, which names the target twice: on a target with side effects
  (`data[Next()] *= -1`) that is an error (`SCG0012`), so put the index in a local first. Vectors
  of floats and halves, and all scalars, are not affected and keep their operators. See
  [Limitations](limitations.md#shader-language) for the negations the generator cannot see.
- **Division by zero** does not trap. Where C# throws `DivideByZeroException`, the GPU carries on
  with an unspecified value (on an Apple M4 Pro, `x / 0` is 0 and `x % 0` is `x` for 64-bit
  operands). `long.MinValue / -1`, which overflows in C#, gives `long.MinValue`. Don't depend on
  either.
- **`checked` arithmetic** (a `checked(...)` expression, a `checked { }` block or a project built
  with `CheckForOverflowUnderflow`) is build error `SCG0052`: it throws `OverflowException` in C#
  and with `ForCpu`, and the GPU has no exceptions. Write it `unchecked`, which wraps on both.
- **Float to integer** conversions truncate, give 0 for NaN and saturate at the bounds of the target
  type, exactly as .NET does: `(byte)300f` is 255, `(sbyte)-129f` is -128, `(ulong)-1f` is 0,
  `(long)1e30f` is `long.MaxValue`. MSL's own `long(x)` wraps instead for a float beyond the range
  of a `long` (2^63 becomes `long.MinValue`), so that one conversion goes through a prelude helper
  that clamps first. All the others are plain MSL casts. Integer to float rounds to nearest even.
- **Literals** keep their C# type: `(byte)5` is `uchar(5)`, `5L` is `5L`, `ulong.MaxValue` is
  `18446744073709551615UL`.
- **Layout.** A `byte` is aligned to 1, a `short` to 2 and a `long` to 8, in C# and in MSL alike, so
  `struct { byte Tag; long Key; }` is 16 bytes on both sides. A constants block that holds a 64-bit
  integer is mirrored field by field at explicit offsets. As for every struct, the generated MSL
  asserts each size and offset (`static_assert`), so a disagreement fails the build of the pipeline
  instead of corrupting data. A user struct packed below the alignment of its fields (`Pack = 4`
  around a `long`) is build error `SCG0009`: MSL cannot under-align a scalar.
- **Speed.** Bytes and shorts cost what `uint`s cost. On an Apple M4 Pro, 64-bit add, shift, xor and
  multiply take about twice the time of their 32-bit counterparts. 64-bit division and remainder are
  about 75 times slower than 32-bit (the GPU has no 64-bit divider), so keep `/` and `%` on `long`
  out of inner loops.
- **Scalars only.** There are no vectors of the 8-, 16- and 64-bit integers, and a `static readonly`
  initializer can use their operators and literals but not call an intrinsic on them.

`Gpu.Abs`, `Min`, `Max`, `Clamp`, `CountBits`, `LeadingZeroCount`, `TrailingZeroCount` and
`ReverseBits` have an overload for each integer type (see [Bits](#bits)), and
`Math.Min/Max/Clamp/Abs` work on them too (see
[Semantics and conformance](cpu-execution.md#semantics-and-conformance) for what they return where
.NET throws). As with `Math.Min`, C# finds `Gpu.Min(b, 5)` ambiguous
between the `byte` and the `int` overload: write `Gpu.Min(b, (byte)5)`.

`scripts/demo-computelab.sh --page small-and-wide-integers` equalises an 8-bit image in place and
hashes every pixel in 64 bits, checked against the CPU:

![ComputeLab's "Small and wide integers" page: a washed-out 8-bit grayscale image from a ReadWriteBuffer of bytes, the same image after histogram equalisation in place with its levels stretched over the whole range, and a 64-bit hash per pixel as colored noise with the pixel that won the 64-bit atomic maximum marked; below them the histogram before with the lookup table's curve, the histogram after, and the 64 bits of the top row's hashes](screenshots/computelab-small-and-wide-integers.png)

## Vectors and matrices

Locals, parameters and return values use the native MSL type. The *boundary* type is the one used
in buffers, captured fields, struct fields and `[GroupShared]` arrays, where the layout has to be the
C# one. Every shader that uses a boundary type asserts its size and offsets (`static_assert`), so a
layout that drifted is a compile error, not corrupt data.

| C# | MSL | At the boundary | Bytes (alignment) |
|---|---|---|---|
| `Float2`, `Float3`, `Float4` | `float2`, `float3`, `float4` | `packed_float2/3/4` | 8, 12, 16 (4) |
| `Half2`, `Half3`, `Half4` | `half2`, `half3`, `half4` | `packed_half2/3/4` | 4, 6, 8 (2) |
| `Int2..4`, `UInt2..4` | `int2/3/4`, `uint2/3/4` | `packed_int2/3/4`, `packed_uint2/3/4` | 8, 12, 16 (4) |
| `Bool2`, `Bool3`, `Bool4` | `bool2`, `bool3`, `bool4` | not allowed: locals only | |
| `Float2x2`, `Float3x3` | `float2x2`, `float3x3` | not allowed: locals only | |
| `Float4x4` | `float4x4` | four `packed_float4` columns | 64 (4) |

Vectors convert to each other explicitly, `(Int3)v`, as MSL's `int3(v)` does. Float and half to
integer truncate and saturate (NaN gives 0); float to half rounds to the nearest half. Comparing two
vectors gives a [bool vector](#bool-vectors). The half vectors have a page of their own:
[Half precision](half-precision.md).

## Bool vectors

Comparing two vectors compares their components, and the answer is a `Bool2`, `Bool3` or `Bool4`
(`bool2/3/4`), as in MSL and HLSL:

```csharp
Bool3 bright = color > threshold;                          // <, <=, >, >=; with a vector or with a scalar
Float3 kept = Gpu.Select(bright, color, new Float3(0f));   // per component: bright ? color : 0
Bool3 usable = bright & !Gpu.IsNaN(color);                 // &, |, ^ and ! are componentwise too
if (Gpu.Any(usable)) { ... }                               // Any and All reduce to one bool
Bool3 same = Gpu.Equal(a, b);                              // and Gpu.NotEqual
```

| C# | MSL | Notes |
|---|---|---|
| `a < b`, `a <= b`, `a > b`, `a >= b` on `Float2..4`, `Half2..4`, `Int2..4`, `UInt2..4` | the same operators | Either side may be a scalar. False for a component where either side is NaN |
| `Gpu.Equal(a, b)`, `Gpu.NotEqual(a, b)` | `(a == b)`, `(a != b)` | Methods, not operators: a C# `==` has to return `bool` for `if (a == b)`, `Equals` and pattern matching to keep working, and MSL's returns a bool vector. `b` may be a scalar |
| `mask & mask`, `\|`, `^`, `!mask` | the same operators | There is no `&&` or `\|\|`: every component is always evaluated, so there is nothing to short-circuit |
| `Gpu.Any(mask)`, `Gpu.All(mask)` | `metal::any`, `metal::all` | One `bool`. `if (a < b)` on vectors does not compile: say which of the two is meant |
| `Gpu.Select(mask, whenTrue, whenFalse)` | `metal::select(whenFalse, whenTrue, mask)` | A componentwise `mask ? whenTrue : whenFalse`, for every vector type. The argument order is HLSL's; MSL's is the reverse, which the generator emits. Both values are evaluated, and the chosen components are copied bit for bit |
| `Gpu.IsNaN(v)`, `Gpu.IsInfinity(v)`, `Gpu.IsFinite(v)` | `metal::isnan`, `metal::isinf`, `metal::isfinite` | `Float2..4` and `Half2..4`; a bool vector |

A bool vector lives in locals, parameters and return values only, like `bool` itself. MSL has no
packed bool vector, so in a buffer, a captured field, a struct field or a `[GroupShared]` array it is
a build error. Store a mask as integers: `Gpu.Select(mask, new Int3(1), new Int3(0))`. Everything in
the table is exact, and identical on the GPU and the CPU.

## Matrices

`Float2x2`, `Float3x3` and `Float4x4` are built from **rows**, as in HLSL and `System.Numerics`:

- `Gpu.Mul(v, m)` is a row vector times a matrix (`Vector4.Transform(v, m)`).
- `Gpu.Mul(m, v)` is a matrix times a column vector.
- `Gpu.Mul(a, b)` is the matrix product (`a * b` for `Matrix4x4`).

MSL matrices are built from columns, so the MSL value is the transpose and `Mul` swaps its operands
to compensate. You never transpose by hand. `Gpu.Transpose` and `Gpu.Determinant` exist for all three
sizes, plus `+`, `-`, and `*` and `/` by a scalar.

`Float4x4` can also be captured as a constant, stored in buffers and used as a struct field: it is 16
floats with no padding on both sides, in the memory layout of `Matrix4x4`. `Float2x2` and `Float3x3`
are for locals only, because MSL pads them.

## Bits

All bit-exact between the GPU and the CPU, including NaN payloads, signed zeros and subnormals.

| C# | MSL | Notes |
|---|---|---|
| `Gpu.AsFloat(int/uint)`, `Gpu.AsInt(float)`, `Gpu.AsUInt(float)`, and `Float2..4`/`Int2..4`/`UInt2..4` overloads | `as_type<T>` | Reinterprets the 32 bits. Also `BitConverter.SingleToInt32Bits`, `Int32BitsToSingle`, `SingleToUInt32Bits`, `UInt32BitsToSingle` |
| `Gpu.AsHalf(ushort/short)`, `Gpu.AsUShort(Half)`, `Gpu.AsShort(Half)` | `as_type<T>` | Reinterprets the 16 bits of a half. Also `BitConverter.UInt16BitsToHalf`, `Int16BitsToHalf`, `HalfToUInt16Bits`, `HalfToInt16Bits` |
| `Gpu.CountBits` | `metal::popcount` | Every integer type, and the `int` and `uint` vectors; the result has the argument's type |
| `Gpu.LeadingZeroCount`, `Gpu.TrailingZeroCount` | `metal::clz`, `metal::ctz` | The width of the type for a zero argument: 32 for an `int`, 8 for a `byte`, 64 for a `long`. `LeadingZeroCount((byte)1)` is 7 |
| `Gpu.ReverseBits` | `metal::reverse_bits` | Within the width of the type: bit 0 becomes bit 31, 7, 15 or 63 |
| `Gpu.RotateLeft`, `Gpu.RotateRight` | `metal::rotate` | Every integer type; the count is an `int`, taken modulo the width, as in .NET |
| `BitOperations.PopCount`, `LeadingZeroCount`, `TrailingZeroCount` | the same, cast to `int` | The `uint` and `ulong` (and `int` and `long` for `TrailingZeroCount`) overloads |
| `BitOperations.RotateLeft`, `RotateRight` | `metal::rotate` | `uint` and `ulong`; the count is taken modulo 32 or 64, as in C# |

ComputeLab's Bits page draws popcount, bit reversal, leading and trailing zeros, float bits and
unorm8 packing as patterns (`scripts/demo-computelab.sh --page bits`):

![ComputeLab Bits page: popcount, bit reversal, leading and trailing zeros, float bits and unorm8 packing drawn as patterns](screenshots/computelab-bits.png)

## Float helpers

- `Gpu.Fma` (also `MathF.FusedMultiplyAdd`) and `Gpu.CopySign` are bit-exact.
- `Gpu.IsNaN`, `Gpu.IsInfinity` and `Gpu.IsFinite` take a `float` and return a `bool`, or take a
  float or half vector and return a [bool vector](#bool-vectors).
- `Gpu.Log10` and `Gpu.Exp10` agree within a few ULP, like the other transcendentals.
- `Fma`, `CopySign`, `Log10` and `Exp10` have `Float2..4` overloads.

GPU math follows Apple GPU behaviour: it flushes subnormal floats (not halves) to zero, and fuses
multiply-adds unless a shader turns that off with [`FpContract = false`](#compile-options).

## unorm8 packing

There are no vectors of bytes, so keep 8-bit *colors* in a buffer as one `uint` per pixel (a single
8-bit channel is simply a `ReadWriteBuffer<byte>`):

- `Gpu.PackUnorm4x8(Float4)` is `metal::pack_float_to_unorm4x8`.
- `Gpu.UnpackUnorm4x8(uint)` is `metal::unpack_unorm4x8_to_float`.

`X` goes to the low byte, which is the layout of an `Rgba32` in memory. Packing clamps to [0, 1] (NaN
gives 0) and rounds the exact product with 255 to the nearest integer, exactly what writing a float
to an `RGBA8Unorm` texture does. Unpacking is `byte / 255f`. Both match the GPU bit for bit, at every
rounding midpoint.

## Compile options

Every shader is compiled with IEEE-safe math, precise math functions and Metal's default
contraction. `[ShaderOptions]` changes that for one shader:

```csharp
[ShaderOptions(FastMath = true)]     // MTLMathModeFast: faster sin/exp/pow, no NaN or infinity
[GeneratedComputeShaderDescriptor]
public readonly partial struct Glow(...) : IComputeShader { ... }

[ShaderOptions(FpContract = false)]  // a * b + c is a multiply and an add, as in C#: no fused multiply-add
[GeneratedComputeShaderDescriptor]
public readonly partial struct Orbit(...) : IComputeShader { ... }
```

- **Fast math** makes a kernel that is mostly `sin`, `cos`, `exp` and `pow` about three times faster.
  On ComputeLab's FastMath page it takes 0.39 ms against 1.2 ms, with a 3.6e-6 error against the CPU
  where safe math has 3.3e-6. But a comparison such as `x != x` is compiled away.
- **`FpContract = false`**: arithmetic that uses only `+ - * /` and `Gpu.Sqrt` then gives the same
  bits on the GPU as on the CPU (subnormal results aside, which the GPU flushes to zero). The unfused
  Mandelbrot kernel is 12 % slower, and saxpy is unchanged.

![ComputeLab's FastMath page: a field of a million values computed by a kernel of sines, cosines, exponentials and powers on the left, its error against the CPU on the right, and under the field the lines "default: IEEE-safe, precise functions" and "kernel: precompiled"](screenshots/computelab-fastmath.png)

## Evaluation order

C# evaluates an expression left to right. MSL leaves the order of two
operands that modify and read the same variable undefined, and it evaluates the value of an
assignment before its target. Where that could change the result, the build fails with `SCG0021`
rather than run something else on the GPU. Besides locals, parameters and `this`, the check tracks
the memory of the shader: with `Bump(slot)` an instance helper that does `data[slot] += 1` and
returns 10,

| Expression | C# (and `ForCpu`) | GPU |
|---|---|---|
| `data[0] += Bump(0)`, from 100 | 110 | 111 |
| `data[data[8]] = Bump(8)`, with `data[8]` = 1 | writes `data[1]` | writes `data[2]` |
| `data[24] += data[24] = 5`, from 100 | 105 | 10 |
| `data[data[32]] += Bump(32)`, with `data[32]` = 40 | updates `data[40]` | updates `data[41]` |

so all four are `SCG0021`, and the message shows the split: `var t = Bump(0); data[0] += t;`.

- **Each resource is one variable**: a captured buffer (or buffer view) or texture, a
  `[GroupShared]` array, a buffer or texture parameter of a helper. Reading any element reads it
  and writing any element writes it, whatever the indices: two elements of one resource are taken
  to be possibly the same one. The location of a `Gpu.Atomic*` call is written, and so is a buffer
  element that a method modifying its struct is called on (`cells[i].Grow()`). A wave matrix's
  `Load` reads its source and `Store` writes its destination. A texture local is
  the texture it was initialized from; one that is assigned again may be any texture of its type.
- **Helpers count at the call**: what a helper reads and writes, and what the helpers it calls do
  (static helpers of any type, struct methods, properties and operators, instance helpers of the
  shader), with the buffers and textures passed as arguments in place of its parameters.
- **Operands and arguments too**: MSL does not say in which order it evaluates the operands of an
  operator or the arguments of a call, so `data[i] + Bump(i)`, `Bump(0) + Bump(1)` and
  `Pair(data[i], Bump(i))` are `SCG0021` as well; evaluate the call into a local first.
- **Still fine**: `data[i] = data[i] + 1`, `data[i] += x`, `a[i] += b[i]` on two resources,
  `data[0] = Bump(0)` and `data[i] = data[j] = 0` (the store comes last in both languages), a
  call's own arguments (`Gpu.AtomicAdd(ref data[0], data[1])`: the call runs after them), and the
  branches of `?:`, `&&` and `||`, which are evaluated in order.
- **Two resources are never compared.** The same buffer bound under two names (captured twice, or
  through overlapping views) is the same memory, which this check cannot see: it is a question of
  how the shader is bound, not of how an expression is written.
