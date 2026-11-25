using System.Runtime.InteropServices;

namespace SharpCompute.Interop;

/// <summary>Metal framework entry points and the constants this runtime uses.</summary>
internal static partial class Metal
{
    private const string MetalFramework = "/System/Library/Frameworks/Metal.framework/Metal";

    /// <summary>Returns a +1 reference to the default device, or nil when the machine has no Metal device.</summary>
    [LibraryImport(MetalFramework, EntryPoint = "MTLCreateSystemDefaultDevice")]
    internal static partial nint CreateSystemDefaultDevice();

    /// <summary><c>MTLResourceStorageModeShared</c> (<c>MTLStorageModeShared &lt;&lt; 4</c>).</summary>
    internal const nuint ResourceStorageModeShared = 0;

    /// <summary><c>MTLStorageModeShared</c> (texture descriptors take the storage mode, not resource options).</summary>
    internal const nuint StorageModeShared = 0;

    /// <summary><c>MTLStorageModeManaged</c>. On Apple silicon it behaves as shared storage (the CPU and the GPU see one copy).</summary>
    internal const nuint StorageModeManaged = 1;

    /// <summary><c>MTLStorageModePrivate</c>: GPU-only memory the CPU must not read or write.</summary>
    internal const nuint StorageModePrivate = 2;

    /// <summary><c>MTLStorageModeMemoryless</c>: tile memory of a render pass, with no backing store at all.</summary>
    internal const nuint StorageModeMemoryless = 3;

    /// <summary><c>MTLHazardTrackingModeUntracked</c>: Metal does not order the commands that use the resource.</summary>
    internal const nuint HazardTrackingModeUntracked = 1;

    /// <summary><c>MTLTextureUsageShaderWrite</c>.</summary>
    internal const nuint TextureUsageShaderWrite = 0x2;

    /// <summary><c>MTLCommandBufferStatusEnqueued</c>: the last status in which commands can still be encoded.</summary>
    internal const nuint CommandBufferStatusEnqueued = 1;

    /// <summary><c>MTLTextureType2D</c>.</summary>
    internal const nuint TextureType2D = 2;

    /// <summary><c>MTLPixelFormatRGBA8Unorm</c>.</summary>
    internal const nuint PixelFormatRGBA8Unorm = 70;

    /// <summary><c>MTLTextureUsageShaderRead</c>: the texture can be read and sampled.</summary>
    internal const nuint TextureUsageShaderRead = 0x1;

    /// <summary><c>MTLTextureUsageShaderRead | MTLTextureUsageShaderWrite</c>.</summary>
    internal const nuint TextureUsageShaderReadWrite = 0x1 | 0x2;

    /// <summary>Largest width or height of a 2D texture on Apple-family GPUs (Apple3 and later).</summary>
    internal const int MaxTexture2DDimension = 16384;

    /// <summary><c>MTLLanguageVersion3_2</c> (<c>(3 &lt;&lt; 16) + 2</c>, macOS 15+).</summary>
    internal const nuint LanguageVersion3_2 = (3u << 16) + 2;

    /// <summary><c>MTLMathModeSafe</c>: IEEE NaN/Inf semantics are honoured.</summary>
    internal const nint MathModeSafe = 0;

    /// <summary><c>MTLMathModeFast</c>: the compiler may assume no NaN or infinity and reorder arithmetic (<c>-ffast-math</c>).</summary>
    internal const nint MathModeFast = 2;

    /// <summary><c>MTLMathFloatingPointFunctionsFast</c>: sin/exp/pow etc. trade accuracy for speed (Metal's own default).</summary>
    internal const nint MathFloatingPointFunctionsFast = 0;

    /// <summary><c>MTLMathFloatingPointFunctionsPrecise</c>: precise sin/exp/pow etc. (default is Fast).</summary>
    internal const nint MathFloatingPointFunctionsPrecise = 1;

    /// <summary><c>MTLDataTypeBool</c>, the type of a bool function constant.</summary>
    internal const nuint DataTypeBool = 53;

    /// <summary><c>MTLCommandBufferStatusError</c>.</summary>
    internal const nuint CommandBufferStatusError = 5;

    /// <summary>Largest payload <c>setBytes:length:atIndex:</c> accepts.</summary>
    internal const int MaxSetBytesLength = 4096;

    /// <summary>Buffer argument table size: valid indices are 0..30.</summary>
    internal const int MaxBufferBindings = 31;

    /// <summary>Texture argument table size of a compute encoder: valid indices are 0..30.</summary>
    internal const int MaxTextureBindings = 31;
}

/// <summary>Metal <c>MTLOrigin</c>: three <c>NSUInteger</c>s, passed by value.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct MTLOrigin(nuint x, nuint y, nuint z)
{
    public readonly nuint X = x;
    public readonly nuint Y = y;
    public readonly nuint Z = z;
}

/// <summary>Metal <c>MTLRegion</c>: an origin and a size, passed by value.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct MTLRegion(MTLOrigin origin, MTLSize size)
{
    public readonly MTLOrigin Origin = origin;
    public readonly MTLSize Size = size;
}

/// <summary>Metal <c>MTLSize</c>: three <c>NSUInteger</c>s, passed by value.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct MTLSize(nuint width, nuint height, nuint depth)
{
    public readonly nuint Width = width;
    public readonly nuint Height = height;
    public readonly nuint Depth = depth;
}

/// <summary>
/// The Metal protocols an <c>id</c> handed in by a caller is checked against. 0 when the runtime has
/// not registered the protocol, in which case the check is skipped (every Metal object of the system
/// framework adopts them, so in practice they are always there once a device exists).
/// </summary>
internal static class MetalProtocols
{
    internal static readonly nint Device = ObjC.GetProtocol("MTLDevice");
    internal static readonly nint CommandQueue = ObjC.GetProtocol("MTLCommandQueue");
    internal static readonly nint CommandBuffer = ObjC.GetProtocol("MTLCommandBuffer");
    internal static readonly nint Buffer = ObjC.GetProtocol("MTLBuffer");
    internal static readonly nint Texture = ObjC.GetProtocol("MTLTexture");
}
