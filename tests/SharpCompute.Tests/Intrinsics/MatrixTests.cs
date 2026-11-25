using System;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpCompute.Tests.Intrinsics;

/// <summary>
/// <c>Float4x4</c> across the CPU/GPU boundary (a constant, buffer elements, a struct field at a
/// 4-byte offset) against <c>System.Numerics.Matrix4x4</c>, which shares its convention: row-major
/// storage and row vectors, so <c>Gpu.Mul(v, m)</c> is <c>Vector4.Transform(v, m)</c> and
/// <c>Gpu.Mul(a, b)</c> is <c>a * b</c>, with no transpose anywhere on the C# side.
/// </summary>
public class MatrixTests
{
    private const int Count = 257;

    private static Matrix4x4 ToNumerics(Float4x4 m) => Unsafe.As<Float4x4, Matrix4x4>(ref m);

    private static Float4x4 FromNumerics(Matrix4x4 m) => Unsafe.As<Matrix4x4, Float4x4>(ref m);

    private static float[] Floats(Float4x4 m) => MemoryMarshal.Cast<Float4x4, float>(new ReadOnlySpan<Float4x4>(in m)).ToArray();

    private static float[] Floats(Matrix4x4 m) => Floats(FromNumerics(m));

    private static float[] Floats(Vector4 v) => [v.X, v.Y, v.Z, v.W];

    private static float[] Floats(Float4 v) => [v.X, v.Y, v.Z, v.W];

    /// <summary>Elements that are small multiples of 1/8: sums of products of them are exact in a float, whatever the order.</summary>
    private static Float4x4 ExactMatrix(Random random) =>
        MemoryMarshal.Cast<float, Float4x4>(Enumerable.Range(0, 16).Select(_ => random.Next(-24, 25) / 8f).ToArray())[0];

    private static Float4x4 AnyMatrix(Random random) =>
        MemoryMarshal.Cast<float, Float4x4>(Enumerable.Range(0, 16).Select(_ => (random.NextSingle() * 4f) - 2f).ToArray())[0];

    private static void AssertClose(float[] expected, float[] actual, float tolerance, string what)
    {
        Assert.Equal(expected.Length, actual.Length);

        for (int i = 0; i < expected.Length; i++)
        {
            float scale = MathF.Max(1f, MathF.Max(MathF.Abs(expected[i]), MathF.Abs(actual[i])));
            Assert.True(MathF.Abs(expected[i] - actual[i]) <= tolerance * scale, $"{what}, element {i}: expected {expected[i]:R}, got {actual[i]:R}");
        }
    }

    [MetalTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Float4x4_AtEveryBoundary_MatchesMatrix4x4(bool exactInputs)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Random random = new(exactInputs ? 1 : 2);
        Func<Random, Float4x4> next = exactInputs ? ExactMatrix : AnyMatrix;

        Float4x4[] left = Enumerable.Range(0, Count).Select(_ => next(random)).ToArray();
        Float4x4[] right = Enumerable.Range(0, Count).Select(_ => next(random)).ToArray();
        Float4[] vectors = Enumerable.Range(0, Count).Select(_ => new Float4(random.Next(-8, 9) / 4f, random.Next(-8, 9) / 4f, random.Next(-8, 9) / 4f, 1f)).ToArray();
        Posed[] posed = Enumerable.Range(0, Count).Select(i => new Posed { Weight = 1f + (i % 3), Transform = next(random), Id = i }).ToArray();
        Float4x4 view = next(random);
        const float scale = 0.5f;

        // Row 0 is the identity and row 1 a translation, to make the convention visible below.
        left[0] = new Float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
        left[1] = new Float4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 10, 20, 30, 1);
        vectors[1] = new Float4(1, 2, 3, 1);

        using ReadOnlyBuffer<Float4x4> leftBuffer = device.AllocateReadOnlyBuffer<Float4x4>(left);
        using ReadOnlyBuffer<Float4> vectorBuffer = device.AllocateReadOnlyBuffer<Float4>(vectors);

        (Float4x4[] Products, Float4[] Transformed, float[] Determinants, Float4x4[] Right, Posed[] Posed) Dispatch(bool onCpu)
        {
            using ReadWriteBuffer<Float4x4> rightBuffer = device.AllocateReadWriteBuffer<Float4x4>(right);
            using ReadWriteBuffer<Float4x4> products = device.AllocateReadWriteBuffer<Float4x4>(Count * 4);
            using ReadWriteBuffer<Float4> transformed = device.AllocateReadWriteBuffer<Float4>(Count * 3);
            using ReadWriteBuffer<float> determinants = device.AllocateReadWriteBuffer<float>(Count * 3);
            using ReadWriteBuffer<Posed> posedBuffer = device.AllocateReadWriteBuffer<Posed>(posed);
            var shader = new MatrixMath(leftBuffer, rightBuffer, vectorBuffer, products, transformed, determinants, posedBuffer, scale, view);

            if (onCpu)
            {
                device.ForCpu(Count, shader);
            }
            else
            {
                device.For(Count, shader);
            }

            return (products.ToArray(), transformed.ToArray(), determinants.ToArray(), rightBuffer.ToArray(), posedBuffer.ToArray());
        }

        var gpu = Dispatch(onCpu: false);
        var cpu = Dispatch(onCpu: true);

        // With exact inputs every product and sum is exact, so the GPU, the CPU and System.Numerics
        // must agree to the bit; with arbitrary inputs they differ by how the sums are fused.
        float tolerance = exactInputs ? 0f : 2e-5f;
        Matrix4x4 viewN = ToNumerics(view);

        for (int t = 0; t < Count; t++)
        {
            Matrix4x4 a = ToNumerics(left[t]), b = ToNumerics(right[t]);
            Vector4 v = new(vectors[t].X, vectors[t].Y, vectors[t].Z, vectors[t].W);

            foreach ((string name, var result) in new[] { ("GPU", gpu), ("CPU", cpu) })
            {
                string what = $"{name}, thread {t}";

                AssertClose(Floats(a * b), Floats(result.Products[(t * 4) + 0]), tolerance, what + ": Mul(a, b)");
                AssertClose(Floats(a * viewN * b), Floats(result.Products[(t * 4) + 1]), tolerance * 4, what + ": Mul(Mul(a, view), b)");
                Assert.Equal(Floats(Matrix4x4.Transpose(a)), Floats(result.Products[(t * 4) + 2]));
                AssertClose(Floats((a + b - viewN) * scale * 0.25f), Floats(result.Products[(t * 4) + 3]), tolerance, what + ": operators");

                AssertClose(Floats(Vector4.Transform(v, a)), Floats(result.Transformed[(t * 3) + 0]), tolerance, what + ": Mul(v, a)");
                AssertClose(Floats(Vector4.Transform(v, Matrix4x4.Transpose(a))), Floats(result.Transformed[(t * 3) + 1]), tolerance, what + ": Mul(a, v)");
                AssertClose(Floats(Vector4.Transform(Vector4.Transform(v, viewN), Matrix4x4.Transpose(b))), Floats(result.Transformed[(t * 3) + 2]), tolerance * 4, what + ": Mul(Mul(v, view), transpose(b))");

                // Determinants: sums of 24 products of four elements; a few ULP of the largest term.
                float detTolerance = exactInputs ? 0f : 2e-4f;
                AssertClose([a.GetDeterminant()], [result.Determinants[(t * 3) + 0]], detTolerance, what + ": Determinant(a)");
                AssertClose([b.GetDeterminant()], [result.Determinants[(t * 3) + 1]], detTolerance, what + ": Determinant(transpose(b))");
                AssertClose([viewN.GetDeterminant() * posed[t].Weight], [result.Determinants[(t * 3) + 2]], detTolerance * posed[t].Weight, what + ": Determinant(view)");

                // The stores into packed storage: a buffer element and a struct field at offset 4.
                Assert.Equal(Floats(b * 0.5f), Floats(result.Right[t]));
                AssertClose(Floats((ToNumerics(posed[t].Transform) * a) + viewN), Floats(result.Posed[t].Transform), tolerance, what + ": struct field");
                Assert.Equal(posed[t].Weight, result.Posed[t].Weight);
                Assert.Equal(posed[t].Id + 1, result.Posed[t].Id);
            }
        }

        // The convention, spelled out: a translation lives in the last row and moves a row vector.
        Assert.Equal([11f, 22f, 33f, 1f], Floats(gpu.Transformed[(1 * 3) + 0]));
        Assert.Equal([1f, 2f, 3f, 141f], Floats(gpu.Transformed[(1 * 3) + 1])); // M * v leaves xyz alone and sums into w instead
        Assert.Equal(Floats(vectors[0]), Floats(gpu.Transformed[0]));

        if (exactInputs)
        {
            // Exact inputs: the GPU and the CPU agree bit for bit on everything.
            Assert.Equal(cpu.Products.SelectMany(Floats), gpu.Products.SelectMany(Floats));
            Assert.Equal(cpu.Transformed.SelectMany(Floats), gpu.Transformed.SelectMany(Floats));
            Assert.Equal(cpu.Determinants, gpu.Determinants);
        }
        else
        {
            // Arbitrary inputs: Mul is an fma chain in the same order on both, so still bit for bit.
            Assert.Equal(
                Enumerable.Range(0, Count).SelectMany(t => Floats(cpu.Products[t * 4])),
                Enumerable.Range(0, Count).SelectMany(t => Floats(gpu.Products[t * 4])));
            Assert.Equal(
                Enumerable.Range(0, Count).SelectMany(t => Floats(cpu.Transformed[t * 3]).Concat(Floats(cpu.Transformed[(t * 3) + 1]))),
                Enumerable.Range(0, Count).SelectMany(t => Floats(gpu.Transformed[t * 3]).Concat(Floats(gpu.Transformed[(t * 3) + 1]))));
        }
    }

    [MetalFact]
    public void TransposeAndDeterminant_OfTheSmallMatrices_MatchTheCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Random random = new(9);
        const int count = 512;

        // The first half exact (multiples of 1/4), the second half arbitrary.
        float[] elements = Enumerable.Range(0, count * 9).Select(i => i < count * 9 / 2 ? random.Next(-12, 13) / 4f : (random.NextSingle() * 4f) - 2f).ToArray();
        using ReadOnlyBuffer<float> input = device.AllocateReadOnlyBuffer<float>(elements);
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(count * SmallMatrixMath.Stride);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(count * SmallMatrixMath.Stride);

        device.For(count, new SmallMatrixMath(input, gpu));
        device.ForCpu(count, new SmallMatrixMath(input, cpu));

        float[] g = gpu.ToArray(), c = cpu.ToArray();

        for (int t = 0; t < count; t++)
        {
            int o = t * SmallMatrixMath.Stride;
            ReadOnlySpan<float> e = elements.AsSpan(t * 9, 9);
            bool exact = t < count / 2;
            float tolerance = exact ? 0f : 1e-4f;

            float det2 = (e[0] * e[3]) - (e[1] * e[2]);
            float det3 = (e[0] * ((e[4] * e[8]) - (e[5] * e[7]))) - (e[1] * ((e[3] * e[8]) - (e[5] * e[6]))) + (e[2] * ((e[3] * e[7]) - (e[4] * e[6])));

            foreach ((string name, float[] r) in new[] { ("GPU", g), ("CPU", c) })
            {
                AssertClose([det2, det3, det2, det3], r[o..(o + 4)], tolerance, $"{name}, thread {t}: determinants");

                // Mul(M, v) and Mul(v, transpose(M)) are the same sums.
                AssertClose(r[(o + 4)..(o + 6)], r[(o + 6)..(o + 8)], 0f, $"{name}, thread {t}: 2x2 transpose");
                AssertClose(r[(o + 8)..(o + 11)], r[(o + 11)..(o + 14)], 0f, $"{name}, thread {t}: 3x3 transpose");
                AssertClose([det2 * det2, det3 * det3], r[(o + 14)..(o + 16)], exact ? 0f : 1e-3f, $"{name}, thread {t}: det of a product");
            }

            // The products are fma chains in the same order on both sides.
            Assert.Equal(c[(o + 4)..(o + 14)], g[(o + 4)..(o + 14)]);
        }
    }

    [MetalFact]
    public void Mul_AcceptsPackedOperandsThatAreNotDirectBufferReads()
    {
        // A conditional between two buffer elements, a helper's result and a swizzle all have the
        // packed type in MSL, which the matrix operators reject unless the operand is converted.
        // This used to fail when the pipeline was compiled, at the first dispatch.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Float3[] data = [new Float3(1, 1, 1), new Float3(1, 2, 3), new Float3(4, 5, 6), new Float3(7, 8, 9)];
        Float3 offset = new(10, 20, 30);
        using ReadWriteBuffer<Float3> points = device.AllocateReadWriteBuffer<Float3>(data);
        using ReadWriteBuffer<Float3> gpu = device.AllocateReadWriteBuffer<Float3>(data.Length * 4);
        using ReadWriteBuffer<Float3> cpu = device.AllocateReadWriteBuffer<Float3>(data.Length * 4);

        device.For(data.Length, new MulPackedOperands(points, gpu, offset));
        device.ForCpu(data.Length, new MulPackedOperands(points, cpu, offset));

        // M = rows (2, 0, 0), (0, 3, 0), (1, 0, 4). M * v = (2x, 3y, x + 4z); v * M = (2x + z, 3y, 4z).
        static Float3 MatrixTimes(Float3 v) => new((2 * v.X), 3 * v.Y, v.X + (4 * v.Z));
        static Float3 TimesMatrix(Float3 v) => new((2 * v.X) + v.Z, 3 * v.Y, 4 * v.Z);

        Float3[] results = gpu.ToArray();

        for (int i = 0; i < data.Length; i++)
        {
            Float3 chosen = i > 1 ? data[i] : data[0];
            Float3 chosenOrOffset = i > 1 ? data[i] : offset;

            Assert.Equal(Floats(MatrixTimes(chosen)), Floats(results[(i * 4) + 0]));
            Assert.Equal(Floats(TimesMatrix(chosenOrOffset)), Floats(results[(i * 4) + 1]));
            Assert.Equal(Floats(MatrixTimes(data[i]) + TimesMatrix(data[0])), Floats(results[(i * 4) + 2]));

            // R = rows (0, 1), (-1, 0): R * (x, y) = (y, -x); (z, x) * R = (-x, z).
            Assert.Equal([data[i].Y - data[i].X, -data[i].X + data[i].Z, 0f], Floats(results[(i * 4) + 3]));
        }

        Assert.Equal(cpu.ToArray().SelectMany(Floats), results.SelectMany(Floats));
    }

    private static float[] Floats(Float3 v) => [v.X, v.Y, v.Z];

    [Fact]
    public void Float4x4_OnTheCpu_HasTheLayoutTheShaderAsserts()
    {
        Assert.Equal(64, Unsafe.SizeOf<Float4x4>());
        Assert.Equal(72, Unsafe.SizeOf<Posed>());
        Assert.Equal(4, (int)Marshal.OffsetOf<Posed>(nameof(Posed.Transform)));
        Assert.Equal(68, (int)Marshal.OffsetOf<Posed>(nameof(Posed.Id)));
    }
}
