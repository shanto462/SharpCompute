// Original SharpCompute sample (MIT).
using System;
using System.Runtime.InteropServices;

namespace SharpCompute.Samples.NativeInterop.Host;

/// <summary>
/// A tiny renderer written directly against Metal, standing in for an app or engine that already has
/// one. It owns everything: the <c>MTLDevice</c> and command queue, a render pipeline compiled from
/// <see cref="HostShaders"/>, its picture, its particle buffer and the textures it renders into. Each
/// frame it records one command buffer: a scene pass (the picture and the particles) into
/// <see cref="SceneTexture"/>, and an overlay pass onto whichever texture the frame ends in.
/// Nothing in this class refers to SharpCompute.
/// </summary>
/// <remarks>Not thread-safe; use it from one thread.</remarks>
internal sealed unsafe class HostRenderer : IDisposable
{
    private const int ImageWidth = 640;
    private const int ImageHeight = 360;

    private readonly nint _backgroundPipeline;
    private readonly nint _particlePipeline;
    private readonly nint _overlayPipeline;
    private readonly nint _image;
    private nint _overlay;
    private byte[] _overlayPixels = [];
    private bool _disposed;

    /// <param name="particleCount">How many particles the particle buffer holds.</param>
    /// <param name="width">Width of the frame textures, in pixels.</param>
    /// <param name="height">Height of the frame textures, in pixels.</param>
    public HostRenderer(int particleCount, int width, int height)
    {
        using Pool pool = Pool.Push();

        Device = Objc.MTLCreateSystemDefaultDevice();

        if (Device == 0)
        {
            throw new PlatformNotSupportedException("No Metal device.");
        }

        Queue = Objc.Id(Device, Sels.NewCommandQueue);

        nint library = CompileLibrary(Device);

        try
        {
            _backgroundPipeline = NewPipeline(library, "frame_quad", "background", Blend.None);
            _particlePipeline = NewPipeline(library, "particle_quad", "particle_glow", Blend.Additive);
            _overlayPipeline = NewPipeline(library, "frame_quad", "overlay", Blend.Alpha);
        }
        finally
        {
            Objc.Release(library);
        }

        _image = NewTexture(ImageWidth, ImageHeight, Mtl.PixelFormatRGBA8Unorm, Mtl.UsageShaderRead, Mtl.StorageModeShared);
        Upload(_image, HostImage.Create(ImageWidth, ImageHeight), ImageWidth * 4);

        ParticleCount = particleCount;
        ParticleBuffer = Objc.Id(Device, Sels.NewBufferWithLengthOptions, (nuint)(particleCount * sizeof(HostParticle)), Mtl.ResourceStorageModeShared);
        ResetParticles(width / (float)height);

        Resize(width, height);
    }

    /// <summary>The host's <c>id&lt;MTLDevice&gt;</c>.</summary>
    public nint Device { get; }

    /// <summary>The host's <c>id&lt;MTLCommandQueue&gt;</c>; every frame is committed to it.</summary>
    public nint Queue { get; }

    /// <summary>The particle buffer the scene pass draws from: <see cref="ParticleCount"/> particles, shared storage.</summary>
    public nint ParticleBuffer { get; }

    public int ParticleCount { get; }

    /// <summary>What the scene pass renders into. RGBA8Unorm, private storage; render target, shader read and write.</summary>
    public nint SceneTexture { get; private set; }

    /// <summary>A second frame texture like <see cref="SceneTexture"/>, for a pass that reads the scene and writes a new picture.</summary>
    public nint OutputTexture { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Puts the particles back where they start: a deterministic spread over the frame, at rest.</summary>
    public void ResetParticles(float aspect)
    {
        HostParticle* particles = (HostParticle*)Objc.Id(ParticleBuffer, Sels.Contents);

        for (int i = 0; i < ParticleCount; i++)
        {
            // A golden-angle spiral: even coverage with no random numbers.
            float t = (i + 0.5f) / ParticleCount;
            float radius = MathF.Sqrt(t);
            float angle = i * 2.3999632f;
            particles[i] = new HostParticle(MathF.Cos(angle) * radius * aspect * 0.95f, MathF.Sin(angle) * radius * 0.95f);
        }
    }

    /// <summary>(Re)creates the frame textures at a new size. Wrappers of the old ones must be dropped first.</summary>
    public void Resize(int width, int height)
    {
        Objc.Release(SceneTexture);
        Objc.Release(OutputTexture);
        Objc.Release(_overlay);

        const nuint frameUsage = Mtl.UsageRenderTarget | Mtl.UsageShaderRead | Mtl.UsageShaderWrite;
        SceneTexture = NewTexture(width, height, Mtl.PixelFormatRGBA8Unorm, frameUsage, Mtl.StorageModePrivate);
        OutputTexture = NewTexture(width, height, Mtl.PixelFormatRGBA8Unorm, frameUsage, Mtl.StorageModePrivate);
        _overlay = NewTexture(width, height, Mtl.PixelFormatR8Unorm, Mtl.UsageShaderRead, Mtl.StorageModeShared);
        _overlayPixels = new byte[width * height];
        Width = width;
        Height = height;
    }

    /// <summary>Draws the help overlay: each line top-left, on a dark plate, at a size that suits the frame.</summary>
    public void SetOverlay(ReadOnlySpan<string> lines)
    {
        Array.Clear(_overlayPixels);
        int scale = Math.Max(2, Height / 270);
        int lineHeight = (PixelFont.GlyphHeight + 4) * scale;
        int x = 4 * scale, y = 4 * scale;

        foreach (string line in lines)
        {
            Fill(x - (2 * scale), y - (2 * scale), PixelFont.Measure(line, scale) + (4 * scale), lineHeight, 128);

            for (int i = 0; i < line.Length; i++)
            {
                int left = x + (i * (PixelFont.GlyphWidth + 1) * scale);

                for (int row = 0; row < PixelFont.GlyphHeight; row++)
                {
                    for (int column = 0; column < PixelFont.GlyphWidth; column++)
                    {
                        if (PixelFont.IsSet(line[i], column, row))
                        {
                            Fill(left + (column * scale), y + (row * scale), scale, scale, 255);
                        }
                    }
                }
            }

            y += lineHeight + scale;
        }

        Upload(_overlay, _overlayPixels, Width);
    }

    /// <summary>A new command buffer of the host's queue, retained: one per frame.</summary>
    public nint BeginFrame()
    {
        using Pool pool = Pool.Push();
        return Objc.Retain(Objc.Id(Queue, Sels.CommandBuffer));
    }

    /// <summary>The scene pass: clears <see cref="SceneTexture"/>, draws the picture, then every particle on top, added.</summary>
    public void EncodeScene(nint commandBuffer, float time, float particleSize)
    {
        using Pool pool = Pool.Push();

        Uniforms uniforms = new(Height / (float)Width, 1f, particleSize, time);
        nint encoder = BeginPass(commandBuffer, SceneTexture, clear: true);

        Objc.Void(encoder, Sels.SetRenderPipelineState, _backgroundPipeline);
        Objc.Void(encoder, Sels.SetFragmentTextureAtIndex, _image, 0);
        Objc.Void(encoder, Sels.SetFragmentBytesLengthAtIndex, &uniforms, (nuint)sizeof(Uniforms), 0);
        Objc.Void(encoder, Sels.DrawPrimitives, Mtl.PrimitiveTypeTriangleStrip, 0, 4, 1);

        Objc.Void(encoder, Sels.SetRenderPipelineState, _particlePipeline);
        Objc.Void(encoder, Sels.SetVertexBufferOffsetAtIndex, ParticleBuffer, 0, 0);
        Objc.Void(encoder, Sels.SetVertexBytesLengthAtIndex, &uniforms, (nuint)sizeof(Uniforms), 1);
        Objc.Void(encoder, Sels.DrawPrimitives, Mtl.PrimitiveTypeTriangleStrip, 0, 4, (nuint)ParticleCount);

        Objc.Void(encoder, Sels.EndEncoding);
    }

    /// <summary>The overlay pass: blends the help text over <paramref name="target"/>, keeping what is there.</summary>
    public void EncodeOverlay(nint commandBuffer, nint target)
    {
        using Pool pool = Pool.Push();

        nint encoder = BeginPass(commandBuffer, target, clear: false);
        Objc.Void(encoder, Sels.SetRenderPipelineState, _overlayPipeline);
        Objc.Void(encoder, Sels.SetFragmentTextureAtIndex, _overlay, 0);
        Objc.Void(encoder, Sels.DrawPrimitives, Mtl.PrimitiveTypeTriangleStrip, 0, 4, 1);
        Objc.Void(encoder, Sels.EndEncoding);
    }

    public static void Commit(nint commandBuffer) => Objc.Void(commandBuffer, Sels.Commit);

    /// <summary>Waits for a committed frame and returns how long the GPU spent on it, in milliseconds.</summary>
    public static double WaitForFrame(nint commandBuffer)
    {
        using Pool pool = Pool.Push();

        Objc.Void(commandBuffer, Sels.WaitUntilCompleted);

        if (Objc.UInt(commandBuffer, Sels.Status) == Mtl.CommandBufferStatusError)
        {
            throw new InvalidOperationException($"The host's frame failed on the GPU: {Objc.Describe(Objc.Id(commandBuffer, Sels.Error))}");
        }

        return (Objc.Double(commandBuffer, Sels.GpuEndTime) - Objc.Double(commandBuffer, Sels.GpuStartTime)) * 1000.0;
    }

    public static void EndFrame(nint commandBuffer) => Objc.Release(commandBuffer);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Objc.Release(SceneTexture);
        Objc.Release(OutputTexture);
        Objc.Release(_overlay);
        Objc.Release(_image);
        Objc.Release(ParticleBuffer);
        Objc.Release(_backgroundPipeline);
        Objc.Release(_particlePipeline);
        Objc.Release(_overlayPipeline);
        Objc.Release(Queue);
        Objc.Release(Device);
    }

    private static nint CompileLibrary(nint device)
    {
        nint source = Objc.String(HostShaders.Source);
        nint options = Objc.New("MTLCompileOptions");

        try
        {
            Objc.Void(options, Objc.Sel("setLanguageVersion:"), Mtl.LanguageVersion3_2);

            nint error = 0;
            nint library = Objc.Id(device, Sels.NewLibraryWithSourceOptionsError, source, options, &error);
            return library != 0 ? library : throw new InvalidOperationException($"The host's shaders did not compile: {Objc.Describe(error)}");
        }
        finally
        {
            Objc.Release(options);
            Objc.Release(source);
        }
    }

    private nint NewPipeline(nint library, string vertex, string fragment, Blend blend)
    {
        nint vertexName = Objc.String(vertex), fragmentName = Objc.String(fragment);
        nint vertexFunction = Objc.Id(library, Sels.NewFunctionWithName, vertexName);
        nint fragmentFunction = Objc.Id(library, Sels.NewFunctionWithName, fragmentName);
        nint descriptor = Objc.New("MTLRenderPipelineDescriptor");

        try
        {
            Objc.Void(descriptor, Sels.SetVertexFunction, vertexFunction);
            Objc.Void(descriptor, Sels.SetFragmentFunction, fragmentFunction);

            nint attachment = Objc.Id(Objc.Id(descriptor, Sels.ColorAttachments), Sels.ObjectAtIndexedSubscript, (nuint)0);
            Objc.Void(attachment, Sels.SetPixelFormat, Mtl.PixelFormatRGBA8Unorm);

            if (blend != Blend.None)
            {
                nuint source = blend == Blend.Additive ? Mtl.BlendFactorOne : Mtl.BlendFactorSourceAlpha;
                nuint destination = blend == Blend.Additive ? Mtl.BlendFactorOne : Mtl.BlendFactorOneMinusSourceAlpha;
                Objc.Void(attachment, Sels.SetBlendingEnabled, (byte)1);
                Objc.Void(attachment, Sels.SetSourceRGBBlendFactor, source);
                Objc.Void(attachment, Sels.SetDestinationRGBBlendFactor, destination);
                Objc.Void(attachment, Sels.SetSourceAlphaBlendFactor, Mtl.BlendFactorOne);
                Objc.Void(attachment, Sels.SetDestinationAlphaBlendFactor, destination);
            }

            nint error = 0;
            nint pipeline = Objc.Id(Device, Sels.NewRenderPipelineStateWithDescriptorError, descriptor, &error);
            return pipeline != 0 ? pipeline : throw new InvalidOperationException($"The host's '{fragment}' pipeline failed: {Objc.Describe(error)}");
        }
        finally
        {
            Objc.Release(descriptor);
            Objc.Release(fragmentFunction);
            Objc.Release(vertexFunction);
            Objc.Release(fragmentName);
            Objc.Release(vertexName);
        }
    }

    private nint NewTexture(int width, int height, nuint pixelFormat, nuint usage, nuint storageMode)
    {
        nint descriptor = Objc.New("MTLTextureDescriptor");

        try
        {
            Objc.Void(descriptor, Sels.SetTextureType, Mtl.TextureType2D);
            Objc.Void(descriptor, Sels.SetPixelFormat, pixelFormat);
            Objc.Void(descriptor, Sels.SetWidth, (nuint)width);
            Objc.Void(descriptor, Sels.SetHeight, (nuint)height);
            Objc.Void(descriptor, Sels.SetUsage, usage);
            Objc.Void(descriptor, Sels.SetStorageMode, storageMode);

            nint texture = Objc.Id(Device, Sels.NewTextureWithDescriptor, descriptor);
            return texture != 0 ? texture : throw new InvalidOperationException($"Metal failed to create a {width}×{height} texture.");
        }
        finally
        {
            Objc.Release(descriptor);
        }
    }

    private static void Upload(nint texture, byte[] pixels, int bytesPerRow)
    {
        fixed (byte* bytes = pixels)
        {
            nuint height = (nuint)(pixels.Length / bytesPerRow);
            nuint width = Objc.UInt(texture, Objc.Sel("width"));
            Objc.Void(texture, Sels.ReplaceRegion, new Region(0, 0, width, height), 0, bytes, (nuint)bytesPerRow);
        }
    }

    /// <summary>Starts a render pass on <paramref name="target"/>: cleared to black, or loaded as it is.</summary>
    private static nint BeginPass(nint commandBuffer, nint target, bool clear)
    {
        nint pass = Objc.Id(Objc.GetClass("MTLRenderPassDescriptor"), Sels.RenderPassDescriptor);
        nint attachment = Objc.Id(Objc.Id(pass, Sels.ColorAttachments), Sels.ObjectAtIndexedSubscript, (nuint)0);
        Objc.Void(attachment, Sels.SetTexture, target);
        Objc.Void(attachment, Sels.SetLoadAction, clear ? Mtl.LoadActionClear : Mtl.LoadActionLoad);
        Objc.Void(attachment, Sels.SetStoreAction, Mtl.StoreActionStore);
        Objc.Void(attachment, Sels.SetClearColor, new ClearColor(0, 0, 0, 1));

        nint encoder = Objc.Id(commandBuffer, Sels.RenderCommandEncoderWithDescriptor, pass);
        return encoder != 0 ? encoder : throw new InvalidOperationException("Metal failed to create a render command encoder.");
    }

    private void Fill(int x, int y, int width, int height, byte value)
    {
        int x0 = Math.Max(x, 0), y0 = Math.Max(y, 0);
        int x1 = Math.Min(x + width, Width), y1 = Math.Min(y + height, Height);

        for (int row = y0; row < y1 && x1 > x0; row++)
        {
            _overlayPixels.AsSpan((row * Width) + x0, x1 - x0).Fill(value);
        }
    }

    private enum Blend
    {
        None,
        Additive,
        Alpha,
    }

    /// <summary>The MSL <c>Particle</c> as the host writes it: position, velocity, color (32 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct HostParticle(float x, float y)
    {
        public readonly float X = x;
        public readonly float Y = y;
        public readonly float VelocityX = 0f;
        public readonly float VelocityY = 0f;
        public readonly float Red = 1f;
        public readonly float Green = 0.75f;
        public readonly float Blue = 0.4f;
        public readonly float Alpha = 0.35f;
    }

    /// <summary>The MSL <c>Uniforms</c>: four floats.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Uniforms(float viewScaleX, float viewScaleY, float particleSize, float time)
    {
        public readonly float ViewScaleX = viewScaleX;
        public readonly float ViewScaleY = viewScaleY;
        public readonly float ParticleSize = particleSize;
        public readonly float Time = time;
    }
}
