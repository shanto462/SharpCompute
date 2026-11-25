using System;
using System.Linq;
using static SharpCompute.Tests.CpuExecution.CpuComparison;

namespace SharpCompute.Tests.CodeReuse;

/// <summary>
/// Code shared between shaders, end to end: static helpers of other types, generic helpers, members
/// of user structs in every address space, and instance members of the shader. Each shader runs on
/// the GPU and, as the same C#, on the CPU; the two must agree, and agree with plain C# where the
/// result is exact.
/// </summary>
public class CodeReuseTests
{
    private static string Msl<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.MslSource;

    private static int Count(string text, string part) => text.Split(part).Length - 1;

    [MetalFact]
    public void StaticAndGenericHelpers_MatchTheCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int width = 40, height = 25;
        using ReadWriteBuffer<Float3> gpu = device.AllocateReadWriteBuffer<Float3>(width * height);
        using ReadWriteBuffer<Float3> cpu = device.AllocateReadWriteBuffer<Float3>(width * height);

        device.For(width, height, new SharedColors(gpu, 1.75f));
        device.ForCpu(width, height, new SharedColors(cpu, 1.75f));

        AssertClose(Flatten(gpu.ToArray()), Flatten(cpu.ToArray()));

        // The helpers are ordinary C#: threads 6 and 7 worked out by hand with the same methods. The
        // swap exchanged the color and the (low, high, squared difference) triple; 6 is a multiple of
        // 3 and even, so it picks the triple and adds 0, and 7 picks the color and adds 1.
        float t6 = SharedMath.Unit(6u), l6 = SharedMath.Luminance(SharedMath.Palette(t6) * 1.75f);
        float low = MathF.Min(t6, l6), high = MathF.Max(t6, l6);
        AssertClose(Flatten([new Float3(low, high, (high - low) * (high - low))]), Flatten([cpu.ToArray()[6]]));
        AssertClose(Flatten([(SharedMath.Palette(SharedMath.Unit(7u)) * 1.75f) + 1f]), Flatten([cpu.ToArray()[7]]));
    }

    [MetalFact]
    public void HelpersWithBuiltinsAndAtomics_AndTwoMethodsOfOneName_MatchTheCpuExactly()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int width = 64, height = 50, binCount = 16;
        using ReadWriteBuffer<int> gpuBins = device.AllocateReadWriteBuffer<int>(binCount);
        using ReadWriteBuffer<int> cpuBins = device.AllocateReadWriteBuffer<int>(binCount);
        using ReadWriteBuffer<uint> gpuHashes = device.AllocateReadWriteBuffer<uint>(width * height);
        using ReadWriteBuffer<uint> cpuHashes = device.AllocateReadWriteBuffer<uint>(width * height);

        device.For(width, height, new SharedBins(gpuBins, gpuHashes, binCount));
        device.ForCpu(width, height, new SharedBins(cpuBins, cpuHashes, binCount));

        uint[] expectedHashes = Enumerable.Range(0, width * height).Select(static i => SharedMath.Hash((uint)i) ^ OtherMath.Hash((uint)i)).ToArray();
        int[] expectedBins = new int[binCount];

        for (uint i = 0; i < width * height; i++)
        {
            expectedBins[SharedMath.Hash(i) % binCount]++;
        }

        Assert.Equal(expectedHashes, gpuHashes.ToArray());
        Assert.Equal(expectedHashes, cpuHashes.ToArray());
        Assert.Equal(expectedBins, gpuBins.ToArray());
        Assert.Equal(expectedBins, cpuBins.ToArray());
        Assert.Equal(width * height, gpuBins.ToArray().Sum());

        // A helper's atomic is the shader's atomic: such a shader is never split between the CPU and the GPU.
        Assert.True(SharedBins_UsesAtomics());

        static bool SharedBins_UsesAtomics() => UsesAtomics<SharedBins>();
    }

    private static bool UsesAtomics<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.UsesAtomics;

    [MetalTheory]
    [InlineData(64)]
    [InlineData(100)] // a last threadgroup that is not full
    public void StructMembers_InEveryAddressSpace_MatchTheCpu(int count)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Body[] start = Enumerable.Range(0, count)
            .Select(static i => new Body(new Float2(i * 0.5f, 10f - i), new Float2(1f + (i % 7), -0.5f * (i % 5))) { Mass = 1f + (i % 3) })
            .ToArray();
        Body[] seedBodies = Enumerable.Range(0, count)
            .Select(static i => new Body(new Float2(-i, i * 0.25f), new Float2(0.1f * i, 2f)) { Mass = 0.5f + (i % 4) })
            .ToArray();
        Body anchor = new(new Float2(3f, -4f), new Float2(0.5f, 0.25f)) { Mass = 4f };

        using ReadOnlyBuffer<Body> seeds = device.AllocateReadOnlyBuffer<Body>(seedBodies);
        using ReadWriteBuffer<Body> gpuBodies = device.AllocateReadWriteBuffer<Body>(start);
        using ReadWriteBuffer<Body> cpuBodies = device.AllocateReadWriteBuffer<Body>(start);
        using ReadWriteBuffer<float> gpuReport = device.AllocateReadWriteBuffer<float>(count * 4);
        using ReadWriteBuffer<float> cpuReport = device.AllocateReadWriteBuffer<float>(count * 4);

        device.For(count, new MoveBodies(gpuBodies, seeds, gpuReport, anchor, 0.125f));
        device.ForCpu(count, new MoveBodies(cpuBodies, seeds, cpuReport, anchor, 0.125f));

        AssertClose(gpuReport.ToArray(), cpuReport.ToArray());
        AssertClose(Flat(gpuBodies.ToArray()), Flat(cpuBodies.ToArray()));

        // Body 3, by hand: the methods modified the element of the read-write buffer in place...
        Body local = seedBodies[3];
        local.Kick(new Float2(0.5f, -0.25f));
        Body expected = start[3];
        expected.Step(0.125f);
        expected.Kick(local.Momentum());
        AssertClose(Flat([expected]), Flat([cpuBodies.ToArray()[3]]));
        Assert.NotEqual(start[3].Position, cpuBodies.ToArray()[3].Position);

        // ...and left the read-only buffer and the captured struct as they were: element 2 of the
        // report reads them after the calls that C# (and the MSL) makes on copies.
        float untouched = seedBodies[3].Position.X + seedBodies[3].Energy + anchor.Position.Y;
        AssertClose([untouched], [cpuReport.ToArray()[(3 * 4) + 2]]);
        AssertClose([untouched], [gpuReport.ToArray()[(3 * 4) + 2]]);
        Assert.Equal(seedBodies.Select(static b => b.Position), seeds.ToArray().Select(static b => b.Position));

        static float[] Flat(Body[] bodies) => bodies.SelectMany(static b => new[] { b.Position.X, b.Position.Y, b.Velocity.X, b.Velocity.Y, b.Mass }).ToArray();
    }

    [MetalFact]
    public void InstanceMembersOfTheShader_MatchTheCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(32);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(32);

        foreach (float offset in new[] { 0.25f, -0.25f })
        {
            device.For(32, new Curve(gpu, 1.5f, offset));
            device.ForCpu(32, new Curve(cpu, 1.5f, offset));

            AssertClose(gpu.ToArray(), cpu.ToArray());

            // this[index] + Choose(1, -1) + Choose((3, 4), (5, 6)).Y
            float[] expected = Enumerable.Range(0, 32).Select(i => (i * 3f) + offset + (offset > 0 ? 1f : -1f) + (offset > 0 ? 4f : 6f)).ToArray();
            AssertClose(expected, cpu.ToArray());
        }
    }

    [Fact]
    public void TheGeneratedMsl_HasEachSharedFunctionOnce_InTheShadersThatUseIt()
    {
        string colors = Msl<SharedColors>(), bins = Msl<SharedBins>(), bodies = Msl<MoveBodies>(), curve = Msl<Curve>();

        // Both shaders call SharedMath.Hash (one of them through Unit), and each has it exactly once.
        Assert.Equal(1, Count(colors, "static uint SharedMath_Hash(uint x)\n{"));
        Assert.Equal(1, Count(bins, "static uint SharedMath_Hash(uint x)\n{"));

        // Two types, two functions; a generic method, one function per type argument.
        Assert.Contains("static uint OtherMath_Hash(uint x)", bins);
        Assert.DoesNotContain("OtherMath_Hash", colors);
        Assert.Contains("static void SharedMath_Swap_float(thread float& a, thread float& b)", colors);
        Assert.Contains("static void SharedMath_Swap_float3(thread float3& a, thread float3& b)", colors);
        Assert.Contains("static float3 SharedMath_Pick_float3(bool condition, float3 whenTrue, float3 whenFalse)", colors);
        Assert.Contains("static float SharedMath_Pick_float(bool condition, float whenTrue, float whenFalse)", colors);

        // What a shader does not call is not in its MSL.
        Assert.DoesNotContain("SharedMath_Count", colors);
        Assert.DoesNotContain("SharedMath_Palette", bins);
        Assert.DoesNotContain("SharedMath", bodies);
        Assert.DoesNotContain("SharedMath", curve);

        // A method that modifies its struct exists once per address space it is called in.
        Assert.Contains("static void Body_Step(thread Body& __this, float dt)", bodies);
        Assert.Contains("static void Body_Step_device(device Body& __this, float dt)", bodies);
        Assert.Contains("static void Body_Step_threadgroup(threadgroup Body& __this, float dt)", bodies);
        Assert.Contains("static void Body_Step_copy(Body __this, float dt)", bodies);
        Assert.Contains("static void Body_Kick_device(device Body& __this, float2 impulse)", bodies);
        Assert.Contains("static void Body_Kick_copy(Body __this, float2 impulse)", bodies);

        // One that only reads takes a copy, wherever the struct lives.
        Assert.Equal(1, Count(bodies, "static float Body_Energy(Body __this)\n{"));
        Assert.Equal(1, Count(bodies, "static float2 Body_Momentum(Body __this)\n{"));
    }
}
