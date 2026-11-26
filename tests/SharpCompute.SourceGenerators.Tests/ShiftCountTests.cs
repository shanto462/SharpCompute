using System.Linq;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// A shift by a constant count is emitted with the count masked as C# masks it (31, or 63 for the
/// 64-bit types, also for the 8- and 16-bit targets of a compound shift, which C# shifts as ints);
/// a variable count is left to MSL, which takes it modulo the width of the promoted operand. Every
/// result is compiled with Metal.
/// </summary>
public class ShiftCountTests
{
    private const string Shader = "Tests.TestShader";

    private static string Statement(string body) =>
        GeneratorHarness.Run($$"""
            using SharpCompute;

            namespace Tests;

            [GeneratedComputeShaderDescriptor]
            [ThreadGroupSize(64, 1, 1)]
            public readonly partial struct TestShader(ReadWriteBuffer<int> ints, ReadWriteBuffer<long> longs, ReadWriteBuffer<short> shorts, ReadWriteBuffer<Int3> vectors) : IComputeShader
            {
                public void Execute()
                {
                    int i = ThreadIds.X;
                    int n = ints[0];
                    short s = shorts[0];
                    ints[2] = i + n + s;
                    {{body}}
                }
            }
            """).Verify(Shader).KernelBody.Split('\n').Last();

    [Theory]
    [InlineData("s <<= 33;", "s <<= 1;")]
    [InlineData("shorts[1] >>= 40;", "shorts[1] >>= 8;")]
    // A count of the target's width or more: the sign fill, or 0, which is what C# gives after
    // shifting the promoted int and truncating.
    [InlineData("s >>= 1431655765;", "s >>= 15;")]
    [InlineData("s <<= -1;", "s = short(0);")]
    [InlineData("shorts[1] <<= 16;", "shorts[1] = short(0);")]
    [InlineData("longs[1] <<= 65;", "longs[1] <<= 1;")]
    [InlineData("ints[1] >>= 32;", "ints[1] >>= 0;")]
    [InlineData("ints[1] <<= n;", "ints[1] <<= n;")]
    public void ACompoundShiftByAConstant_MasksTheCount(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Theory]
    [InlineData("ints[1] = s << 33;", "ints[1] = int(s) << 1;")]
    [InlineData("longs[1] = longs[0] >> 70;", "longs[1] = longs[0] >> 6;")]
    [InlineData("ints[1] = n >>> 40;", "ints[1] = int(uint(n) >> 8);")]
    [InlineData("ints[1] = n << n;", "ints[1] = n << n;")]
    public void AShiftByAConstant_MasksTheCount(string body, string expected) =>
        Assert.Equal(expected, Statement(body));
}
