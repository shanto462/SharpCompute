using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// The 8-, 16- and 64-bit integers (<c>byte</c> → <c>uchar</c>, <c>sbyte</c> → <c>char</c>,
/// <c>short</c>, <c>ushort</c>, <c>long</c>, <c>ulong</c>): the MSL for every type in every position a
/// scalar can take, the conversions C# inserts when it promotes them, their literals, the layout of
/// the constants struct with its drift guards, and what is diagnosed.
/// </summary>
public class ScalarTypeTests
{
    private const string Shader = "Tests.TestShader";

    private static GeneratorRun Run(string members, string attributes = "", string trailer = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(members, attributes) + trailer + InlineArrays);

    /// <summary>An inline array of a small integer, for the local array tests.</summary>
    private const string InlineArrays = """

        [System.Runtime.CompilerServices.InlineArray(2)]
        public struct Keys { private short _e; }
        """;

    private static GeneratorRun RunBody(string body, string extraMembers = "") => Run($$"""
            public readonly ReadWriteBuffer<byte> bytes;
            public readonly ReadWriteBuffer<sbyte> sbytes;
            public readonly ReadWriteBuffer<short> shorts;
            public readonly ReadWriteBuffer<ushort> ushorts;
            public readonly ReadWriteBuffer<long> longs;
            public readonly ReadWriteBuffer<ulong> ulongs;
            public readonly ReadWriteBuffer<int> ints;
            public readonly ReadWriteBuffer<uint> uints;
            public readonly ReadWriteBuffer<float> floats;
            {{extraMembers}}

            public void Execute()
            {
                {{body}}
            }
        """);

    private static void AssertKernel(string expected, string body, string extraMembers = "") =>
        Assert.Equal(expected.ReplaceLineEndings("\n"), RunBody(body, extraMembers).Verify(Shader).KernelBody);

    // ---- every type in every position -------------------------------------------------------------

    /// <summary>
    /// One shader per type with the type as a local, a buffer element (read-only and read-write), a
    /// captured constant, a field of a user struct, a <c>[GroupShared]</c> array, a helper's parameter
    /// and return type, a <c>const</c> and a <c>static readonly</c> field.
    /// </summary>
    private static GeneratorRun EveryPosition(string type) => Run(
        $$"""
            public readonly ReadWriteBuffer<{{type}}> values;
            public readonly ReadOnlyBuffer<{{type}}> source;
            public readonly ReadWriteBuffer<Item> items;
            public readonly {{type}} bias;

            [GroupShared]
            private static readonly {{type}}[] shared = new {{type}}[8];

            private const {{type}} Limit = 100;

            private static readonly {{type}} Start = 3;

            private static {{type}} Twice({{type}} x) => ({{type}})(x + x);

            public void Execute()
            {
                int i = ThreadIds.X;
                {{type}} local = source[i];
                shared[GroupIds.Index] = local;
                Gpu.GroupMemoryBarrierWithGroupSync();
                {{type}} sum = ({{type}})(local + bias);
                items[i].Value = Twice(sum);
                items[i].Index = Twice(Limit) > Start ? 1 : 0;
                values[i] = ({{type}})(shared[0] + Limit + Start);
            }
        """,
        attributes: "[ThreadGroupSize(8)]",
        trailer: $$"""

            public struct Item { public {{type}} Value; public int Index; }
            """);

    [Theory]
    [InlineData("byte", "uchar", 1)]
    [InlineData("sbyte", "char", 1)]
    [InlineData("short", "short", 2)]
    [InlineData("ushort", "ushort", 2)]
    public void SmallInteger_InEveryPosition_IsItsMslType_AndPromotesToIntWhereCSharpDoes(string type, string msl, int size)
    {
        GeneratorRun run = EveryPosition(type);
        ShaderDescriptor descriptor = run.Verify(Shader);
        string source = descriptor.MslSource;

        // Program-scope constants, the struct field, the constants block, the helper and the kernel's
        // buffers and threadgroup memory: all the MSL type, not a widened one.
        Assert.Contains($"constant {msl} Limit = {msl}(100);", source);
        Assert.Contains($"constant {msl} Start = {msl}(3);", source);
        Assert.Contains($"    {msl} Value;\n    int Index;", source);
        Assert.Contains($"    {msl} bias;", source);
        Assert.Contains($"static {msl} Twice({msl} x)", source);
        Assert.Contains($"device {msl}* values [[buffer(0)]]", source);
        Assert.Contains($"const device {msl}* source [[buffer(1)]]", source);
        Assert.Contains($"threadgroup {msl} shared[8];", source);
        Assert.Equal(8 * size, descriptor.ThreadgroupMemoryLength);

        // C# adds two small integers as ints and the cast truncates the sum: the MSL says the same,
        // with the int sum computed as a uint so that it wraps as C#'s does. (`Limit` widened to int
        // is a C# constant, so it is inlined as the literal it is there.)
        Assert.Equal($"return {msl}(int(uint(x) + uint(x)));", descriptor.FunctionBody($"static {msl} Twice"));
        Assert.Equal(
            $"""
            threadgroup {msl} shared[8];
            int i = int(__tid.x);
            {msl} local = source[i];
            shared[int(__gindex)] = local;
            metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
            {msl} sum = {msl}(int(uint(local) + uint(__c.bias)));
            items[i].Value = ::Twice(sum);
            items[i].Index = int(::Twice(::Limit)) > int(::Start) ? 1 : 0;
            values[i] = {msl}(int(uint(shared[0]) + 100u + uint(::Start)));
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);

        descriptor.AssertLayoutMatchesClr("Item", run.LoadType("Tests.Item"));
        descriptor.AssertLayoutMatchesClr("__Constants", descriptor.ConstantsType!);
    }

    [Theory]
    [InlineData("long", "long", "L")]
    [InlineData("ulong", "ulong", "UL")]
    public void WideInteger_InEveryPosition_IsItsMslType_AndNeedsNoPromotion(string type, string msl, string suffix)
    {
        GeneratorRun run = EveryPosition(type);
        ShaderDescriptor descriptor = run.Verify(Shader);
        string source = descriptor.MslSource;

        Assert.Contains($"constant {msl} Limit = 100{suffix};", source);
        Assert.Contains($"constant {msl} Start = 3{suffix};", source);
        Assert.Contains($"    {msl} Value;\n    int Index;", source);
        Assert.Contains($"    {msl} bias;", source);
        Assert.Contains($"static {msl} Twice({msl} x)", source);
        Assert.Contains($"device {msl}* values [[buffer(0)]]", source);
        Assert.Contains($"const device {msl}* source [[buffer(1)]]", source);
        Assert.Contains($"threadgroup {msl} shared[8];", source);
        Assert.Equal(64, descriptor.ThreadgroupMemoryLength);

        // 64-bit arithmetic is 64-bit in C# as well: no conversion appears, except that a signed sum
        // is computed as a ulong, where it wraps as C#'s does.
        bool signed = type == "long";
        Assert.Equal(signed ? "return long(ulong(x) + ulong(x));" : "return x + x;", descriptor.FunctionBody($"static {msl} Twice"));
        string sum = signed ? "long(ulong(local) + ulong(__c.bias))" : "local + __c.bias";
        string total = signed ? "long(ulong(shared[0]) + ulong(::Limit) + ulong(::Start))" : "shared[0] + ::Limit + ::Start";
        Assert.Equal(
            $"""
            threadgroup {msl} shared[8];
            int i = int(__tid.x);
            {msl} local = source[i];
            shared[int(__gindex)] = local;
            metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
            {msl} sum = {sum};
            items[i].Value = ::Twice(sum);
            items[i].Index = ::Twice(::Limit) > ::Start ? 1 : 0;
            values[i] = ({total});
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);

        // The struct holds a 64-bit field: 16 bytes, aligned to 8 on both sides.
        Assert.Contains("static_assert(sizeof(Item) == 16,", source);
        Assert.Contains("static_assert(__builtin_offsetof(Item, Index) == 8,", source);
        descriptor.AssertLayoutMatchesClr("Item", run.LoadType("Tests.Item"));
        descriptor.AssertLayoutMatchesClr("__Constants", descriptor.ConstantsType!);
    }

    // ---- promotions, literals, operators ----------------------------------------------------------

    [Fact]
    public void Promotions_AreTheConversionsOfTheCSharpExpression()
    {
        AssertKernel(
            """
            int i = int(__tid.x);
            uchar a = bytes[i];
            uchar b = bytes[int(uint(i) + 1u)];
            short s = shorts[i];
            uint u = uints[i];
            int sum = int(uint(a) + uint(b));
            uchar wrapped = uchar(int(uint(a) + uint(b)));
            short shifted = short(int(a) << 9);
            long wide = long(ulong(a) * 1000UL);
            long negated = long(-ulong(u));
            long mixed = long(ulong(i) + ulong(u));
            bool less = int(a) < int(s);
            int inverted = ~int(a);
            a += int(b);
            a++;
            s -= 1;
            ints[i] = int(uint(sum) + uint(wrapped) + uint(shifted) + uint(inverted) + uint(less ? 1 : 0) + uint(a) + uint(s));
            longs[i] = long(ulong(wide) + ulong(negated) + ulong(mixed));
            """,
            """
            int i = ThreadIds.X;
            byte a = bytes[i];
            byte b = bytes[i + 1];
            short s = shorts[i];
            uint u = uints[i];
            int sum = a + b;
            byte wrapped = (byte)(a + b);
            short shifted = (short)(a << 9);
            long wide = a * 1000L;
            long negated = -u;
            long mixed = i + u;
            bool less = a < s;
            int inverted = ~a;
            a += b;
            a++;
            s -= 1;
            ints[i] = sum + wrapped + shifted + inverted + (less ? 1 : 0) + a + s;
            longs[i] = wide + negated + mixed;
            """);
    }

    [Fact]
    public void Literals_HaveTheTypeTheyHaveInCSharp()
    {
        // A bare `5` is an int in MSL too: the small integers get a cast, the 64-bit ones a suffix.
        AssertKernel(
            """
            uchar a = uchar(5);
            char b = char(-5);
            short c = short(300);
            ushort d = ushort(65535);
            long e = 5L;
            long f = 9223372036854775807L;
            long g = -9223372036854775807L - 1L;
            long h = -42L;
            ulong k = 18446744073709551615UL;
            ulong m = 1UL;
            uchar n = uchar(255);
            char p = char(-128);
            short q = short(-32768);
            uchar zero = uchar(0);
            long none = long(0);
            bytes[0] = uchar(int(uint(a) + uint(n) + uint(zero)));
            sbytes[0] = char(int(uint(b) + uint(p)));
            shorts[0] = short(int(uint(c) + uint(q)));
            ushorts[0] = d;
            longs[0] = long(ulong(e) + ulong(f) + ulong(g) + ulong(h) + ulong(none) + 4294967296UL);
            ulongs[0] = k + m + (1UL << 40);
            """,
            """
            byte a = 5;
            sbyte b = -5;
            short c = 300;
            ushort d = 65535;
            long e = 5;
            long f = 0x7FFFFFFFFFFFFFFF;
            long g = long.MinValue;
            long h = -42;
            ulong k = ulong.MaxValue;
            ulong m = 1;
            byte n = byte.MaxValue;
            sbyte p = sbyte.MinValue;
            short q = short.MinValue;
            byte zero = default;
            long none = default(long);
            bytes[0] = (byte)(a + n + zero);
            sbytes[0] = (sbyte)(b + p);
            shorts[0] = (short)(c + q);
            ushorts[0] = d;
            longs[0] = e + f + g + h + none + 4294967296;
            ulongs[0] = k + m + (1UL << 40);
            """);
    }

    [Fact]
    public void Shifts_KeepTheCSharpOperandTypes_AndUnsignedShiftGoesThroughTheUnsignedType()
    {
        // MSL masks the count with the width of the (promoted) left operand, as C# does: 31 for the
        // small integers, which are ints by then, and 63 for the 64-bit ones.
        AssertKernel(
            """
            int n = int(__tid.x);
            long a = longs[0];
            ulong u = ulongs[0];
            char sb = sbytes[0];
            longs[1] = a << n;
            longs[2] = a >> n;
            longs[3] = long(ulong(a) >> n);
            ulongs[1] = (u << n) | (u >> (int(64u - uint(n))));
            ints[0] = int(uint(int(sb)) >> n);
            ints[1] = int(sb) << n;
            a <<= 3;
            u >>= n;
            longs[4] = long(ulong(a) + u);
            """,
            """
            int n = ThreadIds.X;
            long a = longs[0];
            ulong u = ulongs[0];
            sbyte sb = sbytes[0];
            longs[1] = a << n;
            longs[2] = a >> n;
            longs[3] = a >>> n;
            ulongs[1] = (u << n) | (u >> (64 - n));
            ints[0] = sb >>> n;
            ints[1] = sb << n;
            a <<= 3;
            u >>= n;
            longs[4] = a + (long)u;
            """);
    }

    [Fact]
    public void SwitchConditionalAndHelpers_WorkOnTheNewTypes()
    {
        GeneratorRun run = RunBody(
            """
            long a = longs[0];
            byte b = bytes[0];
            long picked = a > 0 ? a : long.MinValue;
            byte small = b > 100 ? b : (byte)200;

            switch (b)
            {
                case 1:
                    picked += 1;
                    break;
                case 255:
                    picked -= 1;
                    break;
            }

            switch (a)
            {
                case -1L:
                    small = 0;
                    break;
                case 0x100000000:
                    small = 1;
                    break;
            }

            Bump(ref picked, small);
            longs[1] = picked;
            """,
            "private static void Bump(ref long total, byte step) { total += step; }");

        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Equal(
            """
            long a = longs[0];
            uchar b = bytes[0];
            long picked = a > 0L ? a : -9223372036854775807L - 1L;
            uchar small = int(b) > 100 ? b : uchar(200);
            switch (b)
            {
                case uchar(1):
                {
                    picked = long(ulong(picked) + 1UL);
                    break;
                }
                case uchar(255):
                {
                    picked = long(ulong(picked) - 1UL);
                    break;
                }
            }
            switch (a)
            {
                case -1L:
                {
                    small = uchar(0);
                    break;
                }
                case 4294967296L:
                {
                    small = uchar(1);
                    break;
                }
            }
            ::Bump(picked, small);
            longs[1] = picked;
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
        Assert.Contains("static void Bump(thread long& total, uchar step)", descriptor.MslSource);
        Assert.Equal("total = long(ulong(total) + ulong(step));", descriptor.FunctionBody("static void Bump"));
    }

    // ---- conversions ------------------------------------------------------------------------------

    [Fact]
    public void FloatToInteger_GoesThroughTheSaturatingHelperOfTheTargetType()
    {
        GeneratorRun run = RunBody(
            """
            float f = floats[0];
            Half h = (Half)f;
            longs[0] = (long)f;
            longs[1] = (long)h;
            ulongs[0] = (ulong)f;
            bytes[0] = (byte)f;
            sbytes[0] = (sbyte)f;
            shorts[0] = (short)f;
            ushorts[0] = (ushort)f;
            ints[0] = (int)f;
            uints[0] = (uint)f;
            floats[1] = longs[2];
            floats[2] = ulongs[2];
            floats[3] = (float)bytes[2] + shorts[2];
            longs[3] = ints[1];
            longs[4] = (long)ulongs[3];
            """);

        ShaderDescriptor descriptor = run.Verify(Shader);

        // MSL leaves a float to integer conversion out of range undefined, and the Metal compiler
        // folds on that ground; C# saturates. Every such conversion goes through a helper that clamps
        // in the float domain first. The integer conversions and those to float are plain casts.
        Assert.Equal(
            """
            float f = floats[0];
            half h = __ms_to_half(f);
            longs[0] = __ms_sat_long(f);
            longs[1] = __ms_sat_long(h);
            ulongs[0] = __ms_sat_ulong(f);
            bytes[0] = __ms_sat_uchar(f);
            sbytes[0] = __ms_sat_char(f);
            shorts[0] = __ms_sat_short(f);
            ushorts[0] = __ms_sat_ushort(f);
            ints[0] = __ms_sat_int(f);
            uints[0] = __ms_sat_uint(f);
            floats[1] = float(longs[2]);
            floats[2] = float(ulongs[2]);
            floats[3] = float(bytes[2]) + float(shorts[2]);
            longs[3] = long(ints[1]);
            longs[4] = long(ulongs[3]);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);

        // The bounds are floats: the largest below 2^31 (2^32, 2^63, 2^64), and a select for the rest.
        Assert.Contains("inline uchar __ms_sat_uchar(float x) { float y = x > 0.0f ? x : 0.0f; return uchar(metal::min(y, 255.0f)); }", descriptor.MslSource);
        Assert.Contains("inline char __ms_sat_char(float x) { float y = x == x ? x : 0.0f; return char(metal::clamp(y, -128.0f, 127.0f)); }", descriptor.MslSource);
        Assert.Contains(
            "inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }",
            descriptor.MslSource);
        Assert.Contains(
            "inline uint __ms_sat_uint(float x) { float y = x > 0.0f ? x : 0.0f; uint r = uint(metal::min(y, 4294967040.0f)); return x >= 4294967296.0f ? 4294967295u : r; }",
            descriptor.MslSource);
        Assert.Contains(
            "inline long __ms_sat_long(float x) { float y = x == x ? x : 0.0f; long r = long(metal::clamp(y, -9223372036854775808.0f, 9223371487098961920.0f)); return x >= 9223372036854775808.0f ? 9223372036854775807L : r; }",
            descriptor.MslSource);
        Assert.Contains(
            "inline ulong __ms_sat_ulong(float x) { float y = x > 0.0f ? x : 0.0f; ulong r = ulong(metal::min(y, 18446742974197923840.0f)); return x >= 18446744073709551616.0f ? 18446744073709551615UL : r; }",
            descriptor.MslSource);
        Assert.Contains("inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }", descriptor.MslSource);

        // The helpers are a block of their own, of which the shader carries the ones it calls: not
        // the rest of the prelude, and not the conversions to a vector.
        Assert.DoesNotContain("__ms_radians", descriptor.MslSource);
        Assert.DoesNotContain("__ms_sat_int2", descriptor.MslSource);
    }

    /// <summary>
    /// The conversions of an integer that went through a float or a half, which the Metal compiler
    /// folded when they were plain casts (<c>uchar(float(i))</c> into a truncation of <c>i</c>,
    /// <c>uint(float(s))</c> into a zero extension), and of vectors. The GPU side is pinned in
    /// SharpCompute.Differential.Tests (KnownDifferencesTests) and in SaturatingConversionTests.
    /// </summary>
    [Fact]
    public void FloatToInteger_OfConvertedIntegersAndOfVectors_GoesThroughTheHelpersToo()
    {
        GeneratorRun run = RunBody(
            """
            int i = ints[0];
            short s = shorts[0];
            Half h = (Half)floats[0];
            bytes[0] = (byte)(float)i;
            sbytes[0] = (sbyte)h;
            ushorts[0] = (ushort)(Half)s;
            uints[0] = (uint)(float)s;
            ulongs[0] = (ulong)h;
            ints[1] = (int)(float)s;
            Int3 v = (Int3)new Float3(floats[0], floats[1], floats[2]);
            UInt2 u = (UInt2)new Half2(h, h);
            ints[2] = v.X + (int)u.Y;
            """);

        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Equal(
            """
            int i = ints[0];
            short s = shorts[0];
            half h = __ms_to_half(floats[0]);
            bytes[0] = __ms_sat_uchar(float(i));
            sbytes[0] = __ms_sat_char(h);
            ushorts[0] = __ms_sat_ushort(half(s));
            uints[0] = __ms_sat_uint(float(s));
            ulongs[0] = __ms_sat_ulong(h);
            ints[1] = __ms_sat_int(float(s));
            int3 v = __ms_sat_int3(float3(floats[0], floats[1], floats[2]));
            uint2 u = __ms_sat_uint2(half2(h, h));
            ints[2] = int(uint(v.x) + u.y);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
        Assert.Contains(
            "inline int3 __ms_sat_int3(float3 x) { float3 y = metal::select(float3(0.0f), x, x == x); int3 r = int3(metal::clamp(y, float3(-2147483648.0f), float3(2147483520.0f))); return metal::select(r, int3(2147483647), x >= 2147483648.0f); }",
            descriptor.MslSource);
        Assert.Contains("inline uint2 __ms_sat_uint2(half2 x) { return __ms_sat_uint2(float2(x)); }", descriptor.MslSource);
    }

    /// <summary>C# folds the conversion of a constant, and the MSL holds what C# folded it to: the value <c>ForCpu</c> runs with.</summary>
    [Fact]
    public void FloatToInteger_OfAConstant_IsTheConstantCSharpFolded()
    {
        AssertKernel(
            """
            ints[0] = 7;
            ints[1] = int(uint(ints[0]) + uint(250));
            bytes[0] = uchar(200);
            longs[0] = -3L;
            """,
            """
            ints[0] = (int)(Scale * 2f);
            ints[1] = ints[0] + (int)(Scale * 71.6f);
            bytes[0] = (byte)(Scale * 57.3f);
            longs[0] = (long)(-Scale);
            """,
            "private const float Scale = 3.5f;");
    }

    /// <summary>
    /// The parentheses the C# has are kept, except around a primary expression: C++ reads
    /// <c>(float(uint(x))) * y</c> as a C-style cast of <c>*y</c> to the function type
    /// <c>float(uint x)</c>, so a parenthesized nested conversion followed by <c>*</c>, <c>-</c>,
    /// <c>+</c> or <c>&amp;</c> did not compile. Verify compiles the MSL.
    /// </summary>
    [Fact]
    public void ParenthesizedNestedConversion_BeforeAnOperatorThatIsAlsoUnary_Compiles()
    {
        AssertKernel(
            """
            floats[0] = float(uint(longs[0])) * floats[1];
            ulongs[0] = ulong(short(ushorts[0])) - ulong(bytes[0]);
            floats[1] = +(float(half(shorts[0])) * floats[2]);
            """,
            """
            floats[0] = ((float)(uint)longs[0]) * floats[1];
            ulongs[0] = ((ulong)(short)ushorts[0]) - (ulong)bytes[0];
            floats[1] = +((float)(Half)shorts[0] * floats[2]);
            """);
    }

    [Fact]
    public void ShaderWithoutAFloatToIntegerOrHalfConversion_HasNoPrelude()
    {
        // Integer conversions, conversions to float, and a half of a constant within its range.
        ShaderDescriptor descriptor = RunBody("longs[0] = (long)ulongs[0] + bytes[0] + (long)(int)uints[0]; floats[0] = (float)(Half)ints[1] + (float)(Half)0.5f + ints[0];").Verify(Shader);

        Assert.DoesNotContain("__ms_", descriptor.MslSource);
    }

    /// <summary>
    /// The Metal compiler makes a constant beyond the range of a half 65504 where it converts one
    /// itself, and moves the conversion to the constants of a max, a min or a clamp to do so, so a
    /// float converts to a half through a helper that selects the infinity. A constant within the
    /// range keeps the cast. The GPU side is pinned in HalfConversionTests.
    /// </summary>
    [Fact]
    public void FloatToHalf_GoesThroughTheHelper_ExceptForAConstantWithinTheRangeOfAHalf()
    {
        GeneratorRun run = RunBody(
            """
            float f = floats[0];
            Half h = (Half)f;
            Half inRange = (Half)0.5f;
            Half edge = (Half)(-65519.99f);
            Half beyond = (Half)65520f;
            Half3 v = (Half3)new Float3(f, 1f, 2f);
            Half3 constants = (Half3)new Float3(0.25f, -3f, 1000f);
            Half2 huge = (Half2)new Float2(1e30f, 1f);
            Half fromInt = (Half)ints[0];
            floats[1] = (float)h + (float)inRange + (float)edge + (float)beyond + (float)v.X + (float)constants.Y + (float)huge.X + (float)fromInt;
            """);

        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Equal(
            """
            float f = floats[0];
            half h = __ms_to_half(f);
            half inRange = half(0.5f);
            half edge = half(-65519.99f);
            half beyond = __ms_to_half(65520.0f);
            half3 v = __ms_to_half3(float3(f, 1.0f, 2.0f));
            half3 constants = half3(float3(0.25f, -3.0f, 1000.0f));
            half2 huge = __ms_to_half2(float2(1E+30f, 1.0f));
            half fromInt = half(ints[0]);
            floats[1] = float(h) + float(inRange) + float(edge) + float(beyond) + float(v.x) + float(constants.y) + float(huge.x) + float(fromInt);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
        Assert.Contains("inline half __ms_to_half(float x) { return metal::abs(x) >= 65520.0f ? (x < 0.0f ? half(-INFINITY) : half(INFINITY)) : half(x); }", descriptor.MslSource);
        Assert.Contains(
            "inline half3 __ms_to_half3(float3 x) { return metal::select(half3(x), metal::select(half3(INFINITY), half3(-INFINITY), x < 0.0f), metal::abs(x) >= 65520.0f); }",
            descriptor.MslSource);

        // Only the helpers the shader calls: no conversion to an integer, and no half4.
        Assert.DoesNotContain("__ms_sat_", descriptor.MslSource);
        Assert.DoesNotContain("__ms_to_half4", descriptor.MslSource);
    }

    [Fact]
    public void FloatToHalf_InAProgramScopeInitializer_IsWrittenOutAsAConstantExpression()
    {
        ShaderDescriptor descriptor = RunBody(
            "floats[0] = (float)Tint.X + (float)FromBig + (float)Lanes.Y;",
            """
            private static readonly float Big = 1e30f;
                private static readonly Half3 Tint = (Half3)new Float3(0.5f, 0.25f, 2f);
                private static readonly Half FromBig = (Half)Big;
                private static readonly Half2 Lanes = (Half2)new Float2(Big, 1f);
            """).Verify(Shader);

        // A vector of constants within the range keeps the cast.
        Assert.Contains("constant half3 Tint = half3(float3(0.5f, 0.25f, 2.0f));", descriptor.MslSource);

        // An initializer must not call a function: the selection is a constant expression, per lane for a vector.
        Assert.Contains("constant half FromBig = (::Big >= 65520.0f ? half(INFINITY) : (::Big <= -65520.0f ? half(-INFINITY) : half(::Big)));", descriptor.MslSource);
        Assert.Contains(
            "constant half2 Lanes = half2(float2(::Big, 1.0f).x >= 65520.0f ? half(INFINITY) : (float2(::Big, 1.0f).x <= -65520.0f ? half(-INFINITY) : half(float2(::Big, 1.0f).x)), "
            + "float2(::Big, 1.0f).y >= 65520.0f ? half(INFINITY) : (float2(::Big, 1.0f).y <= -65520.0f ? half(-INFINITY) : half(float2(::Big, 1.0f).y)));",
            descriptor.MslSource);
        Assert.DoesNotContain("__ms_", descriptor.MslSource);
    }

    [Fact]
    public void FloatToInteger_InAProgramScopeInitializer_IsFoldedByCSharpOrWrittenOutAsAConstantExpression()
    {
        // A constant conversion is folded by C#, so no call is needed.
        ShaderDescriptor folded = RunBody(
            "longs[0] = Scaled;",
            "private static readonly long Scaled = (long)2.75f * 1000;").Verify(Shader);

        Assert.Contains("constant long Scaled = long(ulong(2L) * 1000UL);", folded.MslSource);

        // One that reads another program-scope constant cannot call the helper: an initializer must
        // not call a function. It is the same saturation as a constant expression of comparisons.
        ShaderDescriptor written = RunBody(
            "longs[0] = Scaled + Small + Count;",
            """
            private static readonly float Factor = 2.75f;
                private static readonly Half Tiny = (Half)(-3f);
                private static readonly long Scaled = (long)Factor;
                private static readonly sbyte Small = (sbyte)(Factor * 100f);
                private static readonly uint Count = (uint)Tiny;
            """).Verify(Shader);

        Assert.Contains(
            "constant long Scaled = long(::Factor >= 9223372036854775808.0f ? 9223372036854775807L : (::Factor >= -9223372036854775808.0f ? long(::Factor) : (::Factor < 0.0f ? (-9223372036854775807L - 1L) : 0L)));",
            written.MslSource);
        Assert.Contains(
            "constant char Small = char((::Factor * 100.0f) >= 128.0f ? 127 : ((::Factor * 100.0f) >= -128.0f ? char(::Factor * 100.0f) : ((::Factor * 100.0f) < 0.0f ? -128 : 0)));",
            written.MslSource);
        Assert.Contains(
            "constant uint Count = uint(float(::Tiny) >= 4294967296.0f ? 4294967295u : (float(::Tiny) > 0.0f ? uint(float(::Tiny)) : 0u));",
            written.MslSource);
        Assert.DoesNotContain("__ms_", written.MslSource);

        // A vector's conversion is a call, and there is no constant expression for it.
        GeneratorRun run = RunBody(
            "ints[0] = Cell.X;",
            "private static readonly Float3 Point = new(1.5f, 2.5f, 3.5f);\n    private static readonly Int3 Cell = (Int3)Point;");

        Assert.Contains(
            "a conversion from a floating-point vector to 'int3' is a function call in MSL",
            run.AssertOnlyDiagnostic("SCG0024", "(Int3)Point").GetMessage());
    }

    [Fact]
    public void IntrinsicCalls_InAProgramScopeInitializer_AreOnlyFoldedForIntAndUInt()
    {
        // Operators and literals of the new types are fine in an initializer...
        ShaderDescriptor operators = RunBody(
            "longs[0] = Mask + Small;",
            "private static readonly long Mask = (1L << 40) - 1;\n    private static readonly byte Small = (byte)(Three + 4);\n    private const int Three = 3;").Verify(Shader);

        Assert.Contains("constant long Mask = long(ulong(1L << 40) - 1UL);", operators.MslSource);
        Assert.Contains("constant uchar Small = uchar(int(uint(::Three) + 4u));", operators.MslSource);

        // ...but a call has to be evaluated here, and the folder only knows float, int and uint.
        GeneratorRun run = RunBody("longs[0] = Lowest;", "private static readonly long Lowest = Gpu.Min(5L, 3L);");

        Assert.Contains("cannot be evaluated at compile time", run.AssertOnlyDiagnostic("SCG0024", "Gpu.Min(5L, 3L)").GetMessage());
    }

    // ---- the constants struct ---------------------------------------------------------------------

    [Fact]
    public void Constants_OfSmallTypes_SitAtTheirNaturalAlignment_InTheSequentialPack4Mirror()
    {
        GeneratorRun run = Run("""
                public readonly ReadWriteBuffer<int> output;
                public readonly byte a;
                public readonly int b;
                public readonly sbyte c;
                public readonly short d;
                public readonly byte e;
                public readonly ushort f;
                public readonly Half g;
                public readonly byte h;

                public void Execute() => output[0] = a + b + c + d + e + f + (int)(float)g + h;
            """);

        ShaderDescriptor descriptor = run.Verify(Shader);
        string source = descriptor.MslSource;

        // A byte is aligned to 1 and a short to 2, so `d` follows `c` after one byte of padding and
        // `h` makes the struct end on a multiple of 4.
        Assert.Contains("static_assert(sizeof(__Constants) == 20,", source);

        foreach ((string name, int offset) in new[] { ("a", 0), ("b", 4), ("c", 8), ("d", 10), ("e", 12), ("f", 14), ("g", 16), ("h", 18) })
        {
            Assert.Contains($"static_assert(__builtin_offsetof(__Constants, {name}) == {offset},", source);
        }

        Type constants = descriptor.ConstantsType!;
        Assert.Equal(LayoutKind.Sequential, constants.StructLayoutAttribute!.Value);
        Assert.Equal(4, constants.StructLayoutAttribute.Pack);
        descriptor.AssertLayoutMatchesClr("__Constants", constants);
    }

    [Fact]
    public void Constants_WithA64BitInteger_UseAnExplicitMirror_PinnedByTheDriftGuards()
    {
        GeneratorRun run = Run("""
                public readonly ReadWriteBuffer<long> output;
                public readonly byte tag;
                public readonly long key;
                public readonly short delta;
                public readonly ulong hash;
                public readonly int offset;
                public readonly Vec3 direction;
                public readonly long last;
                public readonly float scale;

                public void Execute() => output[0] = tag + key + delta + (long)hash + offset + (long)direction.X + last + (long)scale;
            """);

        ShaderDescriptor descriptor = run.Verify(Shader);
        string source = descriptor.MslSource;

        // MSL aligns a long to 8, which `Pack = 4` cannot express: the offsets are the natural ones,
        // asserted in the MSL and stated field by field in the C# mirror.
        Assert.Contains(
            """
            struct __Constants
            {
                uchar tag;
                long key;
                short delta;
                ulong hash;
                int offset;
                packed_float3 direction;
                long last;
                float scale;
            };
            static_assert(sizeof(__Constants) == 64, "SharpCompute: size of '__Constants' does not match its C# layout");
            """.ReplaceLineEndings("\n"),
            source);

        foreach ((string name, int offset) in new[] { ("tag", 0), ("key", 8), ("delta", 16), ("hash", 24), ("offset", 32), ("direction", 36), ("last", 48), ("scale", 56) })
        {
            Assert.Contains($"static_assert(__builtin_offsetof(__Constants, {name}) == {offset},", source);
        }

        Type constants = descriptor.ConstantsType!;
        Assert.Equal(LayoutKind.Explicit, constants.StructLayoutAttribute!.Value);
        Assert.Equal(64, constants.StructLayoutAttribute.Size);
        Assert.Equal(64, Marshal.SizeOf(constants));
        descriptor.AssertLayoutMatchesClr("__Constants", constants);

        string generated = run.Source("TestShader.SharpCompute.g.cs");
        Assert.Contains("[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Explicit, Size = 64)]", generated);
        Assert.Contains("[global::System.Runtime.InteropServices.FieldOffset(8)] public long key;", generated);
        Assert.Contains("[global::System.Runtime.InteropServices.FieldOffset(36)] public global::TestVectors.Vec3 direction;", generated);
    }

    [Fact]
    public void Constants_WithAStructThatHoldsA64BitInteger_AreExplicitToo()
    {
        GeneratorRun run = Run(
            """
                public readonly ReadWriteBuffer<long> output;
                public readonly int first;
                public readonly Entry entry;
                public readonly byte last;

                public void Execute() => output[0] = first + entry.Key + entry.Tag + last;
            """,
            trailer: "\npublic struct Entry { public byte Tag; public long Key; }");

        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Contains("static_assert(sizeof(Entry) == 16,", descriptor.MslSource);
        Assert.Contains("static_assert(sizeof(__Constants) == 32,", descriptor.MslSource);
        Assert.Contains("static_assert(__builtin_offsetof(__Constants, entry) == 8,", descriptor.MslSource);
        Assert.Contains("static_assert(__builtin_offsetof(__Constants, last) == 24,", descriptor.MslSource);
        Assert.Equal(LayoutKind.Explicit, descriptor.ConstantsType!.StructLayoutAttribute!.Value);
        descriptor.AssertLayoutMatchesClr("Entry", run.LoadType("Tests.Entry"));
        descriptor.AssertLayoutMatchesClr("__Constants", descriptor.ConstantsType!);
    }

    [MetalFact]
    public void DriftGuards_FailCompilation_WhenTheLayoutOfA64BitConstantDisagrees()
    {
        ShaderDescriptor descriptor = Run("""
                public readonly ReadWriteBuffer<long> output;
                public readonly byte tag;
                public readonly long key;
                public readonly int offset;

                public void Execute() => output[0] = tag + key + offset;
            """).Verify(Shader);

        // The MSL as generated compiles (Verify). With the member a `Pack = 4` layout would produce (a
        // long at offset 4) the guards reject it instead of letting the shader read the wrong bytes.
        string packed = descriptor.MslSource.Replace("    long key;", "    packed_int2 key;", StringComparison.Ordinal);
        Assert.NotEqual(descriptor.MslSource, packed);

        MetalCompiler.Result result = MetalCompiler.Compile(packed, descriptor.EntryPoint);

        Assert.False(result.Success);
        Assert.Contains("does not match its C# layout", result.Messages);
    }

    [Fact]
    public void Constants_WithA64BitInteger_AreBoundCorrectlyOnTheGpu()
    {
        if (!MetalCompiler.IsAvailable)
        {
            return;
        }

        GeneratorRun run = Run("""
                public readonly ReadWriteBuffer<float> output;
                public readonly byte tag;
                public readonly long key;
                public readonly short delta;
                public readonly ulong hash;
                public readonly float scale;

                public TestShader(ReadWriteBuffer<float> output, byte tag, long key, short delta, ulong hash, float scale)
                {
                    this.output = output;
                    this.tag = tag;
                    this.key = key;
                    this.delta = delta;
                    this.hash = hash;
                    this.scale = scale;
                }

                public void Execute()
                {
                    output[0] = tag;
                    output[1] = (float)(key >> 40);
                    output[2] = delta;
                    output[3] = (float)(hash >> 56);
                    output[4] = scale;
                    output[5] = (float)(key & 0xFFFF);
                }
            """);

        float[] output = GpuDispatch.For(run, Shader, threads: 1, outputLength: 6, (byte)201, 0x0000_7B00_0000_1234L, (short)-321, 0xC800_0000_0000_0000UL, 2.5f);

        Assert.Equal([201f, 123f, -321f, 200f, 2.5f, 0x1234], output);
    }

    // ---- user structs -----------------------------------------------------------------------------

    [Fact]
    public void StructFields_OfEveryNewType_AreLaidOutNaturally_WithDriftGuards()
    {
        GeneratorRun run = Run(
            """
                public readonly ReadWriteBuffer<Record> records;

                public void Execute()
                {
                    int i = ThreadIds.X;
                    records[i].Key += records[i].Tag;
                    records[i].Count = (ushort)(records[i].Delta + records[i].Sign);
                    records[i].Hash ^= (ulong)records[i].Key;
                }
            """,
            trailer: "\npublic struct Record { public byte Tag; public long Key; public short Delta; public ulong Hash; public sbyte Sign; public ushort Count; }");

        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Contains(
            """
            struct Record
            {
                uchar Tag;
                long Key;
                short Delta;
                ulong Hash;
                char Sign;
                ushort Count;
            };
            static_assert(sizeof(Record) == 40, "SharpCompute: size of 'Record' does not match its C# layout");
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);
        Assert.Equal(
            """
            int i = int(__tid.x);
            records[i].Key = long(ulong(records[i].Key) + ulong(records[i].Tag));
            records[i].Count = ushort(int(uint(records[i].Delta) + uint(records[i].Sign)));
            records[i].Hash ^= ulong(records[i].Key);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
        descriptor.AssertLayoutMatchesClr("Record", run.LoadType("Tests.Record"));
    }

    [Theory]
    [InlineData("long", 4, 8)]
    [InlineData("ulong", 1, 8)]
    [InlineData("short", 1, 2)]
    [InlineData("ushort", 1, 2)]
    public void StructPackedBelowTheAlignmentOfANewType_IsRejected(string type, int pack, int alignment)
    {
        // MSL has no packed form of these scalars, so a field at an under-aligned offset cannot be mirrored.
        GeneratorRun run = Run(
            """
                public readonly ReadWriteBuffer<Tight> items;

                public void Execute()
                {
                }
            """,
            trailer: $$"""

                [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = {{pack}})]
                public struct Tight { public byte Tag; public {{type}} Value; }
                """);

        Assert.Contains(
            $"Pack = {pack} is below the natural alignment ({alignment}) of its fields",
            run.AssertOnlyDiagnostic("SCG0009", $"ReadWriteBuffer<Tight>").GetMessage());
    }

    // ---- local arrays -----------------------------------------------------------------------------

    [Fact]
    public void LocalArrays_OfTheNewTypes_AreMslArraysOfThoseTypes()
    {
        GeneratorRun run = RunBody(
            """
            int i = ThreadIds.X;
            Span<byte> window = stackalloc byte[4];
            long[] sums = new long[3];
            ReadOnlySpan<ushort> weights = [1, 4, 6, 4, 1];
            ReadOnlySpan<ulong> primes = [0x100000001B3, 0xD6E8FEB86659FD93];
            sbyte[] signs = [-1, 1];
            Keys keys = default;

            for (int k = 0; k < window.Length; k++)
            {
                window[k] = bytes[i + k];
                sums[k % 3] += window[k] * weights[k];
            }

            keys[1] = (short)(sums[0] >> 3);
            keys[0] += keys[1];
            ulongs[i] = (ulong)sums[1] * primes[1] + primes[0];
            longs[i] = sums[0] + (sums[2] * signs[i & 1]) + keys[0];
            """);

        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Equal(
            """
            int i = int(__tid.x);
            uchar window[4] = {};
            long sums[3] = {};
            const ushort weights[5] = { ushort(1), ushort(4), ushort(6), ushort(4), ushort(1) };
            const ulong primes[2] = { 1099511628211UL, 15485907386658061715UL };
            char signs[2] = { char(-1), char(1) };
            Keys keys = Keys{};
            for (int k = 0; k < 4; k = int(uint(k) + 1u))
            {
                window[k] = bytes[int(uint(i) + uint(k))];
                sums[k % 3] = long(ulong(sums[k % 3]) + ulong(int(uint(window[k]) * uint(weights[k]))));
            }
            keys._e[1] = short(sums[0] >> 3);
            keys._e[0] += int(keys._e[1]);
            ulongs[i] = ulong(sums[1]) * primes[1] + primes[0];
            longs[i] = long(ulong(sums[0]) + (ulong(sums[2]) * ulong(signs[i & 1])) + ulong(keys._e[0]));
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
        Assert.Contains("struct Keys\n{\n    short _e[2];\n};", descriptor.MslSource);
    }

    [Fact]
    public void InlineArray_Of64BitElements_InABuffer_HasTheClrLayout()
    {
        GeneratorRun run = Run(
            """
                public readonly ReadWriteBuffer<Slot> slots;

                public void Execute()
                {
                    int i = ThreadIds.X;
                    slots[i].Totals[1] = slots[i].Totals[0] + slots[i].Tag;
                }
            """,
            trailer: """

                [System.Runtime.CompilerServices.InlineArray(3)]
                public struct Totals { private long _e; }

                public struct Slot { public byte Tag; public Totals Totals; public ushort Count; }
                """);

        ShaderDescriptor descriptor = run.Verify(Shader);

        // A byte, then three longs aligned to 8, then a ushort: 40 bytes on both sides.
        Assert.Contains("static_assert(sizeof(Totals) == 24,", descriptor.MslSource);
        Assert.Contains("static_assert(sizeof(Slot) == 40,", descriptor.MslSource);
        Assert.Contains("static_assert(__builtin_offsetof(Slot, Totals) == 8,", descriptor.MslSource);
        Assert.Contains("static_assert(__builtin_offsetof(Slot, Count) == 32,", descriptor.MslSource);
        Assert.Equal("int i = int(__tid.x);\nslots[i].Totals._e[1] = long(ulong(slots[i].Totals._e[0]) + ulong(slots[i].Tag));", descriptor.KernelBody);
        descriptor.AssertLayoutMatchesClr("Slot", run.LoadType("Tests.Slot"));
    }

    // ---- shared code: static helpers of other types, generic methods, struct members --------------

    private const string SharedIntegers = """

        public static class Bits
        {
            public const ulong Prime = 0x100000001B3;

            public static ulong Feed(ulong hash, byte value) => (hash ^ value) * Prime;

            public static byte High(ushort value) => (byte)(value >> 8);

            public static long Widen(sbyte small, short medium) => (long)small * medium;

            public static T Pick<T>(bool condition, T a, T b) => condition ? a : b;

            public static void Swap<T>(ref T a, ref T b)
            {
                T held = a;
                a = b;
                b = held;
            }

            public static T Load<T>(ReadWriteBuffer<T> data, int index) where T : unmanaged => data[index];
        }

        public struct Tally
        {
            public ulong Total;
            public short Delta;
            public byte Count;

            public Tally(ulong total, short delta)
            {
                Total = total;
                Delta = delta;
                Count = 1;
            }

            public readonly long Signed => (long)Total + Delta;

            public readonly ushort Scaled(byte factor) => (ushort)(Count * factor);

            public void Add(byte value)
            {
                Total += value;
                Count++;
            }

            public static Tally operator +(Tally a, Tally b) => new Tally(a.Total + b.Total, (short)(a.Delta + b.Delta));

            public static explicit operator long(Tally tally) => tally.Signed;
        }
        """;

    [Fact]
    public void SharedHelpers_TakeAndReturnTheNewTypes()
    {
        ShaderDescriptor descriptor = Run(
            """
                public readonly ReadWriteBuffer<byte> bytes;
                public readonly ReadWriteBuffer<ushort> ushorts;
                public readonly ReadWriteBuffer<long> longs;
                public readonly ReadWriteBuffer<ulong> ulongs;

                public void Execute()
                {
                    int i = ThreadIds.X;
                    ulongs[i] = Bits.Feed(Bits.Feed(ulongs[i], bytes[i]), Bits.High(ushorts[i]));
                    longs[i] = Bits.Widen((sbyte)bytes[i], (short)ushorts[i]);
                }
            """,
            trailer: SharedIntegers).Verify(Shader);

        Assert.Contains(
            """
            static ulong Bits_Feed(ulong hash, uchar value);
            static uchar Bits_High(ushort value);
            static long Bits_Widen(char small, short medium);
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);
        // A const of another type is inlined, with the type it has there.
        Assert.Equal("return (hash ^ ulong(value)) * 1099511628211UL;", descriptor.FunctionBody("static ulong Bits_Feed"));
        Assert.Equal("return uchar(int(value) >> 8);", descriptor.FunctionBody("static uchar Bits_High"));
        Assert.Equal("return long(ulong(small) * ulong(medium));", descriptor.FunctionBody("static long Bits_Widen"));
        Assert.Equal(
            """
            int i = int(__tid.x);
            ulongs[i] = ::Bits_Feed(::Bits_Feed(ulongs[i], bytes[i]), ::Bits_High(ushorts[i]));
            longs[i] = ::Bits_Widen(char(bytes[i]), short(ushorts[i]));
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [Fact]
    public void GenericHelpers_AreSpecializedForTheNewTypes()
    {
        ShaderDescriptor descriptor = Run(
            """
                public readonly ReadWriteBuffer<byte> bytes;
                public readonly ReadWriteBuffer<short> shorts;
                public readonly ReadWriteBuffer<long> longs;
                public readonly ReadWriteBuffer<ulong> ulongs;

                public void Execute()
                {
                    int i = ThreadIds.X;
                    byte b = Bits.Load(bytes, i), b2 = Bits.Load(bytes, i + 1);
                    long a = Bits.Load(longs, i), a2 = longs[i + 1];
                    Bits.Swap(ref b, ref b2);
                    Bits.Swap(ref a, ref a2);
                    bytes[i] = Bits.Pick(b > b2, b, b2);
                    shorts[i] = Bits.Pick(a > a2, shorts[i], (short)-1);
                    longs[i] = Bits.Pick(a > a2, a, a2);
                    ulongs[i] = Bits.Pick(b > 7, ulongs[i], 1UL << 40);
                }
            """,
            trailer: SharedIntegers).Verify(Shader);

        // One function per type argument, named after the MSL type.
        Assert.Contains(
            """
            static uchar Bits_Load_uchar(device uchar* data, int index);
            static long Bits_Load_long(device long* data, int index);
            static void Bits_Swap_uchar(thread uchar& a, thread uchar& b);
            static void Bits_Swap_long(thread long& a, thread long& b);
            static uchar Bits_Pick_uchar(bool condition, uchar a, uchar b);
            static short Bits_Pick_short(bool condition, short a, short b);
            static long Bits_Pick_long(bool condition, long a, long b);
            static ulong Bits_Pick_ulong(bool condition, ulong a, ulong b);
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);
        Assert.Equal("long held = a;\na = b;\nb = held;", descriptor.FunctionBody("static void Bits_Swap_long"));
        Assert.Contains("shorts[i] = ::Bits_Pick_short(a > a2, shorts[i], short(-1));", descriptor.KernelBody);
        Assert.Contains("ulongs[i] = ::Bits_Pick_ulong(int(b) > 7, ulongs[i], 1UL << 40);", descriptor.KernelBody);
    }

    [Fact]
    public void StructMembers_WorkOnFieldsOfTheNewTypes()
    {
        GeneratorRun run = Run(
            """
                public readonly ReadWriteBuffer<Tally> tallies;
                public readonly ReadWriteBuffer<long> longs;
                public readonly byte step;

                public void Execute()
                {
                    int i = ThreadIds.X;
                    Tally tally = new Tally((ulong)longs[i], -3);
                    tally.Add(step);
                    Tally sum = tally + tallies[i];
                    tallies[i].Add(step);
                    longs[i] = sum.Signed + sum.Scaled(step) + (long)tallies[i];
                }
            """,
            trailer: SharedIntegers);

        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Contains(
            """
            struct Tally
            {
                ulong Total;
                short Delta;
                uchar Count;
            };
            static_assert(sizeof(Tally) == 16, "SharpCompute: size of 'Tally' does not match its C# layout");
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);
        Assert.Contains("static Tally Tally_new(ulong total, short delta)", descriptor.MslSource);
        Assert.Contains("static long Tally_Signed(Tally __this)", descriptor.MslSource);
        Assert.Contains("static ushort Tally_Scaled(Tally __this, uchar factor)", descriptor.MslSource);
        Assert.Contains("static void Tally_Add(thread Tally& __this, uchar value)", descriptor.MslSource);
        Assert.Contains("static void Tally_Add_device(device Tally& __this, uchar value)", descriptor.MslSource);
        Assert.Contains("static Tally Tally_op_Addition(Tally a, Tally b)", descriptor.MslSource);
        Assert.Contains("static long Tally_op_Explicit(Tally tally)", descriptor.MslSource);
        Assert.Equal("return long(__this.Total + ulong(__this.Delta));", descriptor.FunctionBody("static long Tally_Signed"));
        Assert.Equal("return ushort(int(uint(__this.Count) * uint(factor)));", descriptor.FunctionBody("static ushort Tally_Scaled"));
        Assert.Equal("__this.Total += ulong(value);\n__this.Count++;", descriptor.FunctionBody("static void Tally_Add(thread"));
        Assert.Equal("return ::Tally_new(a.Total + b.Total, short(int(uint(a.Delta) + uint(b.Delta))));", descriptor.FunctionBody("static Tally Tally_op_Addition"));
        descriptor.AssertLayoutMatchesClr("Tally", run.LoadType("Tests.Tally"));
    }

    [Fact]
    public void AGenericHelper_CannotBeSpecializedForATypeThatStaysUnsupported()
    {
        GeneratorRun run = Run(
            """
                public readonly ReadWriteBuffer<long> longs;

                public void Execute() => longs[0] = (long)Bits.Pick(longs[1] > 0, (nint)1, (nint)2);
            """,
            trailer: SharedIntegers);

        Assert.Equal(
            "Generic method 'Pick' cannot be specialized for the type argument 'nint': native-sized integers are not supported; use 'long' or 'ulong' instead",
            run.AssertOnlyDiagnostic("SCG0046", "Bits.Pick(longs[1] > 0, (nint)1, (nint)2)").GetMessage());
    }

    // ---- intrinsics -------------------------------------------------------------------------------

    [Fact]
    public void GpuIntrinsics_KeepTheTypeOfTheirArgument()
    {
        // A typed literal picks the overload of the small type; widening the value picks the int one.
        AssertKernel(
            """
            int n = int(__tid.x);
            uchar b = bytes[0];
            char sb = sbytes[0];
            short s = shorts[0];
            ushort us = ushorts[0];
            long a = longs[0];
            ulong u = ulongs[0];
            bytes[1] = metal::min(b, uchar(100));
            bytes[2] = metal::clz(b);
            bytes[3] = __ms_rotate_left(b, n);
            sbytes[1] = metal::abs(sb);
            sbytes[2] = metal::reverse_bits(sb);
            shorts[1] = metal::clamp(s, short(-5), short(5));
            ushorts[1] = __ms_popcount(us);
            longs[1] = long(ulong(metal::abs(a)) + ulong(metal::ctz(a)));
            ulongs[1] = metal::max(u, 1UL << 40);
            ulongs[2] = __ms_rotate_right(u, n);
            ints[0] = int(uint(metal::min(int(b), 5)) + uint(__ms_clz(int(b))));
            """,
            """
            int n = ThreadIds.X;
            byte b = bytes[0];
            sbyte sb = sbytes[0];
            short s = shorts[0];
            ushort us = ushorts[0];
            long a = longs[0];
            ulong u = ulongs[0];
            bytes[1] = Gpu.Min(b, (byte)100);
            bytes[2] = Gpu.LeadingZeroCount(b);
            bytes[3] = Gpu.RotateLeft(b, n);
            sbytes[1] = Gpu.Abs(sb);
            sbytes[2] = Gpu.ReverseBits(sb);
            shorts[1] = Gpu.Clamp(s, (short)-5, (short)5);
            ushorts[1] = Gpu.CountBits(us);
            longs[1] = Gpu.Abs(a) + Gpu.TrailingZeroCount(a);
            ulongs[1] = Gpu.Max(u, 1UL << 40);
            ulongs[2] = Gpu.RotateRight(u, n);
            ints[0] = Gpu.Min((int)b, 5) + Gpu.LeadingZeroCount((int)b);
            """);
    }

    [Fact]
    public void BitOperationsAndMath_On64BitValues_MapToTheSameMetalFunctions()
    {
        AssertKernel(
            """
            int n = int(__tid.x);
            long a = longs[0];
            long b = longs[1];
            ulong u = ulongs[0];
            ints[0] = int(uint(int(__ms_popcount(u))) + uint(int(__ms_clz(u))) + uint(int(metal::ctz(u))) + uint(int(metal::ctz(a))));
            ulongs[1] = metal::rotate(u, ulong(uint(n) & 63u)) ^ metal::rotate(u, ulong((0u - uint(int(uint(n) + 1u))) & 63u));
            longs[2] = long(ulong(metal::min(a, b)) + ulong(metal::max(a, b)) + ulong(__ms_math_clamp(a, -10L, 10L)) + ulong(long(metal::abs(b))));
            bytes[0] = metal::max(bytes[1], bytes[2]);
            shorts[0] = short(metal::abs(shorts[1]));
            """,
            """
            int n = ThreadIds.X;
            long a = longs[0];
            long b = longs[1];
            ulong u = ulongs[0];
            ints[0] = System.Numerics.BitOperations.PopCount(u) + System.Numerics.BitOperations.LeadingZeroCount(u) + System.Numerics.BitOperations.TrailingZeroCount(u) + System.Numerics.BitOperations.TrailingZeroCount(a);
            ulongs[1] = System.Numerics.BitOperations.RotateLeft(u, n) ^ System.Numerics.BitOperations.RotateRight(u, n + 1);
            longs[2] = Math.Min(a, b) + Math.Max(a, b) + Math.Clamp(a, -10L, 10L) + Math.Abs(b);
            bytes[0] = Math.Max(bytes[1], bytes[2]);
            shorts[0] = Math.Abs(shorts[1]);
            """);
    }

    // ---- atomics ----------------------------------------------------------------------------------

    [Fact]
    public void AtomicMinAndMax_OnAUlongBufferElement_AreTheOnly64BitAtomics()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            ulong key = ((ulong)ThreadIds.X << 32) | uints[0];
            Gpu.AtomicMin(ref ulongs[0], key);
            Gpu.AtomicMax(ref ulongs[ThreadIds.X & 1], key);
            """).Verify(Shader);

        // Not "fetch" functions: MSL 3.2 has atomic_min_explicit and atomic_max_explicit for an
        // atomic_ulong in device memory, and they return nothing.
        Assert.Equal(
            """
            ulong key = (ulong(int(__tid.x)) << 32) | ulong(uints[0]);
            metal::atomic_min_explicit((device metal::atomic_ulong*)&(ulongs[0]), key, metal::memory_order_relaxed);
            metal::atomic_max_explicit((device metal::atomic_ulong*)&(ulongs[int(__tid.x) & 1]), key, metal::memory_order_relaxed);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
        Assert.True(descriptor.UsesAtomics);
        Assert.True(descriptor.UsesDeviceAtomics);
    }

    [Theory]
    [InlineData("Gpu.AtomicAdd(ref ulongs[0], 1UL);", "ulongs[0]", "AtomicAdd", "ulong", "there is no 64-bit add, exchange or compare-and-swap")]
    [InlineData("Gpu.AtomicExchange(ref ulongs[0], 1UL);", "ulongs[0]", "AtomicExchange", "ulong", "Gpu.AtomicMin and Gpu.AtomicMax, which return nothing")]
    [InlineData("Gpu.AtomicMin(ref longs[0], 1L);", "longs[0]", "AtomicMin", "long", "MSL 3.2 has no atomics for long")]
    [InlineData("Gpu.AtomicAdd(ref longs[0], 1L);", "longs[0]", "AtomicAdd", "long", "Use a ReadWriteBuffer<ulong> for those")]
    [InlineData("Gpu.AtomicAdd(ref bytes[0], (byte)1);", "bytes[0]", "AtomicAdd", "byte", "MSL has no 8- or 16-bit atomics")]
    [InlineData("Gpu.AtomicMax(ref sbytes[0], (sbyte)1);", "sbytes[0]", "AtomicMax", "sbyte", "MSL has no 8- or 16-bit atomics")]
    [InlineData("Gpu.AtomicOr(ref shorts[0], (short)1);", "shorts[0]", "AtomicOr", "short", "keep the value in a buffer or [GroupShared] array of int or uint")]
    [InlineData("Gpu.AtomicCompareExchange(ref ushorts[0], (ushort)1, (ushort)2);", "ushorts[0]", "AtomicCompareExchange", "ushort", "MSL has no 8- or 16-bit atomics")]
    [InlineData("Gpu.AtomicAdd(value: 1UL, location: ref ulongs[1]);", "ulongs[1]", "AtomicAdd", "ulong", "there is no 64-bit add")]
    public void AtomicsWithoutAnMslEquivalent_AreExplainedNextToTheCompilerError(string body, string location, string method, string type, string reason)
    {
        GeneratorRun run = RunBody(body);

        // No overload takes the location, so C# rejects the call (CS1503 says "cannot convert from
        // 'ref long' to 'ref int'"); SCG0047 says what is actually missing.
        Assert.Contains(run.Input.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0047", location);
        string message = diagnostic.GetMessage();

        Assert.StartsWith($"The atomic operation '{method}' is not available for '{type}' elements: ", message);
        Assert.Contains(reason, message);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Theory]
    [InlineData("ulong", "Gpu.AtomicMax(ref shared[GroupIds.Index], 1UL);", "AtomicMax", "for buffer elements only, not for threadgroup memory")]
    [InlineData("ulong", "Gpu.AtomicAdd(ref shared[GroupIds.Index], 1UL);", "AtomicAdd", "for buffer elements only, not for threadgroup memory")]
    [InlineData("long", "Gpu.AtomicAdd(ref shared[GroupIds.Index], 1L);", "AtomicAdd", "MSL 3.2 has no atomics for long")]
    [InlineData("short", "Gpu.AtomicAdd(ref shared[GroupIds.Index], (short)1);", "AtomicAdd", "MSL has no 8- or 16-bit atomics")]
    public void AtomicsOnGroupSharedElementsOfTheNewTypes_AreDiagnosed(string type, string body, string method, string reason)
    {
        GeneratorRun run = Run(
            $$"""
                public readonly ReadWriteBuffer<int> output;

                [GroupShared]
                private static readonly {{type}}[] shared = new {{type}}[64];

                public void Execute()
                {
                    {{body}}
                }
            """,
            attributes: "[ThreadGroupSize(64)]");

        string message = run.AssertOnlyDiagnostic("SCG0047", "shared[GroupIds.Index]").GetMessage();

        Assert.StartsWith($"The atomic operation '{method}' is not available for '{type}' elements: ", message);
        Assert.Contains(reason, message);
    }

    [Theory]
    [InlineData("Gpu.AtomicAdd(ref ints[0], \"one\");")]
    [InlineData("Gpu.AtomicAdd(ref floats[0], 1);")]
    [InlineData("Gpu.AtomicMin(ref floats[0], 1f);")]
    [InlineData("Gpu.Min(ref longs[0], 1L);")]
    [InlineData("Missing(ref longs[0]);")]
    public void OtherInvalidCalls_GetNoAtomicExplanation(string body)
    {
        GeneratorRun run = RunBody(body);

        // Only an atomic on an element type without one is explained; the rest is the compiler's to report.
        Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "SCG0047");
    }

    // ---- what stays unsupported -------------------------------------------------------------------

    [Theory]
    [InlineData("char", "'char' is not supported; use 'ushort' instead")]
    [InlineData("nint", "native-sized integers are not supported; use 'long' or 'ulong' instead")]
    [InlineData("nuint", "native-sized integers are not supported; use 'long' or 'ulong' instead")]
    [InlineData("System.Int128", "128-bit integers are not supported; use 'long' or 'ulong' instead")]
    [InlineData("System.UInt128", "128-bit integers are not supported; use 'long' or 'ulong' instead")]
    [InlineData("System.IntPtr", "native-sized integers are not supported")]
    public void OtherIntegerTypes_AreStillRejected_AndNameTheAlternative(string type, string reason)
    {
        GeneratorRun run = Run($$"""
                public readonly ReadWriteBuffer<{{type}}> values;

                public void Execute()
                {
                }
            """);

        Assert.Contains(reason, run.AssertOnlyDiagnostic("SCG0006", $"ReadWriteBuffer<{type}>").GetMessage());
    }

    [Theory]
    [InlineData("long a = longs[0]; a >>>= n; longs[1] = a;", "a = long(ulong(a) >> n);")]
    [InlineData("int a = (int)longs[0]; a >>>= n; longs[1] = a;", "a = int(uint(a) >> n);")]
    [InlineData("short a = shorts[0]; a >>>= n; shorts[1] = a;", "a = short(uint(int(a)) >> n);")]
    public void UnsignedShiftAssignment_OnASignedTarget_ShiftsTheUnsignedTypeOfItsPromotedWidth(string body, string expected)
    {
        // MSL's >> is arithmetic on a signed type; C# shifts the (promoted) bits as unsigned.
        Assert.Contains(expected, RunBody("int n = ThreadIds.X; " + body).Verify(Shader).KernelBody);
    }

    [Fact]
    public void UnsignedShiftAssignment_OnASignedTargetWithSideEffects_IsUnsupported()
    {
        RunBody("int k = ThreadIds.X; longs[k++] >>>= 3;").AssertOnlyDiagnostic("SCG0012", "longs[k++] >>>= 3");
    }
}
