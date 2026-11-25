using System;
using System.Linq;

namespace SharpCompute.Tests.EvaluationOrder;

/// <summary>
/// Expressions with side effects in buffers, <c>[GroupShared]</c> memory and textures that SCG0021
/// accepts, because the GPU evaluates them as C# does. One thread runs each, so every cell has a
/// single exact value. The expressions whose order differs (<c>data[0] += Bump(0)</c> and the like)
/// are build errors, tested in the generator tests.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(1)]
public readonly partial struct OrderedResourceEffects(ReadWriteBuffer<int> data, ReadWriteBuffer<int> result, ReadWriteBuffer<int> counter, ReadWriteTexture2D<Float4> image)
    : IComputeShader
{
    [GroupShared]
    private static readonly int[] tile = new int[4];

    private int Bump(int slot)
    {
        data[slot] += 1;
        return 10;
    }

    private int BumpResult(int slot)
    {
        result[slot] += 1;
        return 10;
    }

    private static int BumpTile(int slot)
    {
        tile[slot] += 1;
        return 10;
    }

    private int Paint(int x)
    {
        image[x, 0] = new Float4(50f, 0f, 0f, 0f);
        return 10;
    }

    private static int Inc(ReadWriteBuffer<int> buffer, int slot)
    {
        buffer[slot] += 1;
        return 10;
    }

    private static int Pair(int a, int b) => (a * 1000) + b;

    public void Execute()
    {
        // One operand reads what the other (a call) writes: left to right on both sides.
        result[0] = data[16] + Bump(16);
        result[1] = Bump(17) + data[17];
        result[2] = Pair(data[18], Bump(18));
        result[3] = Pair(Bump(19), data[19]);
        result[4] = data[20] + Inc(data, 20) + data[20];

        // The store comes last in both languages.
        data[0] = Bump(0);
        data[1] = data[2] = 5;
        data[data.Length - 1] = Bump(47);

        // Reads and writes of the target itself.
        data[24] = data[24] + 1;
        data[25] += 3;
        data[26]++;

        // Compound assignments on different resources.
        result[5] += data[27];
        result[6] += Bump(28);
        data[29] += BumpResult(7);
        result[data[30]] = Inc(counter, 1);

        // Atomics whose location the rest of the expression does not use.
        result[Gpu.AtomicAdd(ref counter[0], 1) + 12] = 77;
        data[31] = Gpu.AtomicAdd(ref counter[0], 5) + data[32];

        // [GroupShared] memory.
        tile[0] = data[33];
        tile[1] = 0;
        result[10] = tile[0] + BumpTile(0) + tile[0];
        tile[1] += tile[0];
        result[11] = tile[1];

        // A texture read next to a helper that writes the pixel.
        result[14] = (int)image[0, 0].X + Paint(0);
        result[15] = (int)image[0, 0].X;
    }
}

public class EvaluationOrderTests
{
    private static int[] Initial(int length)
    {
        int[] values = Enumerable.Range(0, length).Select(static i => 100 + i).ToArray();
        values[30] = 8;   // an index into result
        return values;
    }

    [MetalFact]
    public void AcceptedSideEffectsInResources_GiveTheSameResultsOnTheGpuAndTheCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        var data = new int[2][];
        var result = new int[2][];
        var counter = new int[2][];
        var pixels = new Float4[2][];

        for (int pass = 0; pass < 2; pass++)
        {
            using ReadWriteBuffer<int> d = device.AllocateReadWriteBuffer(Initial(48));
            using ReadWriteBuffer<int> r = device.AllocateReadWriteBuffer<int>(16);
            using ReadWriteBuffer<int> c = device.AllocateReadWriteBuffer(new[] { 0, 40 });
            using ReadWriteTexture2D<Float4> t = device.AllocateReadWriteTexture2D(new[] { new Float4(7f, 0f, 0f, 0f), new Float4(9f, 0f, 0f, 0f) }, 2, 1);
            var shader = new OrderedResourceEffects(d, r, c, t);

            if (pass == 0)
            {
                device.For(1, shader);
            }
            else
            {
                device.ForCpu(1, shader);
            }

            data[pass] = d.ToArray();
            result[pass] = r.ToArray();
            counter[pass] = c.ToArray();
            pixels[pass] = t.ToArray();
        }

        Assert.Equal(data[1], data[0]);
        Assert.Equal(result[1], result[0]);
        Assert.Equal(counter[1], counter[0]);
        Assert.Equal(pixels[1], pixels[0]);

        // And both are what C# says, worked out by hand.
        int[] expectedResult = new int[16];
        expectedResult[0] = 116 + 10;                // data[16] read before the call
        expectedResult[1] = 10 + 118;                // data[17] read after it
        expectedResult[2] = Pair(118, 10);
        expectedResult[3] = Pair(10, 120);
        expectedResult[4] = 120 + 10 + 121;
        expectedResult[5] = 127;
        expectedResult[6] = 10;
        expectedResult[7] = 1;
        expectedResult[8] = 10;                      // result[data[30]], data[30] = 8
        expectedResult[10] = 133 + 10 + 134;
        expectedResult[11] = 134;
        expectedResult[12] = 77;                     // the first atomic returned 0
        expectedResult[14] = 7 + 10;
        expectedResult[15] = 50;
        Assert.Equal(expectedResult, result[1]);

        int[] expectedData = Initial(48);
        expectedData[0] = 10;
        expectedData[1] = 5;
        expectedData[2] = 5;
        expectedData[16]++;
        expectedData[17]++;
        expectedData[18]++;
        expectedData[19]++;
        expectedData[20]++;
        expectedData[24]++;
        expectedData[25] += 3;
        expectedData[26]++;
        expectedData[28]++;
        expectedData[29] += 10;
        expectedData[31] = 1 + 132;                  // the second atomic returned 1
        expectedData[47] = 10;                       // Bump(47) wrote 148, then the store
        Assert.Equal(expectedData, data[1]);
        Assert.Equal(new[] { 6, 41 }, counter[1]);
    }

    private static int Pair(int a, int b) => (a * 1000) + b;
}
