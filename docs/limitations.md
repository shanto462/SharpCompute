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
- **`checked` arithmetic** (a `checked(...)` expression, a `checked { }` block or a project built
  with `CheckForOverflowUnderflow`) is build error `SCG0052`: it throws `OverflowException` in C#
  and with `ForCpu`, and the GPU has no exceptions. Write it `unchecked`, which wraps on both.
  ([Integers](shader-language.md#integers))
- **A Metal compiler bug drops a lane of integer vector arithmetic**: when an integer vector is
  negated, a lane of the negation is never read, and the same vector is subtracted in an
  add/subtract/multiply chain elsewhere, that chain loses the lane on the GPU (not with `ForCpu`).
  The generator avoids it for every negation it can see (`-v`, `0 - v`, `v * -1`, `v / -1`, and a
  constant with some lanes of 0 or -1). A negation it cannot see is still exposed until Apple fixes
  the compiler: `~v + 1`, `v - 2 * v` and other arithmetic that only amounts to a negation. Write
  those as `-v`. Reported to Apple as FB25732151 (Metal compiler 32023.922, Xcode 27.2, macOS 27.2.1).
  ([Integers](shader-language.md#integers))
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

## Matrix units

- **Wave matrices** are 8 × 8 and `float` or `Half`, the one size and the two element types Metal's
  matrix units have. A matrix needs all 32 lanes of its SIMD group and the same arguments in each;
  the GPU does not check that, `ForCpu` refuses anything else. Sizes that are not a multiple of 8
  (of the kernel's tile) are the caller's to pad. The terms of a load's or store's offset are summed
  without wrapping on the GPU; where C# would wrap the sum, the CPU reports the offset as out of
  range. ([Matrix units](matrix-units.md#rules))

## Resources and dispatch

- **Atomics** work on elements of `int`, `uint` and (add only) `float`, in buffers and in
  `[GroupShared]` arrays, and (minimum and maximum only) on `ulong` buffer elements. Not on struct
  fields, and not on `long` or the 8- and 16-bit integers, which have no atomics in MSL.
  ([Atomics](atomics.md#rules))
- **One binding per written resource.** A dispatch whose shader writes a buffer or texture can bind
  that memory only once: the same buffer or texture as two captures, overlapping views, or a texture
  with its `AsReadOnly()` view throw an `ArgumentException` when one of them is written, because the
  GPU would read stale data. Update in place through one capture, or ping-pong between two. Two
  different `MTLTexture` objects that share storage without a CPU-visible buffer behind them (a
  texture view other Metal code made of a wrapped texture, a linear texture over a private buffer)
  are not recognized as the same memory. ([Overlapping bindings](resources.md#overlapping-bindings))
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
