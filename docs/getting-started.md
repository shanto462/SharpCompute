# Getting started

This page takes you from an empty project to a shader that runs on the GPU, and lists what a
shader may contain. The other pages of the [documentation](README.md) go into each topic.

- [Requirements](#requirements)
- [Add SharpCompute to a project](#add-sharpcompute-to-a-project)
- [A compute shader](#a-compute-shader)
- [A pixel shader](#a-pixel-shader)
- [What a shader can contain](#what-a-shader-can-contain)
- [Accuracy by default](#accuracy-by-default)
- [Showing results in a window](#showing-results-in-a-window)

## Requirements

- macOS 15 or later on Apple Silicon.
- The .NET 11 SDK (the version is pinned in [`global.json`](../global.json)).
- Optional: the Xcode Metal Toolchain, to [precompile shaders](precompiled-shaders.md) at build
  time. Without it, shaders are compiled when they are first used.

There is no NuGet package yet, so you reference the projects directly.

## Add SharpCompute to a project

Reference the runtime, and the generator as an analyzer. Add `SharpCompute.Imaging` as well if you
want to [load and save images](imaging.md):

```xml
<ItemGroup>
  <ProjectReference Include="path/to/src/SharpCompute/SharpCompute.csproj" />
  <ProjectReference Include="path/to/src/SharpCompute.SourceGenerators/SharpCompute.SourceGenerators.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  <ProjectReference Include="path/to/src/SharpCompute.Imaging/SharpCompute.Imaging.csproj" />
</ItemGroup>
```

To have the shaders compiled when the project is built instead of when they are first used, add
`<SharpComputePrecompileShaders>true</SharpComputePrecompileShaders>` and import
`src/SharpCompute.Build/SharpCompute.Build.targets`. See [Precompiled shaders](precompiled-shaders.md).

## A compute shader

A shader is a `readonly partial struct` marked `[GeneratedComputeShaderDescriptor]`. Its fields (or
primary constructor parameters) are what it captures: buffers, textures and constants. At build time
the generator translates `Execute()` to Metal Shading Language (MSL) and writes the binding code.
This is a whole `Program.cs` (top-level statements come before the type):

```csharp
using SharpCompute;

GraphicsDevice device = GraphicsDevice.GetDefault();
using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>([1, 2, 3, 4]);
device.For(buffer.Length, new MultiplyByTwo(buffer));
float[] result = buffer.ToArray(); // [2, 4, 6, 8]

[GeneratedComputeShaderDescriptor]
public readonly partial struct MultiplyByTwo(ReadWriteBuffer<float> buffer) : IComputeShader
{
    public void Execute() => buffer[ThreadIds.X] *= 2;
}
```

`IComputeShader` is general compute over a 1D, 2D or 3D grid: `device.For(x, shader)`,
`device.For(x, y, shader)` or `device.For(x, y, z, shader)`. Inside `Execute`, `ThreadIds` says
which thread this is and `DispatchSize` how large the grid is.

C# that has no MSL equivalent is a build error with an `SCGxxxx` id that points at the exact
construct. The ids are listed in [Diagnostics](diagnostics.md).

## A pixel shader

An `IComputeShader<Float4>` returns a color per pixel, written into a texture by
`device.ForEach(texture, shader)`:

```csharp
[GeneratedComputeShaderDescriptor]
public readonly partial struct Gradient(float time) : IComputeShader<Float4>
{
    public Float4 Execute()
    {
        Float2 uv = ThreadIds.Normalized.XY;
        Float3 color = new Float3(0.5f, 0.5f, 0.5f) + (0.5f * Gpu.Cos(new Float3(time + uv.X, time + uv.Y, time + 4f)));
        return new Float4(color, 1f);
    }
}

using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(1280, 720);
device.ForEach(texture, new Gradient(time: 1.5f));
Rgba32[] pixels = texture.ToArray();
```

Textures, samplers and pixel formats are covered in [Resources](resources.md#textures).

## What a shader can contain

| Area | What works | Details |
|---|---|---|
| Types | `float`, `Half`, every integer width from `byte` to `ulong`, `bool`; `Float2..4`, `Half2..4`, `Int2..4`, `UInt2..4` with every swizzle; `Bool2..4` from componentwise comparisons; `Float2x2`, `Float3x3`, `Float4x4` with `Gpu.Mul`; your own structs | [Shader language](shader-language.md) |
| Functions | About 60 `Gpu.*` intrinsics modelled on HLSL. `MathF.*`, `BitOperations.*` and `BitConverter.*` work too | [Shader language](shader-language.md), and the doc comments of `Gpu` |
| Code | Static and instance helper methods, local functions, `ref`/`out` parameters, all loops and branches, and `static readonly` constants folded at compile time | [Shader language](shader-language.md) |
| Shared code | Static methods of any class, generic methods, and the methods, properties, operators and constructors of your structs | [Sharing code between shaders](shared-code.md) |
| Local arrays | `stackalloc`, `new T[N]`, collection expressions and `[InlineArray(N)]` structs | [Local arrays](local-arrays.md) |
| Resources | Buffers and buffer views, 2D textures in eight pixel formats, six samplers | [Resources](resources.md) |
| Threads working together | `[GroupShared]` memory and barriers, SIMD-group (wave) operations, wave matrices on the matrix units, atomics | [Shared memory](shared-memory.md), [SIMD groups](simd-groups.md), [Matrix units](matrix-units.md), [Atomics](atomics.md) |

The same C# also runs on the CPU, with `device.ForCpu` or split between the CPU and the GPU with
`device.ForSplit`. See [CPU execution](cpu-execution.md).

## Accuracy by default

- Shaders compile with IEEE-safe math and precise math functions. A shader opts into fast math, or
  out of fused multiply-adds, with [`[ShaderOptions]`](shader-language.md#compile-options).
- Every shader that passes structs or constants carries `static_assert` layout guards, so a layout
  that drifted between C# and MSL is a compile error, not corrupt data.
- The CPU implementations of `Gpu.*` were measured against Apple GPU results. They are bit-for-bit
  for arithmetic, rounding functions, `sqrt` and `fmod`, and within a few ULP for transcendentals.
  [Half precision](half-precision.md) is bit-for-bit as well, down to the subnormals.

## Showing results in a window

`MetalWindow` shows GPU output in a native macOS window at the display's refresh rate, with keyboard
and mouse input:

- `KeyDown` and `KeyUp` events, plus `IsKeyDown(key)` and `HeldModifiers` to poll what is held each
  frame. Held keys are released automatically when the window loses focus.
- `MouseWheel` reports wheel and trackpad scrolling, with the pointer position in pixels.

The [demos](../README.md#demos) that open a window all use it.

The runtime has no dependencies beyond the system frameworks. It talks to Metal through hand-written
`LibraryImport` interop, which keeps it AOT-friendly.
