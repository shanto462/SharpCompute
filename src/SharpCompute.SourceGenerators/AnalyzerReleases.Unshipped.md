; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SCG0001 | SharpCompute | Error | Shader struct must be partial
SCG0002 | SharpCompute | Error | Type containing a shader struct must be partial
SCG0003 | SharpCompute | Error | Shader struct must implement IComputeShader or IComputeShader<TPixel>
SCG0004 | SharpCompute | Warning | Shader struct should be readonly
SCG0005 | SharpCompute | Error | Unsupported shader struct declaration
SCG0006 | SharpCompute | Error | Unsupported captured field or parameter type
SCG0007 | SharpCompute | Error | Unsupported type in shader code
SCG0008 | SharpCompute | Error | Primary constructor parameter is also stored in a field
SCG0009 | SharpCompute | Error | Struct cannot be used in a shader
SCG0010 | SharpCompute | Error | Buffer nested inside a struct
SCG0011 | SharpCompute | Error | Too many bound arguments
SCG0012 | SharpCompute | Error | Unsupported construct in shader code
SCG0013 | SharpCompute | Error | Instance method called on another instance
SCG0014 | SharpCompute | Error | Recursive helper method
SCG0015 | SharpCompute | Error | Invalid thread group size
SCG0016 | SharpCompute | Error | Method cannot be called from a shader
SCG0017 | SharpCompute | Error | Member cannot be used in a shader
SCG0018 | SharpCompute | Error | Captured shader state is read-only
SCG0019 | SharpCompute | Error | Buffer length is unavailable
SCG0020 | SharpCompute | Error | Execute method is missing
SCG0021 | SharpCompute | Error | Expression depends on C# evaluation order
SCG0022 | SharpCompute | Error | Shader implements both shader interfaces
SCG0023 | SharpCompute | Error | Unsupported pixel type
SCG0024 | SharpCompute | Error | Static field cannot be lowered to an MSL constant
SCG0025 | SharpCompute | Error | Local function captures a local or parameter
SCG0026 | SharpCompute | Error | Static field is assigned in a static constructor
SCG0027 | SharpCompute | Error | Texture nested inside a struct
SCG0028 | SharpCompute | Error | Unsupported texture element type
SCG0029 | SharpCompute | Error | Too many textures
SCG0030 | SharpCompute | Error | Sampler must be a static property of Sampler passed to Sample
SCG0031 | SharpCompute | Error | Texture element is modified in place
SCG0032 | SharpCompute | Error | Texture is returned or passed by reference
SCG0033 | SharpCompute | Error | Read-write texture cannot be sampled
SCG0034 | SharpCompute | Error | Atomic operation needs a buffer element or a [GroupShared] element
SCG0035 | SharpCompute | Warning | SIMD-group operations share a line
SCG0036 | SharpCompute | Error | Unsupported local array
SCG0037 | SharpCompute | Error | Local array length must be a positive compile-time constant
SCG0038 | SharpCompute | Warning | Local array is large
SCG0039 | SharpCompute | Error | Unsupported use of a local array
SCG0040 | SharpCompute | Error | Invalid [GroupShared] field
SCG0041 | SharpCompute | Error | [GroupShared] array length is not a compile-time constant
SCG0042 | SharpCompute | Error | Unsupported [GroupShared] element type
SCG0043 | SharpCompute | Error | Too much threadgroup memory
SCG0044 | SharpCompute | Warning | [GroupShared] array in a shader without [ThreadGroupSize]
SCG0045 | SharpCompute | Error | Computed property is assigned in a shader
SCG0046 | SharpCompute | Error | Generic method cannot be specialized
SCG0047 | SharpCompute | Error | No atomic operation for this element type
SCG0048 | SharpCompute | Error | Wave matrix cannot be stored
SCG0049 | SharpCompute | Error | Wave matrix cannot be loaded from or stored to this memory
SCG0050 | SharpCompute | Error | Wave matrix in a static field
SCG0051 | SharpCompute | Warning | Wave matrix in a threadgroup that is not a multiple of 32 threads
SCG9999 | SharpCompute | Error | Internal generator error
