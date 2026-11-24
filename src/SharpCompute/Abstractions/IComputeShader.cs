namespace SharpCompute;

/// <summary>
/// A GPU compute shader. Implement it on a <c>readonly partial struct</c> marked with
/// <see cref="GeneratedComputeShaderDescriptorAttribute"/>; the source generator translates
/// <see cref="Execute"/> to Metal Shading Language at build time.
/// </summary>
public interface IComputeShader
{
    /// <summary>The body of the kernel, executed once per dispatched thread.</summary>
    void Execute();
}

/// <summary>
/// A pixel-style compute shader: <see cref="Execute"/> runs once per pixel of an output texture and
/// returns that pixel's color (normalized, 0..1 per channel). Dispatched with
/// <c>GraphicsDevice.ForEach(texture, shader)</c>; inside it, <see cref="DispatchSize"/> is the texture size.
/// </summary>
/// <typeparam name="TPixel">The returned color type. v1 supports <see cref="Float4"/> (RGBA).</typeparam>
public interface IComputeShader<TPixel>
    where TPixel : unmanaged
{
    /// <summary>The color of the current pixel.</summary>
    TPixel Execute();
}
