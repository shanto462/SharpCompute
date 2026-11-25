using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// Identifier handling for emitted MSL. A C# identifier is kept as-is unless it would clash with
/// an MSL/C++ keyword, a built-in type, a stdlib macro, or a name reserved for the generator
/// (anything starting with <c>__</c>); in that case it is prefixed with <c>__ms_</c>.
/// </summary>
/// <remarks>
/// The mapping is injective: escaped names always start with <c>__ms_</c> and unescaped names never
/// start with <c>__</c>, so every other <c>__</c>-prefixed name (<c>__c</c>, <c>__tid</c>,
/// <c>__Constants</c>, <c>__local_*</c>, <c>__length_*</c>, <c>__len_*</c>, <c>__i_*</c>) belongs to the generator alone.
/// </remarks>
internal static class MslNames
{
    public const string EscapePrefix = "__ms_";
    public const string LocalPrefix = "__local_";
    public const string LengthPrefix = "__length_";

    /// <summary>The hidden <c>int</c> parameter that follows a local array parameter of a helper (<c>__len_values</c>).</summary>
    public const string ArrayLengthPrefix = "__len_";

    /// <summary>The index of the loop a <c>foreach</c> over a local array is lowered to (<c>__i_value</c>).</summary>
    public const string ForEachIndexPrefix = "__i_";
    public const string Constants = "__c";
    public const string ConstantsStruct = "__Constants";
    public const string Output = "__output";

    /// <summary>
    /// The prefix of the program-scope <c>constexpr sampler</c> of each <c>Sampler</c> property that is
    /// used (<c>__ms_sampler_linear_clamp</c>). No C# identifier escapes to such a name: <see cref="Escape"/>
    /// only adds <c>__ms_</c> to reserved names, and no reserved name starts with <c>sampler_</c>.
    /// </summary>
    public const string SamplerPrefix = "__ms_sampler_";

    /// <summary>
    /// The prelude's conversions between a boundary type it declares (see
    /// <c>GpuType.NeedsExplicitPacking</c>) and the native MSL type, overloaded per type.
    /// </summary>
    public const string Unpack = "__ms_unpack";
    public const string Pack = "__ms_pack";

    /// <summary>The struct of the hidden grid window (see <c>MslSourceBuilder</c>), bound only for partial-grid dispatches.</summary>
    public const string GridWindowStruct = "__GridWindow";
    public const string GridWindow = "__window";

    /// <summary>The bool function constant (index 0) that selects the windowed specialization of a kernel.</summary>
    public const string Windowed = "__windowed";

    private static readonly HashSet<string> Reserved = CreateReserved();

    /// <summary>Replaces characters that are not valid in an ASCII C identifier.</summary>
    public static string Sanitize(string name)
    {
        StringBuilder? builder = null;

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            bool valid = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_' || (c is >= '0' and <= '9' && i > 0);

            if (valid)
            {
                builder?.Append(c);
                continue;
            }

            builder ??= new StringBuilder(name, 0, i, name.Length + 8);
            builder.Append("_u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
        }

        return builder?.ToString() ?? name;
    }

    /// <summary>Returns the MSL identifier to use for a C# identifier.</summary>
    public static string Escape(string name)
    {
        string sanitized = Sanitize(name);

        return IsReserved(sanitized) ? EscapePrefix + sanitized : sanitized;
    }

    public static bool IsReserved(string name) =>
        name.StartsWith("__", StringComparison.Ordinal) || Reserved.Contains(name);

    private static HashSet<string> CreateReserved()
    {
        var set = new HashSet<string>(StringComparer.Ordinal)
        {
            // C++14 keywords and alternative tokens.
            "alignas", "alignof", "and", "and_eq", "asm", "auto", "bitand", "bitor", "bool", "break", "case", "catch",
            "char", "char16_t", "char32_t", "class", "compl", "const", "constexpr", "const_cast", "continue",
            "decltype", "default", "delete", "do", "double", "dynamic_cast", "else", "enum", "explicit", "export",
            "extern", "false", "float", "for", "friend", "goto", "if", "inline", "int", "long", "mutable", "namespace",
            "new", "noexcept", "not", "not_eq", "nullptr", "operator", "or", "or_eq", "private", "protected", "public",
            "register", "reinterpret_cast", "return", "short", "signed", "sizeof", "static", "static_assert",
            "static_cast", "struct", "switch", "template", "this", "thread_local", "throw", "true", "try", "typedef",
            "typeid", "typename", "union", "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while",
            "xor", "xor_eq", "final", "override", "restrict", "concept", "requires", "co_await", "co_return",
            "co_yield", "char8_t", "consteval", "constinit", "import", "module",

            // MSL qualifiers, address spaces, and entry-point keywords (verified to be rejected as identifiers),
            // plus names the generator relies on resolving to their built-in meaning.
            "kernel", "vertex", "fragment", "device", "constant", "thread", "threadgroup", "threadgroup_imageblock",
            "ray_data", "object_data", "main", "metal", "simd", "std",

            // The texture type and its access qualifier, which texture parameters and locals name unqualified.
            "texture2d", "access",

            // Scalar types and common typedefs. Vector and matrix types are added below.
            "uchar", "ushort", "uint", "ulong", "half", "bfloat", "size_t", "ptrdiff_t", "intptr_t", "uintptr_t",
            "int8_t", "uint8_t", "int16_t", "uint16_t", "int32_t", "uint32_t", "int64_t", "uint64_t",

            // Macros defined by the Metal standard library.
            "INFINITY", "NAN", "MAXFLOAT", "HUGE_VALF", "HUGE_VALH", "MAXHALF", "NULL", "FLT_DIG", "FLT_MANT_DIG",
            "FLT_MAX_10_EXP", "FLT_MAX_EXP", "FLT_MIN_10_EXP", "FLT_MIN_EXP", "FLT_RADIX", "FLT_MAX", "FLT_MIN",
            "FLT_EPSILON", "HALF_DIG", "HALF_MANT_DIG", "HALF_MAX_10_EXP", "HALF_MAX_EXP", "HALF_MIN_10_EXP",
            "HALF_MIN_EXP", "HALF_RADIX", "HALF_MAX", "HALF_MIN", "HALF_EPSILON", "CHAR_BIT", "SCHAR_MAX",
            "SCHAR_MIN", "UCHAR_MAX", "CHAR_MAX", "CHAR_MIN", "USHRT_MAX", "SHRT_MAX", "SHRT_MIN", "UINT_MAX",
            "INT_MAX", "INT_MIN", "ULONG_MAX", "LONG_MAX", "LONG_MIN", "M_E_F", "M_LOG2E_F", "M_LOG10E_F", "M_LN2_F",
            "M_LN10_F", "M_PI_F", "M_PI_2_F", "M_PI_4_F", "M_1_PI_F", "M_2_PI_F", "M_2_SQRTPI_F", "M_SQRT2_F",
            "M_SQRT1_2_F", "M_E_H", "M_LOG2E_H", "M_LOG10E_H", "M_LN2_H", "M_LN10_H", "M_PI_H", "M_PI_2_H",
            "M_PI_4_H", "M_1_PI_H", "M_2_PI_H", "M_2_SQRTPI_H", "M_SQRT2_H", "M_SQRT1_2_H", "METAL_FUNC",
            "METAL_ASM", "METAL_INTERNAL", "METAL_CONST_ARG", "METAL_ALIGN", "METAL_PURE",
        };

        // Vector types (and their packed forms) for every scalar, plus matrices.
        string[] scalars = { "bool", "char", "uchar", "short", "ushort", "int", "uint", "long", "ulong", "half", "float", "double" };

        foreach (string scalar in scalars)
        {
            for (int n = 2; n <= 4; n++)
            {
                set.Add(scalar + n.ToString(CultureInfo.InvariantCulture));
                set.Add("packed_" + scalar + n.ToString(CultureInfo.InvariantCulture));
            }
        }

        foreach (string scalar in new[] { "half", "float", "double" })
        {
            for (int columns = 2; columns <= 4; columns++)
            {
                for (int rows = 2; rows <= 4; rows++)
                {
                    set.Add(scalar + columns.ToString(CultureInfo.InvariantCulture) + "x" + rows.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        return set;
    }
}

/// <summary>Hands out unique names in one MSL scope, appending <c>_1</c>, <c>_2</c>… on collisions.</summary>
internal sealed class NameAllocator
{
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Used => _used;

    public string Allocate(string preferred)
    {
        string name = preferred;

        for (int i = 1; !_used.Add(name); i++)
        {
            name = preferred + "_" + i.ToString(CultureInfo.InvariantCulture);
        }

        return name;
    }
}
