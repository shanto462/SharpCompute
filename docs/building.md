# Building from source

How to build SharpCompute and run its demos from a clone. To change SharpCompute itself (tests,
code style, diagnostics, screenshots, pull requests), see [CONTRIBUTING.md](../CONTRIBUTING.md).

## Requirements

- macOS 15 or later on Apple Silicon.
- The .NET 11 SDK (the version is pinned in [`global.json`](../global.json)).
- Optional: the Xcode Metal Toolchain, to precompile shaders at build time.
  `xcodebuild -downloadComponent MetalToolchain` installs it.

## Build

```sh
git clone https://github.com/shanto462/SharpCompute.git
cd SharpCompute
dotnet build SharpCompute.slnx
```

The samples, the tests and the benchmarks precompile their shaders when the Xcode Metal Toolchain is
installed, and compile them at run time when it is not; the tests that need a precompiled library
are then skipped. `-p:SharpComputePrecompileShaders=false` builds everything without precompiled
shaders. See [Precompiled shaders](precompiled-shaders.md).

## Run the demos

Each `scripts/demo-<name>.sh` builds its sample in Release, writes to `demo-output/` and opens the
result (`NO_OPEN=1` to skip):

```sh
scripts/demo-helloworld.sh        # MultiplyByTwo, SAXPY and a 1080p Mandelbrot
scripts/demo-computelab.sh        # one page per compute feature, in a window
scripts/run-demos.sh              # everything that doesn't need a window
scripts/run-benchmarks.sh         # the GPU vs CPU benchmarks, written to benchmarks/RESULTS.md
```

The README's [Demos](../README.md#demos) section lists every demo with its options and keys.
