using System;
using System.Runtime.CompilerServices;
using SharpCompute.Interop;

namespace SharpCompute;

/// <summary>How a texture format stores a channel.</summary>
internal enum PixelEncoding
{
    /// <summary>8-bit unsigned normalized: <c>c / 255</c>.</summary>
    Unorm8,

    /// <summary>8-bit sRGB: the color channels are decoded with the sRGB transfer function, alpha is <see cref="Unorm8"/>.</summary>
    Srgb8,

    /// <summary>IEEE half.</summary>
    Float16,

    /// <summary>IEEE single.</summary>
    Float32,
}

/// <summary>The Metal pixel format behind a texture's element type, and how the CPU reads and writes it.</summary>
/// <param name="PixelFormat">The <c>MTLPixelFormat</c>.</param>
/// <param name="Name">The Metal name, for messages.</param>
/// <param name="Channels">Stored channels (1, 2 or 4). Reads fill the missing ones with <c>(0, 0, 1)</c> as Metal does.</param>
/// <param name="Encoding">How each channel is stored.</param>
/// <param name="SupportsReadWrite">
/// True when an Apple GPU can read and write the format in one shader (<c>access::read_write</c>, read-write
/// texture tier 2). Only such formats are allowed for <see cref="ReadWriteTexture2D{T}"/>.
/// </param>
internal readonly record struct TextureFormat(nuint PixelFormat, string Name, int Channels, PixelEncoding Encoding, bool SupportsReadWrite);

/// <summary>
/// The texture element types SharpCompute supports and the CPU side of reading and writing them. The
/// conversions match what Apple GPUs do when a shader reads or writes a <c>texture2d&lt;float&gt;</c>, so
/// a shader run with <c>ForCpu</c> sees the same values as on the GPU (see each method for the details).
/// </summary>
internal static unsafe class TextureFormats
{
    // MTLPixelFormat values.
    private const nuint R8Unorm = 10;
    private const nuint R16Float = 25;
    private const nuint RG8Unorm = 30;
    private const nuint R32Float = 55;
    private const nuint RGBA8UnormSrgb = 71;
    private const nuint RGBA16Float = 115;
    private const nuint RGBA32Float = 125;

    /// <summary>The sRGB-to-linear table for the 256 byte values, computed in double precision.</summary>
    private static readonly float[] SrgbToLinear = CreateSrgbTable();

    /// <summary>The supported element types, listed in messages.</summary>
    internal const string SupportedTypes = "Rgba32, Rgba32Srgb, R8, Rg16, Half, Rgba16Float, float or Float4";

    /// <summary>The format of <typeparamref name="T"/>, or null when it is not a texture element type.</summary>
    internal static TextureFormat? Get<T>()
        where T : unmanaged
    {
        if (typeof(T) == typeof(Rgba32))
        {
            return new(Metal.PixelFormatRGBA8Unorm, "RGBA8Unorm", 4, PixelEncoding.Unorm8, SupportsReadWrite: true);
        }

        if (typeof(T) == typeof(Rgba32Srgb))
        {
            return new(RGBA8UnormSrgb, "RGBA8Unorm_sRGB", 4, PixelEncoding.Srgb8, SupportsReadWrite: false);
        }

        if (typeof(T) == typeof(R8))
        {
            return new(R8Unorm, "R8Unorm", 1, PixelEncoding.Unorm8, SupportsReadWrite: true);
        }

        if (typeof(T) == typeof(Rg16))
        {
            return new(RG8Unorm, "RG8Unorm", 2, PixelEncoding.Unorm8, SupportsReadWrite: false);
        }

        if (typeof(T) == typeof(Half))
        {
            return new(R16Float, "R16Float", 1, PixelEncoding.Float16, SupportsReadWrite: true);
        }

        if (typeof(T) == typeof(Rgba16Float))
        {
            return new(RGBA16Float, "RGBA16Float", 4, PixelEncoding.Float16, SupportsReadWrite: true);
        }

        if (typeof(T) == typeof(float))
        {
            return new(R32Float, "R32Float", 1, PixelEncoding.Float32, SupportsReadWrite: true);
        }

        if (typeof(T) == typeof(Float4))
        {
            return new(RGBA32Float, "RGBA32Float", 4, PixelEncoding.Float32, SupportsReadWrite: true);
        }

        return null;
    }

    /// <summary>
    /// What a shader's <c>read</c> returns for the pixel at <paramref name="pixel"/>. Unorm channels are
    /// <c>c / 255</c> (exact in float), sRGB channels go through the transfer function, and missing
    /// channels read as <c>(0, 0, 1)</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Float4 Load<T>(T* pixel)
        where T : unmanaged
    {
        byte* p = (byte*)pixel;

        if (typeof(T) == typeof(Rgba32))
        {
            return new Float4(p[0] / 255f, p[1] / 255f, p[2] / 255f, p[3] / 255f);
        }

        if (typeof(T) == typeof(Rgba32Srgb))
        {
            return new Float4(SrgbToLinear[p[0]], SrgbToLinear[p[1]], SrgbToLinear[p[2]], p[3] / 255f);
        }

        if (typeof(T) == typeof(R8))
        {
            return new Float4(p[0] / 255f, 0f, 0f, 1f);
        }

        if (typeof(T) == typeof(Rg16))
        {
            return new Float4(p[0] / 255f, p[1] / 255f, 0f, 1f);
        }

        if (typeof(T) == typeof(Half))
        {
            return new Float4((float)*(Half*)p, 0f, 0f, 1f);
        }

        if (typeof(T) == typeof(Rgba16Float))
        {
            Rgba16Float value = *(Rgba16Float*)p;
            return new Float4((float)value.R, (float)value.G, (float)value.B, (float)value.A);
        }

        if (typeof(T) == typeof(float))
        {
            return new Float4(*(float*)p, 0f, 0f, 1f);
        }

        if (typeof(T) == typeof(Float4))
        {
            return *(Float4*)p;
        }

        throw Unsupported<T>();
    }

    /// <summary>
    /// What a shader's <c>write</c> stores for <paramref name="value"/>. Unorm channels use the measured
    /// GPU conversion (<see cref="CpuDispatcher.ToUnorm8"/>), half channels round to nearest even, and
    /// channels the format does not store are dropped.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Store<T>(T* pixel, Float4 value)
        where T : unmanaged
    {
        byte* p = (byte*)pixel;

        if (typeof(T) == typeof(Rgba32))
        {
            p[0] = CpuDispatcher.ToUnorm8(value.X);
            p[1] = CpuDispatcher.ToUnorm8(value.Y);
            p[2] = CpuDispatcher.ToUnorm8(value.Z);
            p[3] = CpuDispatcher.ToUnorm8(value.W);
            return;
        }

        if (typeof(T) == typeof(R8))
        {
            p[0] = CpuDispatcher.ToUnorm8(value.X);
            return;
        }

        if (typeof(T) == typeof(Half))
        {
            *(Half*)p = (Half)value.X;
            return;
        }

        if (typeof(T) == typeof(Rgba16Float))
        {
            *(Rgba16Float*)p = new Rgba16Float((Half)value.X, (Half)value.Y, (Half)value.Z, (Half)value.W);
            return;
        }

        if (typeof(T) == typeof(float))
        {
            *(float*)p = value.X;
            return;
        }

        if (typeof(T) == typeof(Float4))
        {
            *(Float4*)p = value;
            return;
        }

        // Rg16 and Rgba32Srgb are read-only formats; ReadWriteTexture2D rejects them at allocation.
        throw Unsupported<T>();
    }

    internal static NotSupportedException Unsupported<T>() =>
        new($"'{typeof(T).Name}' is not a texture element type; use {SupportedTypes}.");

    private static float[] CreateSrgbTable()
    {
        float[] table = new float[256];

        for (int i = 0; i < table.Length; i++)
        {
            double c = i / 255.0;
            table[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }

        return table;
    }
}
