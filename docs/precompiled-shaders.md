# Warm-up and precompiled shaders

A shader becomes GPU code in two steps. Its MSL is compiled to Metal's intermediate code, and the GPU
driver compiles that to machine code when the pipeline is created. Both happen the first time the
shader is used. `Prepare` moves that work off the first frame, and precompiling does the first step
when the project is built.

- [Warm-up with Prepare](#warm-up-with-prepare)
- [Precompiling at build time](#precompiling-at-build-time)
- [How it behaves](#how-it-behaves)
- [What it saves](#what-it-saves)

## Warm-up with Prepare

`Prepare` compiles a shader ahead of time, so the first frame doesn't stall:

```csharp
device.Prepare<Blur>();                                                  // now
await device.PrepareAsync(ShaderReference.Of<Blur>(), ShaderReference.Of<Sharpen>()); // in parallel, off this thread
device.Prepare<Blur>(PrepareOptions.IncludeSplit);                       // also the pipeline ForSplit runs on the GPU
```

The first time an application runs on a machine, the first frame of a large shader (a raymarched
scene of about 300 lines) takes 247 ms without `Prepare` and 12 ms with it, the 242 ms having moved
into `Prepare`. On later launches macOS has the compiled shader in its cache and the first frame
takes about 8 ms. `Prepare` moves the compile; precompiling removes part of it.

## Precompiling at build time

```xml
<PropertyGroup>
  <SharpComputePrecompileShaders>true</SharpComputePrecompileShaders>
</PropertyGroup>
<Import Project="path/to/src/SharpCompute.Build/SharpCompute.Build.targets" />
```

After the C# compiler has run, the build reads the shaders out of the assembly's metadata. It
compiles each one with the Xcode Metal Toolchain (`xcrun metal`, with the options the runtime would
use for that shader, its `[ShaderOptions]` included) and links them into `<AssemblyName>.metallib`.

The file is copied to the output folder with the assembly, to the output of every project that
references it, and into `dotnet publish` (also single-file, self-contained and Native AOT, where it
sits next to the executable).

At run time nothing changes in your code: a dispatch or a `Prepare` takes the shader's kernel from
the library instead of compiling its MSL.

## How it behaves

- **It falls back by itself.** A kernel is stored under a name that includes a hash of its MSL and
  compile options, and the runtime asks for the name of the shader it has. A library from another
  build, a shader that is not in it, a missing file, a hand-written descriptor: all of those are
  compiled from source at run time, as without the property. The results are the same bits either
  way.
- **The build does not depend on the toolchain.** Without it, the build prints a message, produces no
  library and succeeds. `xcodebuild -downloadComponent MetalToolchain` installs it. A shader the
  offline compiler rejects is reported and left to run time.
- **It is incremental.** Only shaders whose MSL or options changed are compiled, and a build in which
  the assembly did not change does not start the tool. On an M4 Pro the 16 shaders of the benchmark
  project take 0.3 s to precompile and the 160 of the test project 2.1 s (medians of 3); their
  libraries are 106 KB and 1.0 MB.
- **`device.GetPipelineOrigin<T>()`** says where a shader's pipeline came from (`Precompiled`,
  `CompiledAtRunTime`, or `None` before its first use), for a status line or a log.
- **Turning it off at run time.** Setting the `AppContext` switch
  `SharpCompute.UsePrecompiledShaders` to false before a device is created makes that device compile
  everything from source.

## What it saves

What it saves is the first step, on the first launch. The measurements: an M4 Pro, macOS 27.2,
median of 7 fresh processes. A first launch is a run from an application bundle macOS has never
seen, so with an empty shader cache. The workloads are the first frame of a large shader at
320 × 180, and `Prepare` of thirteen small kernels (the benchmark suite's own: saxpy, Mandelbrot, the
blurs, a matrix product, an n-body step):

| | First launch on a machine | Later launches |
|---|---|---|
| RaymarchedGarden (about 300 lines), compiled at run time | 247 ms | 8 ms |
| the same, with `Prepare` first | 242 ms in `Prepare`, then 12 ms | 3 ms in `Prepare`, then 6 ms |
| RaymarchedGarden, precompiled | 195 ms | 10 ms |
| the same, with `Prepare` first | 176 ms in `Prepare`, then 13 ms | 3 ms in `Prepare`, then 6 ms |
| `Prepare` of 13 small kernels, compiled at run time | 144 ms | 7 ms |
| `Prepare` of 13 small kernels, precompiled | 108 ms | 8 ms |

A steady frame of the large shader is 3 ms. Precompiling takes 52 ms off its first frame and a
quarter off the wait for the thirteen kernels. What remains is the GPU driver compiling for this
machine (more than three quarters of that first use for a large shader), which macOS does once and
caches, and which `Prepare` moves off the first frame. On later launches the cache already holds the
result, and precompiling makes no difference. `SharpCompute.Benchmarks --first-frames` prints the
full table.

Every sample in this repository is built this way. ComputeLab's Indirect and FastMath pages say
whether their kernel was precompiled when the lab was built or compiled at run time: the FastMath
page's [screenshot](shader-language.md#compile-options) shows "kernel: precompiled" under its field.

To build the repository without precompiled shaders, see [Building from source](building.md).
