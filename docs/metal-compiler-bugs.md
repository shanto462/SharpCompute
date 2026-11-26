# Metal compiler bugs and workarounds

SharpCompute translates C# shaders to Metal Shading Language (MSL), and Apple's Metal compiler turns
that MSL into GPU code. In a few cases the compiler turns correct MSL into wrong results. The
generator works around each case it knows of, so the C# you write gives the same results on the GPU
as with `ForCpu`. This page lists those cases, what the generator does about each one, what that
costs, and the toolchain it was checked on.

They were found by the [conformance suite](cpu-execution.md), which runs every operator, conversion
and intrinsic on the GPU and with `ForCpu` over edge values, and by the seeded random-expression
differential test (`tests/SharpCompute.Differential.Tests`, `scripts/fuzz-differential.sh`).

- [Tested toolchain](#tested-toolchain)
- [The bugs](#the-bugs)
- [What is left](#what-is-left)
- [After a macOS or Xcode update](#after-a-macos-or-xcode-update)

## Tested toolchain

The workarounds were verified with:

| | Version |
|---|---|
| macOS | 27.2.1 (26B131) |
| Xcode | 27.2 (27B52) |
| Metal compiler (Xcode toolchain, `xcrun metal --version`) | 32023.922 (`metalfe-32023.922.4`) |
| GPU | Apple M4 Pro |

Two compilers are involved. At run time, shaders are compiled by the Metal compiler that ships with
**macOS**. When shaders are [precompiled](precompiled-shaders.md), the **Xcode** Metal Toolchain
compiles them to intermediate code at build time, and the GPU driver of the macOS that runs the app
finishes the job on first use. So the macOS version matters to everyone, and the Xcode version only to
projects that precompile. The division and bit-count bugs are in that last stage (the intermediate
code is correct), so precompiling with another Xcode does not avoid them.

SharpCompute supports macOS 15 and later. Other macOS and Xcode versions may fix some of these bugs,
or have other ones. If something on this page doesn't hold on your versions, please
[open an issue](https://github.com/shanto462/SharpCompute/issues) with the versions and the failing test.

## The bugs

Costs are GPU time on an Apple M4 Pro, from the pull request that added each workaround.

| Bug | What it did | Workaround | Cost | Apple report |
|---|---|---|---|---|
| **Lane dropped from integer vector arithmetic** | When an integer vector is negated (`-v`, or anything the compiler reduces to it, such as `~v + 1` or `v - 2 * v`), a lane of the negation is never read, and the same vector is subtracted in an add/subtract chain elsewhere, that chain lost the lane. | Every `+`, `-` and negation of an integer vector is computed lane by lane (`__ms_add`, `__ms_sub`, `__ms_neg`). | none measurable | FB25732151 |
| **Division guard dropped** | `t == 0 ? 0 : x % t` divided by a nonzero value when `t` came from a conditional (`t = l == C ? 0 : Math.Abs(l)`): the compiler moved the division ahead of its guard and then took the divisor to be nonzero. | An integer `/` or `%` by anything but a nonzero constant divides by `__ms_nonzero(t)`, which is `t`, or 1 for 0. | +1% on a box filter; +9 to 13% in a loop that only divides | |
| **Constant dividend near 2^32** | A 32-bit unsigned `/` or `%` whose dividend is a constant from 0xFFFFFF80 to 0xFFFFFFFF, by a variable, gave wrong results: `4294967295u / x` was 3579139454 for `x = 3`. | When the generator can see the constant, `__ms_div_near_max(c, x)` divides `c - x` instead. | none on other divisions | |
| **Division of constants folded wrongly** | A 32-bit `/` or `%` of a constant by a divisor that only the GPU compiler folds to a constant gave wrong results for most even divisors: `2863311530u % d` was 4294967212 for `d` = 2. The division-guard workaround made this reachable, because its divisor barrier hid constant divisors from the front end, which folds them right. | `__ms_nonzero(t)` returns `t` unchanged when the front end has folded it to a constant (`__builtin_constant_p`, lane by lane for a vector), so the front end folds the division or makes it a multiplication. | none | |
| **Bit counts of 16-bit values** | `Gpu.LeadingZeroCount` of a 32- or 64-bit value holding a sign-extended 16-bit one counted it as zero-extended: `Gpu.LeadingZeroCount((uint)(short)i)` was 16 instead of 0 for a negative short. And a 16-bit count of a value computed in a wider register counted the bits above it: `Gpu.CountBits(b ? s : t)` of the short -2 was 31 instead of 15. | Popcounts are `popcount(reverse_bits(x))`; a 32- or 64-bit leading-zero count is `x < 0 ? 0 : clz(x)`. | none in a Hamming-distance kernel; +27% (popcount), +9% (clz) in a loop that only counts | |
| **Out-of-range float to integer** | MSL leaves these conversions undefined, and the compiler folded them or assumed a range: `(byte)(float)i` became `i & 255`. | Every float and half to integer conversion saturates as in C# (NaN gives 0), through `__ms_sat_*` helpers. | up to +2.6% (a noise kernel with eight `(int)Floor` per pixel), +0.5% colour packing | not a bug: undefined in MSL |
| **Compiler crash** | Some combinations of integer statements crash the compiler when it builds the pipeline (XPC_ERROR_CONNECTION_INTERRUPTED). It fails loudly with a `ShaderCompilationException` at the first dispatch, never with a wrong result. | None in general. The reduced case needs a `short` shifted right by 21; the generator now writes that shift as the sign fill `s >>= 15`, which avoids it. [Limitations](limitations.md#shader-language) has the shapes. | none | FB25737990 |
| **Float to half overflow** | A float the compiler could bound beyond the half range converted to 65504 instead of infinity: `(Half4)Gpu.Max(v, 1e30f)`. | Float to half conversions select ±∞ for a magnitude of 65520 or more (`__ms_to_half`). | under 1% | |

[Shader language](shader-language.md#integers) describes each helper in more detail.

## What is left

- **A constant dividend near 2^32 that only the Metal compiler sees**, through a helper's
  parameter, a local that is also written, or an expression only it folds
  (`BitOperations.RotateLeft(uint.MaxValue, n) / x`), can still divide wrongly. A local with a
  constant initializer that is never written is recognized (`uint m = uint.MaxValue; ... m / x`
  takes the workaround). Write the constant in the division, or read it from a buffer.
- **Other compiler crashes.** The crash above has more than one shape (random shaders found two),
  and only the reduced one is avoided. The differential test's long mode records such a shader and
  goes on.
- **The product of `Gpu.Step` and a constant** can be +0 where C# gives -0 (`Gpu.Step(2, 1) * -2.5f`):
  the compiler writes it as a select. A product with a value that is not a constant keeps its sign.
- **Two forms are build errors** (`SCG0012`), because the lane-by-lane workaround would have to name
  a target with side effects twice: `v *= -1` on such a target, and `+=` or `-=` on components of one
  (`data[Next()].XY += w`). Put the index in a local first.
- **Float and half results that differ from `ForCpu`** beyond subnormal flushing and the
  transcendental tolerance: a half division by a constant, `Sqrt` of a half converted to float, and a
  few more. [Limitations](limitations.md#shader-language) lists them, and
  [`KnownDifferencesTests`](../tests/SharpCompute.Differential.Tests/KnownDifferencesTests.cs) pins
  each one with a repro.

## After a macOS or Xcode update

The workarounds rest on what this compiler does, and a later compiler could optimise one of them
away. After updating macOS or Xcode, run:

```sh
dotnet test SharpCompute.slnx                  # includes BackendMiscompileTests and the conformance suite
scripts/fuzz-differential.sh --seed 2026 --count 20000
scripts/validate-gpu.sh                        # Metal's API and shader validation layers
```

A failure in `BackendMiscompileTests`, the conformance suite or the differential test on a newer
toolchain is worth an issue, with the versions from `sw_vers`, `xcodebuild -version` and
`xcrun metal --version`.
