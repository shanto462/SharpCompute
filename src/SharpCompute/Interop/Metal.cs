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

/// <summary>Metal <c>MTLSize</c>: three <c>NSUInteger</c>s, passed by value.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct MTLSize(nuint width, nuint height, nuint depth)
{
    public readonly nuint Width = width;
    public readonly nuint Height = height;
    public readonly nuint Depth = depth;
}
