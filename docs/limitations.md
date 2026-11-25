# Limitations

Everything SharpCompute does not do yet, or does differently from C#, in one list. Each item links
to the page that explains it.

## Platform

- Apple Silicon and macOS 15+ only, for now. Vulkan and OpenGL backends are planned; see the
  [roadmap](../README.md#roadmap).
- There is no NuGet package yet: reference the projects directly
  ([Getting started](getting-started.md#add-sharpcompute-to-a-project)).

## Shader language

- **Integers.** The 8-, 16- and 64-bit integers are scalars only: there are no vectors of them, the
  SIMD-group operations take `float`, `int`, `uint` and `Half`, and a `static readonly` initializer
  can use their operators and literals but not call an intrinsic on them.
  ([Integers](shader-language.md#integers))
- **`checked` arithmetic** throws `OverflowException` with `ForCpu` but wraps on the GPU, which has no
  exceptions. ([Integers](shader-language.md#integers))
- **Half precision.** The math functions of `Gpu.*` take half *vectors*. For a scalar `Half`, convert
  to `float` or put it in a vector (only `Radians`, `Degrees`, the SIMD-group operations and the bit
  casts take a scalar `Half`). The bits of a half reinterpret as `ushort` and `short`, which are
  scalars: a vector form of the bit casts waits for 16-bit integer vectors (`Short2..4`,
  `UShort2..4`). A `static readonly` half or bool value can be initialized with constructors, casts
  and operators, but not with a function call (`SCG0024`): the compile-time evaluation only does
  32-bit arithmetic. ([Half precision](half-precision.md#functions-that-take-a-scalar-half))
- **GPU math** follows Apple GPU behaviour, which flushes subnormal floats (not halves) to zero and
  fuses multiply-adds (`[ShaderOptions(FpContract = false)]` turns the fusing off for one shader).
  Transcendental functions agree with the CPU to within a few ULP.
  ([Compile options](shader-language.md#compile-options))
- **Local arrays** have a compile-time length and one name: they cannot be sliced, copied to another
  variable or returned (pass them to a helper instead), and multi-dimensional and jagged arrays are
  not supported. ([Local arrays](local-arrays.md#rules))
- **Shared shader code** must be C# source in the shader's own project: methods of referenced
  assemblies are not translated. Of a struct's members, methods, getters, operators, conversions and
  constructors are translated; property setters with a body, object initializers (`new T { ... }`),
  `with` expressions, user-defined `++`/`--`, generic structs and interface calls are not. A
  by-reference (`ref`/`out`) argument has to be a local or a parameter, not a buffer element.
  ([Sharing code](shared-code.md#rules))

## Resources and dispatch

- **Atomics** work on elements of `int`, `uint` and (add only) `float`, in buffers and in
  `[GroupShared]` arrays, and (minimum and maximum only) on `ulong` buffer elements. Not on struct
  fields, and not on `long` or the 8- and 16-bit integers, which have no atomics in MSL.
  ([Atomics](atomics.md#rules))
- **Textures** are 2D with one mip level (a wrapped mipmapped texture uses level 0); samplers are the
  six fixed ones. ([Textures](resources.md#textures))
- **An indirect dispatch runs whole threadgroups**, so `DispatchSize` is a multiple of the
  threadgroup size and the shader has to check its index against the real count.
  ([Indirect dispatch](dispatch.md#indirect-dispatch))
- **Wrapped Metal resources** must use tracked hazard tracking, and `EncodeInto` cannot detect an
  encoder the app left open on its command buffer. ([Interop](interop.md))

## CPU execution

- **SIMD-group operations on the CPU** need uniform control flow. `ForCpu` refuses lanes that arrive
  at different operations, and cannot tell lanes apart that reach one call site along different paths
  (a loop iteration some lanes skip, a helper called from two branches). The GPU handles both.
  ([SIMD groups on the CPU](simd-groups.md#on-the-cpu))
- **Shared memory and SIMD groups on the CPU** are for checking and debugging, not bulk work: they
  are thousands of times slower than the GPU. ([CPU execution](cpu-execution.md#feature-by-feature))

## Imaging

- `SharpCompute.Imaging` loads the first image of a file (not the other frames of a GIF or HEIC
  sequence) and saves one image. ImageIO does not report a file cut off inside its pixel data, so
  such a file loads with the missing part zero. An oriented image with translucent pixels goes
  through ImageIO's premultiplied thumbnail path and can lose a little precision in those pixels.
  ([Imaging](imaging.md#limitations))

## Precompiled shaders

- A precompiled library holds Metal's intermediate code, not machine code: the GPU driver still
  compiles each shader the first time it is used on a machine (more than three quarters of that first
  use for a large shader). Building the library needs the Xcode Metal Toolchain on the build machine.
  ([Precompiled shaders](precompiled-shaders.md#what-it-saves))
