# Diagnostics

The generator reports C# it cannot translate as a build error or warning with an `SCG` id, at the
exact construct. This table lists every id. The message of each one says what is wrong and, where
there is one, what to write instead. The source of truth is
[`AnalyzerReleases.Unshipped.md`](../src/SharpCompute.SourceGenerators/AnalyzerReleases.Unshipped.md).

| Id | Severity | Meaning | More |
|---|---|---|---|
| SCG0001 | Error | Shader struct must be partial | [Getting started](getting-started.md#a-compute-shader) |
| SCG0002 | Error | Type containing a shader struct must be partial | |
| SCG0003 | Error | Shader struct must implement `IComputeShader` or `IComputeShader<TPixel>` | [Getting started](getting-started.md#a-compute-shader) |
| SCG0004 | Warning | Shader struct should be readonly | |
| SCG0005 | Error | Unsupported shader struct declaration | |
| SCG0006 | Error | Unsupported captured field or parameter type (`double`, `bool`, managed arrays, ...) | [Scalar types](shader-language.md#scalar-types) |
| SCG0007 | Error | Unsupported type in shader code (`double`, `decimal`, `char`, `nint`, ...) | [Scalar types](shader-language.md#scalar-types) |
| SCG0008 | Error | Primary constructor parameter is also stored in a field | |
| SCG0009 | Error | Struct cannot be used in a shader (for example, packed below the alignment of its fields) | [Integers](shader-language.md#integers) |
| SCG0010 | Error | Buffer nested inside a struct | |
| SCG0011 | Error | Too many bound arguments | |
| SCG0012 | Error | Unsupported construct in shader code (for example, a compound negation of an integer vector whose target has side effects) | [Integers](shader-language.md#integers) |
| SCG0013 | Error | Instance method called on another instance | |
| SCG0014 | Error | Recursive helper method | [Shared code](shared-code.md#rules) |
| SCG0015 | Error | Invalid thread group size | |
| SCG0016 | Error | Method cannot be called from a shader (for example, a method of another assembly) | [Shared code](shared-code.md#rules) |
| SCG0017 | Error | Member cannot be used in a shader | |
| SCG0018 | Error | Captured shader state is read-only | |
| SCG0019 | Error | Buffer length is unavailable | |
| SCG0020 | Error | Execute method is missing | |
| SCG0021 | Error | Expression depends on C# evaluation order (through locals, buffers, `[GroupShared]` arrays, textures and helper calls) | [Evaluation order](shader-language.md#evaluation-order) |
| SCG0022 | Error | Shader implements both shader interfaces | |
| SCG0023 | Error | Unsupported pixel type (pixel shaders return `Float4`) | [Getting started](getting-started.md#a-pixel-shader) |
| SCG0024 | Error | Static field cannot be lowered to an MSL constant | [Half precision](half-precision.md#writing-half-constants) |
| SCG0025 | Error | Local function captures a local or parameter | [Shared code](shared-code.md#rules) |
| SCG0026 | Error | Static field is assigned in a static constructor | |
| SCG0027 | Error | Texture nested inside a struct | |
| SCG0028 | Error | Unsupported texture element type | [Pixel formats](resources.md#pixel-formats) |
| SCG0029 | Error | Too many textures | |
| SCG0030 | Error | Sampler must be a static property of `Sampler` passed to `Sample` | [Samplers](resources.md#samplers) |
| SCG0031 | Error | Texture element is modified in place | |
| SCG0032 | Error | Texture is returned or passed by reference | |
| SCG0033 | Error | Read-write texture cannot be sampled | [Sampling what a pass wrote](resources.md#sampling-what-a-pass-wrote) |
| SCG0034 | Error | Atomic operation needs a buffer element or a `[GroupShared]` element | [Atomics](atomics.md#rules) |
| SCG0035 | Warning | SIMD-group operations share a line | [SIMD groups](simd-groups.md#on-the-cpu) |
| SCG0036 | Error | Unsupported local array (multi-dimensional, jagged) | [Local arrays](local-arrays.md#rules) |
| SCG0037 | Error | Local array length must be a positive compile-time constant | [Local arrays](local-arrays.md#rules) |
| SCG0038 | Warning | Local array is large (over 16 KB per thread) | [Local arrays](local-arrays.md#rules) |
| SCG0039 | Error | Unsupported use of a local array | [Local arrays](local-arrays.md#rules) |
| SCG0040 | Error | Invalid `[GroupShared]` field | [Shared memory](shared-memory.md#rules) |
| SCG0041 | Error | `[GroupShared]` array length is not a compile-time constant | [Shared memory](shared-memory.md#rules) |
| SCG0042 | Error | Unsupported `[GroupShared]` element type | [Shared memory](shared-memory.md#rules) |
| SCG0043 | Error | Too much threadgroup memory (over 32,768 bytes) | [Shared memory](shared-memory.md#rules) |
| SCG0044 | Warning | `[GroupShared]` array in a shader without `[ThreadGroupSize]` | [Shared memory](shared-memory.md#rules) |
| SCG0045 | Error | Computed property is assigned in a shader | [Shared code](shared-code.md#rules) |
| SCG0046 | Error | Generic method cannot be specialized (for example, for `double`) | [Shared code](shared-code.md#rules) |
| SCG0047 | Error | No atomic operation for this element type | [Atomics](atomics.md#rules) |
| SCG0048 | Error | Wave matrix cannot be stored (in a buffer, array, field or struct) | [Matrix units](matrix-units.md#rules) |
| SCG0049 | Error | Wave matrix cannot be loaded from or stored to this memory | [Matrix units](matrix-units.md#rules) |
| SCG0050 | Error | Wave matrix in a static field | [Matrix units](matrix-units.md#rules) |
| SCG0051 | Warning | Wave matrix in a threadgroup that is not a multiple of 32 threads | [Matrix units](matrix-units.md#rules) |
| SCG0052 | Error | Checked arithmetic in a shader (once per shader under `CheckForOverflowUnderflow`) | [Integers](shader-language.md#integers) |
| SCG9999 | Error | Internal generator error | |
