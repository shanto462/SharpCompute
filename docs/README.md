# SharpCompute documentation

Start with [Getting started](getting-started.md). The [README](../README.md) has the overview, the
demos and the headline numbers.

## Writing shaders

| Page | What it covers |
|---|---|
| [Getting started](getting-started.md) | Requirements, project references, a compute shader and a pixel shader, what a shader may contain |
| [Shader language](shader-language.md) | Scalar types, integer semantics and overflow, vectors, bool vectors, matrices, bits, unorm8 packing, compile options, evaluation order |
| [Half precision](half-precision.md) | `Half2..4`: what they compute on the GPU and the CPU, and what they buy |
| [Local arrays](local-arrays.md) | `stackalloc`, `new T[N]`, constant tables and inline arrays inside a thread |
| [Sharing code between shaders](shared-code.md) | Static methods, generic methods, and struct methods, operators and constructors |
| [Diagnostics](diagnostics.md) | Every `SCG` build error and warning |

## Data and dispatch

| Page | What it covers |
|---|---|
| [Resources](resources.md) | Buffers, zero-copy paths, views, copy/fill/clear, textures, pixel formats, samplers |
| [Dispatch](dispatch.md) | `ComputeContext` batching, `ForAsync`, indirect dispatch |
| [Loading and saving images](imaging.md) | `SharpCompute.Imaging`: PNG, JPEG and HEIC to and from textures |
| [Unified memory](unified-memory.md) | When sharing memory between the CPU and the GPU saves time, and when it doesn't |

## Threads working together

| Page | What it covers |
|---|---|
| [Shared memory and barriers](shared-memory.md) | `[GroupShared]` arrays, barriers, the group built-ins |
| [SIMD-group operations](simd-groups.md) | `Gpu.Wave*` and `Gpu.Quad*`: votes, reductions, scans, shuffles |
| [Atomics](atomics.md) | `Gpu.Atomic*` on buffer and threadgroup elements |

## Running and shipping

| Page | What it covers |
|---|---|
| [CPU execution](cpu-execution.md) | `ForCpu` and `ForSplit`, and how each feature behaves on the CPU |
| [Interop with existing Metal code](interop.md) | `FromNative`, wrapping native buffers and textures, `EncodeInto`, native handles |
| [Warm-up and precompiled shaders](precompiled-shaders.md) | `Prepare`, and `.metallib` libraries built with the project |
| [Performance](performance.md) | Benchmarks against the CPU, zero-copy paths, where the other numbers are |
| [Limitations](limitations.md) | The full list |
| [Building and testing](building.md) | Building the repository, generated sources, demo pictures |
