using System;
using System.Threading.Tasks;
using SharpCompute.Benchmarks.Shaders;

namespace SharpCompute.Benchmarks.Workloads;

/// <summary>
/// The integer workload of the native baselines: <see cref="HashRounds"/> over an array of random
/// <c>uint</c>, generated and hand-written, with the same rounds on the CPU to check them against.
/// </summary>
public sealed class Hash : IDisposable
{
    public Hash(int length, int rounds, GraphicsDevice device)
    {
        Length = length;
        Rounds = rounds;
        Input = new uint[length];
        Random random = new(7);

        for (int i = 0; i < length; i++)
        {
            Input[i] = (uint)random.NextInt64(1L << 32);
        }

        Device = device;
        GpuInput = device.AllocateReadOnlyBuffer<uint>(Input);
        GpuOutput = device.AllocateReadWriteBuffer<uint>(length);
    }

    public int Length { get; }

    public int Rounds { get; }

    public uint[] Input { get; }

    public GraphicsDevice Device { get; }

    public ReadOnlyBuffer<uint> GpuInput { get; }

    public ReadWriteBuffer<uint> GpuOutput { get; }

    /// <summary>Hash rounds of one dispatch.</summary>
    public double RoundsPerDispatch => (double)Length * Rounds;

    /// <summary>The same rounds on the CPU. Integer arithmetic wraps the same way everywhere, so the GPU must match exactly.</summary>
    public uint[] CpuReference()
    {
        uint[] output = new uint[Length];
        int rounds = Rounds;

        Parallel.For(0, Length, i =>
        {
            uint h = Input[i];

            for (int r = 0; r < rounds; r++)
            {
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                h = (h << 5) | (h >> 27);
                h += (uint)r * 0x9E3779B9u;
            }

            output[i] = h;
        });

        return output;
    }

    public void Generated() => Device.For(Length, new HashRounds(GpuInput, GpuOutput, Rounds));

    public void HandWritten() => Device.For(Length, new HashRoundsHandWritten(GpuInput, GpuOutput, Rounds));

    public void Dispose()
    {
        GpuInput.Dispose();
        GpuOutput.Dispose();
    }
}
