// Original:  https://www.shadertoy.com/new (Shadertoy's default "new shader" template)
// Author:    Beautypi (Inigo Quilez and Pol Jeremias), Shadertoy
// License:   Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported (CC BY-NC-SA 3.0),
//            Shadertoy's default license. NOT covered by SharpCompute's MIT license; see NOTICE.md.
// C# port:   adapted from ComputeSharp's samples/ComputeSharp.SwapChain.Shaders/HelloWorld.cs
//            (MIT, Copyright (c) Sergio Pedri).

namespace SharpCompute.Samples.Shaders;

/// <summary>
/// A simple shader to get started with, based on Shadertoy's new shader template: a color gradient
/// over the normalized pixel position that cycles with <paramref name="time"/>.
/// </summary>
/// <param name="time">The current time in seconds.</param>
[GeneratedComputeShaderDescriptor]
public readonly partial struct HelloWorld(float time) : IComputeShader<Float4>
{
    internal const string Author = "Beautypi (Shadertoy default template)";

    internal const string SourceUrl = "https://www.shadertoy.com/new";

    /// <inheritdoc/>
    public Float4 Execute()
    {
        // Normalized screen space UV coordinates from 0.0 to 1.0
        Float2 uv = ThreadIds.Normalized.XY;

        // Time varying pixel color
        Float3 col = 0.5f + (0.5f * Gpu.Cos(time + new Float3(uv, uv.X) + new Float3(0, 2, 4)));

        // Output to screen
        return new(col, 1f);
    }
}
