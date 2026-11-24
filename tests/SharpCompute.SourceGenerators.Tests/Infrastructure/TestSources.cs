namespace SharpCompute.SourceGenerators.Tests;

/// <summary>Shared C# snippets for the generator tests.</summary>
internal static class TestSources
{
    /// <summary>
    /// Test-local vector types and intrinsics. The generator must not know any vector or intrinsic by
    /// name, so these deliberately use names that SharpCompute itself does not define; everything the
    /// generator needs comes from <c>[MslType]</c> and <c>[MslIntrinsic]</c>.
    /// </summary>
    public const string Vectors = """
        using System.Runtime.InteropServices;
        using SharpCompute;

        namespace TestVectors
        {
            [MslType("float2", Packed = "packed_float2")]
            [StructLayout(LayoutKind.Sequential, Pack = 4)]
            public struct Vec2
            {
                private float _x;
                private float _y;

                public Vec2(float x, float y) { _x = x; _y = y; }
                public Vec2(float value) { _x = value; _y = value; }

                public float X { readonly get => _x; set => _x = value; }
                public float Y { readonly get => _y; set => _y = value; }
                public Vec2 YX { readonly get => new(_y, _x); set { _y = value._x; _x = value._y; } }

                public static Vec2 operator +(Vec2 a, Vec2 b) => new(a._x + b._x, a._y + b._y);
                public static Vec2 operator -(Vec2 a, Vec2 b) => new(a._x - b._x, a._y - b._y);
                public static Vec2 operator *(Vec2 a, Vec2 b) => new(a._x * b._x, a._y * b._y);
                public static Vec2 operator *(Vec2 a, float b) => new(a._x * b, a._y * b);
                public static Vec2 operator *(float a, Vec2 b) => new(a * b._x, a * b._y);
                public static Vec2 operator /(Vec2 a, float b) => new(a._x / b, a._y / b);
                public static Vec2 operator %(Vec2 a, Vec2 b) => new(a._x % b._x, a._y % b._y);
                public static Vec2 operator -(Vec2 a) => new(-a._x, -a._y);
                public static implicit operator Vec2(float value) => new(value);
                public static explicit operator Vec2(IVec2 value) => new(value.X, value.Y);
            }

            [MslType("float3", Packed = "packed_float3")]
            [StructLayout(LayoutKind.Sequential, Pack = 4)]
            public struct Vec3
            {
                private float _x;
                private float _y;
                private float _z;

                public Vec3(float x, float y, float z) { _x = x; _y = y; _z = z; }
                public Vec3(Vec2 xy, float z) { _x = xy.X; _y = xy.Y; _z = z; }

                public float X { readonly get => _x; set => _x = value; }
                public float Y { readonly get => _y; set => _y = value; }
                public float Z { readonly get => _z; set => _z = value; }
                public Vec2 XY { readonly get => new(_x, _y); set { _x = value.X; _y = value.Y; } }
                public Vec3 ZYX => new(_z, _y, _x);
                public float this[int index] { readonly get => index == 0 ? _x : index == 1 ? _y : _z; set { if (index == 0) _x = value; else if (index == 1) _y = value; else _z = value; } }

                public static Vec3 operator +(Vec3 a, Vec3 b) => new(a._x + b._x, a._y + b._y, a._z + b._z);
                public static Vec3 operator -(Vec3 a, Vec3 b) => new(a._x - b._x, a._y - b._y, a._z - b._z);
                public static Vec3 operator *(Vec3 a, Vec3 b) => new(a._x * b._x, a._y * b._y, a._z * b._z);
                public static Vec3 operator *(Vec3 a, float b) => new(a._x * b, a._y * b, a._z * b);
                public static Vec3 operator *(float a, Vec3 b) => new(a * b._x, a * b._y, a * b._z);
                public static Vec3 operator /(Vec3 a, float b) => new(a._x / b, a._y / b, a._z / b);
                public static Vec3 operator -(Vec3 a) => new(-a._x, -a._y, -a._z);
            }

            [MslType("int2", Packed = "packed_int2")]
            [StructLayout(LayoutKind.Sequential, Pack = 4)]
            public struct IVec2
            {
                private int _x;
                private int _y;

                public IVec2(int x, int y) { _x = x; _y = y; }

                public int X { readonly get => _x; set => _x = value; }
                public int Y { readonly get => _y; set => _y = value; }

                public static IVec2 operator +(IVec2 a, IVec2 b) => new(a._x + b._x, a._y + b._y);
                public static IVec2 operator *(IVec2 a, int b) => new(a._x * b, a._y * b);
                public static IVec2 operator &(IVec2 a, IVec2 b) => new(a._x & b._x, a._y & b._y);
                public static IVec2 operator >>(IVec2 a, int b) => new(a._x >> b, a._y >> b);
                public static IVec2 operator %(IVec2 a, IVec2 b) => new(a._x % b._x, a._y % b._y);
                public static IVec2 operator ~(IVec2 a) => new(~a._x, ~a._y);
                public static explicit operator IVec2(Vec2 value) => new((int)value.X, (int)value.Y);
                public static explicit operator IVec2(SharpCompute.Int2 value) => new(value.X, value.Y);
            }

            /// <summary>A vector of halves: 2-byte alignment, 6 bytes, like packed_half3.</summary>
            [MslType("half3", Packed = "packed_half3")]
            [StructLayout(LayoutKind.Sequential, Pack = 2)]
            public struct HVec3
            {
                public System.Half X;
                public System.Half Y;
                public System.Half Z;
            }

            public static class Fn
            {
                [MslIntrinsic("metal::dot")]
                public static float Dot(Vec3 a, Vec3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

                [MslIntrinsic("metal::dot")]
                public static float Dot(Vec2 a, Vec2 b) => (a.X * b.X) + (a.Y * b.Y);

                [MslIntrinsic("metal::length")]
                public static float Length(Vec3 a) => System.MathF.Sqrt(Dot(a, a));

                [MslIntrinsic("metal::normalize")]
                public static Vec3 Normalize(Vec3 a) => a / Length(a);

                [MslIntrinsic("metal::clamp")]
                public static float Clamp(float value, float min, float max) => System.Math.Clamp(value, min, max);

                [MslIntrinsic("metal::mix")]
                public static T Lerp<T>(T a, T b, float t) => throw null!;

                [MslIntrinsic("metal::fract")]
                public static float Fract(this float value) => value - System.MathF.Floor(value);

                [MslIntrinsic("metal::modf")]
                public static float Modf(float value, out float integral) => throw null!;
            }
        }
        """;

    /// <summary>Wraps shader struct members in a standard declaration named <c>Tests.TestShader</c>.</summary>
    public static string Shader(string members, string attributes = "") => $$"""
        using System;
        using SharpCompute;
        using TestVectors;

        namespace Tests;

        {{attributes}}
        [GeneratedComputeShaderDescriptor]
        public readonly partial struct TestShader : IComputeShader
        {
        {{members}}
        }
        """;

    /// <summary>A shader with one float buffer, one float constant, and the given <c>Execute</c> body.</summary>
    public static string Body(string body, string extraMembers = "") => Shader($$"""
            public readonly ReadWriteBuffer<float> buffer;
            public readonly float factor;
            {{extraMembers}}

            public void Execute()
            {
                {{body}}
            }
        """);
}
