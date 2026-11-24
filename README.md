# SharpCompute

**Write GPU compute shaders in C# and run them on Metal, Vulkan and OpenGL.** A Roslyn source
generator translates shader structs to the GPU's shading language at build time; a small,
dependency-free runtime compiles, caches and dispatches them.

[ComputeSharp](https://github.com/Sergio0694/ComputeSharp) pioneered this model for **DirectX 12 on
Windows**. SharpCompute's goal is to bring the same experience to the **other GPU APIs**, so the
same C# shader code can run on macOS, Linux, Android and more:

| Backend | Platforms | Status |
|---|---|---|
| **Metal** | macOS on Apple Silicon (iOS later) | ✅ Available |
| **Vulkan** | Windows, Linux, Android; macOS via MoltenVK | 🛠 Planned, next |
| **OpenGL 4.3** | Windows, Linux | 🛠 Planned |
| DirectX 12 | Windows | Use [ComputeSharp](https://github.com/Sergio0694/ComputeSharp) |

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
  results directly, and data can be produced straight into GPU memory (an image decoder can write
  into `AsSpan()`). `MapReadOnlyBuffer` puts a file on the GPU without reading it into memory first,
  and `WrapReadWriteBuffer` makes a buffer over page-aligned memory you already own.
- **Batched and asynchronous dispatch.** A `ComputeContext` runs many dispatches with one commit and
  one wait; `ForAsync`/`ForEachAsync` return a `Task` so the CPU keeps working while the GPU runs.
- **CPU execution and CPU+GPU split.** The same shader also runs as C# on all CPU cores
  (`device.ForCpu`), or split between the CPU and the GPU in one dispatch (`device.ForSplit`).
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
| `scripts/demo-pipeline.sh` | A double-buffered pipeline: the GPU renders frame N+1 (`ForAsync`) while the CPU post-processes frame N; sync vs async throughput |
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
results back. Every dispatch has a fixed cost of roughly 0.1 ms, and Apple Silicon's CPU and GPU
share the same memory. Compute-heavy work wins big; memory-bound work like SAXPY doesn't. The
generated SAXPY kernel runs at the same speed as hand-written MSL (ratio 0.99–1.01).

Moving and converting data can cost more than the kernel, and unified memory means it can be
skipped: decode into the GPU buffer, convert and filter on the GPU, encode from the buffer.

| Data path | Size | Before | Zero copy | Speedup |
|---|---|---:|---:|---:|
| RGBA8 image → blur → RGBA8, CPU conversions + Float4 copies (fastest CPU: 21.8 ms) | 3840×2160 | 12.2 ms | 3.10 ms | **3.9×** (7.0× vs CPU) |
| Same kernels, only the copies of the bytes in and out removed | 3840×2160 | 4.28 ms | 3.10 ms | 1.4× |
| File of floats → GPU sum (`MapReadOnlyBuffer`) | 256 MB | 42.0 ms (`ReadAllBytes` + upload) | 4.06 ms | **10.3×** |

### Unified memory: when it helps and when it doesn't

On Apple silicon the CPU and the GPU share the same memory, so there is no upload or download over a
bus. The gains come from not copying at all and from overlapping work. They don't come from extra
bandwidth: both sides use the same memory controller.

| Helps | Measured | Doesn't help | Why |
|---|---|---|---|
| Decode or read straight into GPU memory, convert on the GPU | 4K image pipeline 12.2 → 3.10 ms | Memory-bound kernels (SAXPY) | Same bandwidth; the CPU ties or wins |
| `MapReadOnlyBuffer` instead of reading a file | 256 MB: 42.0 → 4.06 ms | Tiny one-off dispatches | ~0.1 ms floor per dispatch; the CPU finishes first |
| Many small dispatches in one `ComputeContext` | ~99 µs → ~1.9 µs per dispatch | Single heavy kernels | Nothing to copy or batch |
| `ForAsync` double-buffering, CPU and GPU in parallel | 1.38× (7 % above the ideal) | GPU-private buffers | Same speed as shared on Apple silicon |
| | | Splitting one grid between CPU and GPU (`ForSplit`) | 0.97–1.09× of GPU-only: scalar C# on the CPU, and the GPU slows while the CPU is busy |

The full guide, with how to choose an API and the lifetime rules that come with zero copy, is
[`docs/unified-memory.md`](docs/unified-memory.md).

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

Zero-copy data paths (the CPU and the GPU share the same memory, so skip the copies):

```csharp
// Write input straight into GPU memory, e.g. decode or read a file into it.
using ReadWriteBuffer<uint> pixels = device.AllocateReadWriteBuffer<uint>(width * height);
DecodeInto(MemoryMarshal.AsBytes(pixels.AsSpan()));

// A file of raw floats, read by the GPU from the page cache: no ReadAllBytes, no upload.
using ReadOnlyBuffer<float> samples = device.MapReadOnlyBuffer<float>("samples.f32");

// Memory you already own, if it starts on a page boundary (GraphicsDevice.PageSize).
void* memory = NativeMemory.AlignedAlloc(byteCount, (nuint)GraphicsDevice.PageSize);
using ReadWriteBuffer<float> wrapped = device.WrapReadWriteBuffer<float>((nint)memory, count, () => NativeMemory.AlignedFree(memory));
```

### Batching and async dispatch

Every `device.For` call is its own command buffer, commit and wait, which costs about 0.1 ms even for
a tiny kernel. A `ComputeContext` records several dispatches into one command buffer and runs them
together when it is disposed (or on `Execute()`), so the fixed cost is paid once:

```csharp
using (ComputeContext context = device.CreateComputeContext())
{
    context.For(width, height, new BlurHorizontal(source, temp, weights, width, radius));
    context.For(width, height, new BlurVertical(temp, output, weights, width, height, radius));
} // one commit, one wait; the vertical pass sees everything the horizontal pass wrote
```

Dispatches in a context run in recording order and each sees the writes of the ones before it (no
barriers needed), constants are captured when a dispatch is recorded, and every bound buffer and
texture stays alive until the GPU is done, even if it is disposed first. A context is for one thread
at a time; use one per thread. `ExecuteAsync()` and `await using` submit without blocking.

`ForAsync` and `ForEachAsync` commit one dispatch and return a `Task` that completes when the GPU
has finished (or faults with `MetalException`), so the CPU can work on the previous result meanwhile:

```csharp
Task rendering = device.ForAsync(width, height, new RenderFrame(frames[0], 0));

for (int k = 0; k < frameCount; k++)
{
    await rendering; // frame k is ready in frames[k % 2]

    if (k + 1 < frameCount)
    {
        rendering = device.ForAsync(width, height, new RenderFrame(frames[(k + 1) % 2], k + 1));
    }

    Consume(frames[k % 2]); // CPU work on frame k overlaps the GPU rendering frame k + 1
}
```

Work on one device runs in submission order, so dependent `ForAsync` calls can also be issued back
to back and awaited once. Measurements are in the "Batching and async" section of
[`benchmarks/RESULTS.md`](benchmarks/RESULTS.md).

### On the CPU, or on both

The C# of a shader is real code: `ForCpu` runs it on all CPU cores, with `ThreadIds`,
`DispatchSize` and friends returning per-thread values, and buffers read and written in place.
`ForSplit` runs the leading rows of the grid on the CPU while the GPU runs the rest, on the same
shared buffers, and returns when both are done:

```csharp
device.ForCpu(width, height, new Blur(source, destination));       // CPU only
device.ForSplit(width, height, new Blur(source, destination));     // adaptive CPU share
device.ForSplit(width, height, new Blur(source, destination), 0.25); // a quarter of the rows on the CPU
device.ForEachSplit(texture, new Gradient(time: 1.5f));            // pixel shaders too
```

Both parts see the whole grid (`ThreadIds`, `DispatchSize`, `ThreadIds.Normalized`). Results match
the GPU up to floating-point rounding. Only split shaders whose threads write their own elements.
Don't expect much from the split: the CPU runs the shader's scalar C#, and the GPU slows down while
every CPU core is busy. The adaptive mode therefore only splits while that measures faster than the
GPU alone, and on the benchmarked workloads it lands between 3 % slower and 9 % faster than GPU-only
([`benchmarks/RESULTS.md`](benchmarks/RESULTS.md#cpu-execution-and-cpugpu-split)). `ForCpu` is a
correct fallback and a debugging aid rather than a fast CPU path.

## Building and testing

```sh
dotnet build SharpCompute.slnx
dotnet test SharpCompute.slnx
```

The vector, matrix and intrinsic sources are generated by `tools/gen_vectors.py`
(`python3 -I tools/gen_vectors.py --check` verifies they're up to date).

## Limitations

- Apple Silicon and macOS 15+ only, for now.
- No threadgroup shared memory, atomics or input textures yet.
- 8- and 16-bit integer fields aren't supported inside shaders; pack them into `uint`.
- GPU math follows Apple GPU behaviour, which flushes subnormal floats to zero and fuses
  multiply-adds. Transcendental functions agree with the CPU to within a few ULP.

## Roadmap

- **Vulkan backend** (Windows, Linux, Android; macOS through MoltenVK), then **OpenGL 4.3**
  (Windows, Linux; macOS caps OpenGL at 4.1, which has no compute shaders). The shader authoring
  API stays the same; each backend adds its own shading-language emitter and runtime.
- Threadgroup memory, atomics, input textures.
- NuGet packages.

## License

[MIT](LICENSE), except the Shadertoy shader ports in `samples/SharpCompute.Samples.Shaders`
(CC BY-NC-SA 3.0, see its NOTICE). Inspired by ComputeSharp (MIT, © Sergio Pedri), whose sample
ports the gallery adapts.
