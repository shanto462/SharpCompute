# NOTICE — third-party shader ports

The shaders in this folder (`samples/SharpCompute.Samples.Shaders/`) are **not** covered by
SharpCompute's MIT license.

## Shadertoy originals: CC BY-NC-SA 3.0

Each shader here is a port of a shader published on [Shadertoy](https://www.shadertoy.com). Unless
its author states otherwise, a Shadertoy shader is licensed under the
[Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported license](https://creativecommons.org/licenses/by-nc-sa/3.0/)
(CC BY-NC-SA 3.0), and so are the ports in this folder, which are derivative works:

- **Attribution:** the header of every shader file names the original's URL, its author and its
  license. Keep that header when you copy or modify a file.
- **NonCommercial:** you may not use these shaders, or anything derived from them, for commercial
  purposes.
- **ShareAlike:** if you change and redistribute them, your version must use the same license.

Because of these terms, this project is excluded from every SharpCompute package (`IsPackable` is
`false`) and exists only to demonstrate the library. The rest of the repository, including the
gallery app in `samples/SharpCompute.Samples.Gallery/`, is MIT.

## ComputeSharp: MIT

The ports are adapted from the C# versions in ComputeSharp's
[`samples/ComputeSharp.SwapChain.Shaders`](https://github.com/Sergio0694/ComputeSharp/tree/main/samples/ComputeSharp.SwapChain.Shaders),
which is MIT licensed:

> Copyright (c) 2024 Sergio Pedri
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
> associated documentation files (the "Software"), to deal in the Software without restriction,
> including without limitation the rights to use, copy, modify, merge, publish, distribute,
> sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or
> substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
> NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
> NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
> DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
> OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

The MIT terms apply to ComputeSharp's C# code. They do not relax the CC BY-NC-SA 3.0 terms of the
underlying Shadertoy shaders.

## Shaders

| File | Original | Author | License |
|------|----------|--------|---------|
| `HelloWorld.cs` | https://www.shadertoy.com/new | Beautypi (Shadertoy default template) | CC BY-NC-SA 3.0 |
| `ColorfulInfinity.cs` | https://www.shadertoy.com/view/WtjyzR | Benoit Marini | CC BY-NC-SA 3.0 |
| `FourColorGradient.cs` | https://www.shadertoy.com/view/7lc3R4 | Inigo Quilez | CC BY-NC-SA 3.0 |
| `FractalTiling.cs` | https://www.shadertoy.com/view/Ml2GWy | Inigo Quilez | CC BY-NC-SA 3.0 |
| `Octagrams.cs` | https://www.shadertoy.com/view/tlVGDt | whisky_shusuky | CC BY-NC-SA 3.0 |
| `TwoTiledTruchet.cs` | https://www.shadertoy.com/view/tsSfWK | Shane | CC BY-NC-SA 3.0 |
| `MengerJourney.cs` | https://www.shadertoy.com/view/Mdf3z7 | Syntopia | CC BY-NC-SA 3.0 |
| `ProteanClouds.cs` | https://www.shadertoy.com/view/3l23Rh | nimitz (@stormoid) | CC BY-NC-SA 3.0 |
| `TerracedHills.cs` | https://www.shadertoy.com/view/MtdSRn | Shane | CC BY-NC-SA 3.0 |
| `PyramidPattern.cs` | https://www.shadertoy.com/view/wlscWX | Shane | CC BY-NC-SA 3.0 |
| `ExtrudedTruchetPattern.cs` | https://www.shadertoy.com/view/ttVBzd | Shane | CC BY-NC-SA 3.0 |
| `TriangleGridContouring.cs` | https://www.shadertoy.com/view/WtfGDX | Shane | CC BY-NC-SA 3.0 |

## Exception: original SharpCompute samples

The Mandelbrot deep zoom (`Mandelbrot.cs`, `MandelbrotPerturbation.cs`, `MandelbrotReferenceOrbit.cs`,
`MandelbrotSeries.cs`) and `FixedPoint.cs` are original SharpCompute code, not Shadertoy ports, and
are licensed MIT like the rest of the repository.
