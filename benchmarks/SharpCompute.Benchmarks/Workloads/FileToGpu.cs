using System;
using System.IO;
using System.Runtime.InteropServices;
using SharpCompute.Benchmarks.Shaders;

namespace SharpCompute.Benchmarks.Workloads;

/// <summary>
/// Sums a file of floats on the GPU, starting from the path: open, get the data into a GPU buffer,
/// one reduction dispatch, add up the partial sums on the CPU. The file is in the OS page cache
/// (it is written in setup and read on every iteration), so this measures copies, not the disk.
/// <list type="bullet">
/// <item><b>Two copies</b>: <c>File.ReadAllBytes</c> (page cache → managed array) then
/// <c>AllocateReadOnlyBuffer(data)</c> (array → shared buffer).</item>
/// <item><b>One copy</b>: allocate the buffer and <c>RandomAccess.Read</c> straight into its <c>AsSpan()</c>.</item>
/// <item><b>No copy</b>: <c>MapReadOnlyBuffer</c>: the GPU reads the page-cache pages themselves.</item>
/// </list>
/// Every variant creates and disposes its buffer per call, as loading a file once would.
/// </summary>
public sealed class FileToGpu : IDisposable
{
    /// <summary>Reduction threads; each sums <c>Length / Threads</c> strided floats.</summary>
    public const int Threads = 1 << 16;

    public FileToGpu(int length, GraphicsDevice device)
    {
        if (length % Threads != 0)
        {
            throw new ArgumentException($"The length must be a multiple of {Threads}.", nameof(length));
        }

        Length = length;
        Device = device;
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sharpcompute-bench-{Environment.ProcessId}-{length}.f32");

        float[] data = new float[length];
        Random random = new(5);

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = random.NextSingle() - 0.25f;
        }

        File.WriteAllBytes(Path, MemoryMarshal.AsBytes(data.AsSpan()));
        Partials = device.AllocateReadWriteBuffer<float>(Threads);

        double total = 0;

        foreach (float value in data)
        {
            total += value;
        }

        ExactSum = total;
    }

    public int Length { get; }

    public GraphicsDevice Device { get; }

    public string Path { get; }

    /// <summary>The sum in double precision, for --verify.</summary>
    public double ExactSum { get; }

    private ReadWriteBuffer<float> Partials { get; }

    public double TwoCopies()
    {
        byte[] bytes = File.ReadAllBytes(Path);
        using ReadOnlyBuffer<float> data = Device.AllocateReadOnlyBuffer<float>(MemoryMarshal.Cast<byte, float>(bytes));
        return Sum(data);
    }

    public double OneCopy()
    {
        using ReadWriteBuffer<float> data = Device.AllocateReadWriteBuffer<float>(Length);
        using (Microsoft.Win32.SafeHandles.SafeFileHandle file = File.OpenHandle(Path))
        {
            Span<byte> destination = MemoryMarshal.AsBytes(data.AsSpan());
            long offset = 0;

            while (offset < destination.Length)
            {
                int read = RandomAccess.Read(file, destination[(int)offset..], offset);

                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                offset += read;
            }
        }

        return Sum(data);
    }

    public double NoCopy()
    {
        using ReadOnlyBuffer<float> data = Device.MapReadOnlyBuffer<float>(Path);
        return Sum(data);
    }

    public void Dispose()
    {
        Partials.Dispose();
        File.Delete(Path);
    }

    private double Sum(ReadOnlyBuffer<float> data)
    {
        Device.For(Threads, new StridedPartialSums(data, Partials, Length / Threads));
        return AddPartials();
    }

    private double Sum(ReadWriteBuffer<float> data)
    {
        Device.For(Threads, new StridedPartialSumsReadWrite(data, Partials, Length / Threads));
        return AddPartials();
    }

    private double AddPartials()
    {
        double total = 0;

        foreach (float partial in Partials.AsReadOnlySpan())
        {
            total += partial;
        }

        return total;
    }
}
