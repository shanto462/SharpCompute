using System;

namespace SharpCompute;

/// <summary>Opts a shader struct into descriptor generation (C# → MSL translation).</summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class GeneratedComputeShaderDescriptorAttribute : Attribute;

/// <summary>Threads per threadgroup. When omitted, the runtime picks a size per dispatch rank.</summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ThreadGroupSizeAttribute(int x, int y = 1, int z = 1) : Attribute
{
    public int X { get; } = x;

    public int Y { get; } = y;

    public int Z { get; } = z;
}

/// <summary>
/// Maps a C# vector struct to an MSL type. <see cref="Packed"/> is used wherever the value crosses
/// the CPU/GPU boundary (buffer elements, constants, struct fields). Properties map to lowercase
/// swizzles, constructors to <c>name(...)</c>, operators to native MSL operators.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class MslTypeAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    public string? Packed { get; init; }
}

/// <summary>
/// Maps a static two-argument method to an MSL binary operator, e.g. <c>Gpu.Mul</c> →
/// <c>[MslBinaryOperator("*", SwapOperands = true)]</c> emits <c>(arg1 * arg0)</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MslBinaryOperatorAttribute(string op) : Attribute
{
    public string Operator { get; } = op;

    public bool SwapOperands { get; init; }
}

/// <summary>Maps a static method to an MSL function, e.g. <c>[MslIntrinsic("metal::sqrt")]</c>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MslIntrinsicAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
