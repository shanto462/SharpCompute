# SharpCompute

**Write GPU compute shaders in C# and run them on the GPU.** A Roslyn source generator translates
shader structs to the GPU's shading language at build time; a small, dependency-free runtime
compiles, caches and dispatches them. Inspired by [ComputeSharp](https://github.com/Sergio0694/ComputeSharp).

Metal on Apple Silicon today. Vulkan and OpenGL backends are planned.

![Shader gallery rendered by SharpCompute](docs/gallery.png)

```csharp
using SharpCompute;

[GeneratedComputeShaderDescriptor]
public readonly partial struct MultiplyByTwo(ReadWriteBuffer<float> buffer) : IComputeShader
{
    public void Execute() => buffer[ThreadIds.X] *= 2;
}

GraphicsDevice device = GraphicsDevice.GetDefault();
using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>([1, 2, 3, 4]);
device.For(buffer.Length, new MultiplyByTwo(buffer));
float[] result = buffer.ToArray(); // [2, 4, 6, 8]
```

> **Status: early development.** APIs may change, and there is no NuGet package yet; reference the
> projects directly (see [Using it in your project](#using-it-in-your-project)).

## Features

- **Shaders are plain C#.** `Execute()` is translated to Metal Shading Language at build time, with
  binding code generated for you. Unsupported C# is a build error (`SCGxxxx`) pointing at the exact
  construct.
- **Two shader kinds.** `IComputeShader` for general compute over 1D/2D/3D grids
  (`device.For(x, y, z, shader)`), and `IComputeShader<Float4>` pixel shaders that return a color per
  pixel into a texture (`device.ForEach(texture, shader)`).
- **HLSL-like shader library.** `Float2..4`, `Int2..4`, `UInt2..4` with every swizzle,
  `Float2x2`/`Float3x3` with `Gpu.Mul`, `Half`, user structs, and about 40 `Gpu.*` intrinsics (`MathF.*`
  works too). Static and instance helper methods, local functions, `ref`/`out` parameters, all loops
  and branches, and compile-time-folded `static readonly` constants.
- **Zero-copy buffers.** Buffers live in unified memory, so `AsSpan()` and indexers read GPU
  results directly.
- **Correct by default.** Shaders compile with IEEE-safe math and precise functions, and every
  shader that passes structs or constants carries `static_assert` layout guards. The CPU
  implementations of `Gpu.*` were measured against Apple GPU results: bit-for-bit for arithmetic,
  rounding functions, `sqrt` and `fmod`, and within a few ULP for transcendentals.
- **Live presentation.** `MetalWindow` shows GPU output in a native macOS window at display refresh
  rate, with keyboard and mouse input.
- **No dependencies, AOT-friendly.** The runtime talks to Metal through hand-written
  `LibraryImport` interop.

## Demos

Each script builds in Release, writes to `demo-output/` and opens the result (`NO_OPEN=1` to skip).

| Script | What it shows |
|---|---|
| `scripts/demo-viewer.sh` | **Live window:** every gallery shader, Game of Life and a 16k-body N-body simulation. ←/→ switch, Space pause, S save PNG, Esc quit |
| `scripts/demo-paint.sh` | **Live window:** a GPU paint app with brushes, filters and undo. `--replay` paints a scripted scene |
| `scripts/demo-gallery.sh` | The shader gallery as PNGs (`--video` for mp4s, `--shader <name>` for one) plus a benchmark |
| `scripts/demo-mandelbrot.sh` | A deep-zoom Mandelbrot video (perturbation theory, down to ~1e-30) |
| `scripts/demo-imgproc.sh [photo] [filter args]` | GPU vs CPU image filters on a 4K test image or your own photo (JPEG/PNG/HEIC) |
| `scripts/demo-helloworld.sh` | MultiplyByTwo, SAXPY over 1M floats and a 1080p Mandelbrot |
| `scripts/run-benchmarks.sh` | The GPU vs CPU benchmark suite, written to `benchmarks/RESULTS.md` |
| `scripts/run-demos.sh` | Everything that doesn't need a window (`--video`, `--test`) |

The gallery includes ports of the Shadertoy shaders from ComputeSharp's samples. Those ports are
licensed **CC BY-NC-SA 3.0**, not MIT; see [`samples/SharpCompute.Samples.Shaders/NOTICE.md`](samples/SharpCompute.Samples.Shaders/NOTICE.md).

## Performance

Apple M4 Pro, GPU vs the **fastest** CPU variant (multi-threaded and SIMD where it applies). The full
report, including how each number was measured and the machine load, is in
[`benchmarks/RESULTS.md`](benchmarks/RESULTS.md).

| Workload | Size | Fastest CPU | GPU | GPU speedup |
|---|---|---:|---:|---:|
| Mandelbrot (512 iterations) | 1920×1080 | 9.16 ms | 0.54 ms | **17×** |
| Mandelbrot (512 iterations) | 256×256 | 1.00 ms | 0.15 ms | **6.8×** |
| N-body step | 16,384 bodies | 13.8 ms | 3.95 ms | **3.5×** |
| Matrix multiply (naive) | 1024×1024 | 13.7 ms | 4.39 ms | **3.1×** |
| Gaussian blur, radius 8 | 3840×2160 | 2.25 ms | 1.68 ms | **1.3×** |
| SAXPY `y = a·x + y` | 16M floats | 0.85 ms | 1.01 ms | 0.84× (CPU wins) |

How to read this: GPU times are one synchronous dispatch and exclude uploading inputs and reading
results back (the image-processing demo reports both, and its end-to-end speedups are much lower for
light filters). Every dispatch has a fixed cost of roughly 0.1 ms, and Apple Silicon's CPU and GPU
share the same memory. Compute-heavy work wins big; memory-bound work like SAXPY doesn't. The
generated SAXPY kernel runs at the same speed as hand-written MSL (ratio 0.99–1.01).

## Using it in your project

Requirements: macOS 15 or later on Apple Silicon, and the .NET 11 SDK.

Reference the runtime, and the generator as an analyzer:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/src/SharpCompute/SharpCompute.csproj" />
  <ProjectReference Include="path/to/src/SharpCompute.SourceGenerators/SharpCompute.SourceGenerators.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

A pixel shader, rendered into a texture:

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

## Building and testing

```sh
dotnet build SharpCompute.slnx
dotnet test SharpCompute.slnx
```

The vector, matrix and intrinsic sources are generated by `tools/gen_vectors.py`
(`python3 -I tools/gen_vectors.py --check` verifies they're up to date).

## Limitations

- Apple Silicon and macOS 15+ only, for now.
- No threadgroup shared memory, atomics, input textures or asynchronous/batched dispatch yet.
- 8- and 16-bit integer fields aren't supported inside shaders; pack them into `uint`.
- GPU math follows Apple GPU behaviour, which flushes subnormal floats to zero and fuses
  multiply-adds. Transcendental functions agree with the CPU to within a few ULP.

## Roadmap

- **Vulkan** (Windows, Linux, Android; macOS through MoltenVK) and **OpenGL 4.3** (Windows, Linux).
  macOS caps OpenGL at 4.1, which has no compute shaders.
- Threadgroup memory, atomics, input textures, batched dispatch.
- NuGet packages.

## License

[MIT](LICENSE), except the Shadertoy shader ports in `samples/SharpCompute.Samples.Shaders`
(CC BY-NC-SA 3.0, see its NOTICE). Inspired by ComputeSharp (MIT, © Sergio Pedri), whose sample
ports the gallery adapts.
