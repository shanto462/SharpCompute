using System;
using System.Runtime.InteropServices;
using SharpCompute.Interop;

namespace SharpCompute.Presentation;

/// <summary>
/// The internal kernel that copies a <see cref="ReadWriteTexture2D{T}"/> into a drawable.
/// </summary>
/// <remarks>
/// A <c>CAMetalLayer</c> cannot use RGBA8Unorm, and our textures are linear views over buffers, so
/// they can never be drawables themselves. The kernel samples the RGBA8 frame and writes the BGRA8
/// drawable (the component order is handled by the texture write), placing the frame in the
/// destination rectangle from <see cref="PresentLayout.Destination"/> and clearing the rest to
/// opaque black. Integer scales use nearest filtering so pixels are copied exactly; other scales
/// filter bilinearly.
/// </remarks>
internal sealed unsafe class Presenter : IDisposable
{
    internal const string EntryPoint = "sharpcompute_present";

    internal const string MslSource = """
        #include <metal_stdlib>
        using namespace metal;

        struct PresentParams
        {
            int2 origin;   // top-left of the frame's destination rectangle, in drawable pixels
            int2 size;     // size of that rectangle
            uint nearest;  // 1: nearest filtering (pixel-exact scale), 0: bilinear
            uint padding;
        };

        static_assert(sizeof(PresentParams) == 24, "PresentParams must match the C# mirror");

        kernel void sharpcompute_present(
            texture2d<float, access::sample> source [[texture(0)]],
            texture2d<float, access::write> target [[texture(1)]],
            constant PresentParams& p [[buffer(0)]],
            uint2 gid [[thread_position_in_grid]])
        {
            if (gid.x >= target.get_width() || gid.y >= target.get_height())
            {
                return;
            }

            int2 local = int2(gid) - p.origin;
            float4 color = float4(0.0, 0.0, 0.0, 1.0);

            if (all(local >= int2(0)) && all(local < p.size))
            {
                constexpr sampler nearestSampler(coord::normalized, filter::nearest, address::clamp_to_edge);
                constexpr sampler linearSampler(coord::normalized, filter::linear, address::clamp_to_edge);

                float2 uv = (float2(local) + 0.5) / float2(p.size);
                float3 rgb = p.nearest != 0 ? source.sample(nearestSampler, uv).rgb : source.sample(linearSampler, uv).rgb;
                color = float4(rgb, 1.0);
            }

            target.write(color, gid);
        }
        """;

    private readonly ComputePipeline _pipeline;

    internal Presenter(GraphicsDevice device) => _pipeline = device.CompilePipeline(MslSource, EntryPoint, typeof(Presenter));

    /// <summary>
    /// Encodes one present pass into <paramref name="commandBuffer"/>. The caller keeps
    /// <paramref name="source"/> and <paramref name="target"/> alive until the command buffer completes
    /// and runs this inside an autorelease pool (the encoder is autoreleased).
    /// </summary>
    internal void Encode(nint commandBuffer, nint source, int sourceWidth, int sourceHeight, nint target, int targetWidth, int targetHeight, PresentScaling scaling)
    {
        PixelRect destination = PresentLayout.Destination(sourceWidth, sourceHeight, targetWidth, targetHeight, scaling);
        PresentParams parameters = new()
        {
            OriginX = destination.X,
            OriginY = destination.Y,
            Width = destination.Width,
            Height = destination.Height,
            Nearest = PresentLayout.IsPixelExact(destination, sourceWidth, sourceHeight) ? 1u : 0u,
        };

        using ObjCHandle.Lease state = _pipeline.State.Acquire(this);

        nint encoder = ObjC.SendIntPtr(commandBuffer, Selectors.ComputeCommandEncoder);

        if (encoder == 0)
        {
            throw new InvalidOperationException("Metal failed to create a compute command encoder.");
        }

        try
        {
            ObjC.SendVoid(encoder, Selectors.SetComputePipelineState, state.Pointer);
            ObjC.SendVoid(encoder, Selectors.SetTextureAtIndex, source, (nuint)0);
            ObjC.SendVoid(encoder, Selectors.SetTextureAtIndex, target, (nuint)1);
            ObjC.SendVoid(encoder, Selectors.SetBytesLengthAtIndex, &parameters, (nuint)sizeof(PresentParams), (nuint)0);

            MTLSize threadgroup = GraphicsDevice.ResolveThreadGroupSize(0, 0, 0, rank: 2, _pipeline.MaxTotalThreadsPerThreadgroup, typeof(Presenter));
            ObjC.SendVoid(encoder, Selectors.DispatchThreadsThreadsPerThreadgroup, new MTLSize((nuint)targetWidth, (nuint)targetHeight, 1), threadgroup);
        }
        finally
        {
            // Metal aborts the process if an encoder is deallocated without endEncoding.
            ObjC.SendVoid(encoder, Selectors.EndEncoding);
        }
    }

    public void Dispose() => _pipeline.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct PresentParams
    {
        public int OriginX;
        public int OriginY;
        public int Width;
        public int Height;
        public uint Nearest;
        public uint Padding;
    }
}
