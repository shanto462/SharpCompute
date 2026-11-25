using System;

namespace SharpCompute;

/// <summary>
/// What every lane of a SIMD group brings to a matrix operation. Unlike the value of a reduction it has
/// to be the <i>same</i> in every lane: a wave matrix is one value of the whole group.
/// </summary>
/// <param name="A">The left operand's elements; for a load the source and for a store the destination (a buffer or an array).</param>
/// <param name="B">The right operand's elements; for a store the matrix that is stored.</param>
/// <param name="C">The elements of the matrix a product is added to.</param>
/// <param name="Offset">The index of the first element of a load or store.</param>
/// <param name="Stride">The elements per row of a load or store.</param>
/// <param name="Transpose">Whether a load or store transposes.</param>
/// <param name="ValueBits">The bits of the value of a fill.</param>
internal readonly record struct WaveMatrixArguments(object? A, object? B, object? C, int Offset, int Stride, bool Transpose, uint ValueBits)
{
    /// <summary>
    /// True when <paramref name="other"/> is what this lane brought too. Matrices and buffers are
    /// compared by identity: the lanes of a group hold the very array a meeting of theirs made, so two
    /// lanes with equal elements in different arrays got them along different paths.
    /// </summary>
    internal bool Same(in WaveMatrixArguments other) =>
        ReferenceEquals(A, other.A) && ReferenceEquals(B, other.B) && ReferenceEquals(C, other.C) &&
        Offset == other.Offset && Stride == other.Stride && Transpose == other.Transpose && ValueBits == other.ValueBits;
}

/// <summary>
/// The CPU side of <see cref="WaveMatrix8x8"/> and <see cref="WaveMatrixHalf8x8"/>: the public members
/// funnel into the methods below, which take the lane to the meeting of its SIMD group
/// (<see cref="CpuSimdGroups"/>); when all 32 lanes are there, <see cref="Compute"/> carries the
/// operation out once for the group.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model.</b> On the GPU a matrix is spread over the lanes, two elements each (lane <c>L</c>
/// holds columns <c>4 * ((L / 8) % 2) + 2 * (L % 2)</c> and the next of row
/// <c>4 * (L / 16) + (L % 8) / 2</c>; measured on Apple M4 Pro). Here it is one array of 64 floats in
/// row-major order that the lanes share and nobody writes after the meeting that made it; null stands
/// for the zero matrix. A half matrix is held the same way, with every element exactly a half.
/// </para>
/// <para>
/// <b>What is refused.</b> The GPU gives a matrix operation a meaning only when the whole group makes
/// it alike. A group with fewer than 32 lanes loads and stores just the elements its lanes hold, and a
/// product needs the elements the missing lanes would have held; lanes with different arguments each
/// act on their own two elements. Neither is something a shader should rely on, so rather than
/// reproduce it the meeting throws <see cref="NotSupportedException"/>.
/// </para>
/// <para>
/// <b>Arithmetic.</b> An element of a product is computed in <see cref="float"/>, the terms added in
/// order after the matrix it accumulates into, and rounded to half at the end for a half result. The
/// GPU does not say in which order or precision it adds, so the two agree within rounding, not bit
/// for bit.
/// </para>
/// </remarks>
internal static class CpuWaveMatrix
{
    /// <summary>Rows and columns of a wave matrix: 8, the one size Metal's matrix units have.</summary>
    internal const int Size = 8;

    private const int Elements = Size * Size;

    internal static float[]? Filled(SimdOp op, SimdScalar scalar, float value, string callerFile, int callerLine) =>
        CpuSimdGroup.MeetMatrix(
            new SimdCall(op, scalar, 1, callerFile, callerLine),
            new WaveMatrixArguments(null, null, null, 0, 0, false, BitConverter.SingleToUInt32Bits(value)));

    internal static float[]? Load(SimdScalar scalar, object source, int offset, int stride, bool transpose, string callerFile, int callerLine)
    {
        // Checked before the meeting, so that a null is reported as the argument it is.
        _ = CpuThread.Current;
        ArgumentNullException.ThrowIfNull(source);

        return CpuSimdGroup.MeetMatrix(
            new SimdCall(SimdOp.MatrixLoad, scalar, 1, callerFile, callerLine),
            new WaveMatrixArguments(source, null, null, offset, stride, transpose, 0));
    }

    internal static void Store(SimdScalar scalar, float[]? matrix, object destination, int offset, int stride, bool transpose, string callerFile, int callerLine)
    {
        _ = CpuThread.Current;
        ArgumentNullException.ThrowIfNull(destination);

        CpuSimdGroup.MeetMatrix(
            new SimdCall(SimdOp.MatrixStore, scalar, 1, callerFile, callerLine),
            new WaveMatrixArguments(destination, matrix, null, offset, stride, transpose, 0));
    }

    internal static float[]? Multiply(SimdOp op, SimdScalar scalar, float[]? a, float[]? b, float[]? c, string callerFile, int callerLine) =>
        CpuSimdGroup.MeetMatrix(new SimdCall(op, scalar, 1, callerFile, callerLine), new WaveMatrixArguments(a, b, c, 0, 0, false, 0));

    /// <summary>
    /// Carries out the operation the <paramref name="lanes"/> lanes of a SIMD group met at. Called once
    /// per meeting, under the lock of the threadgroup's meetings, by whichever lane completed it.
    /// </summary>
    /// <returns>The matrix the operation made; null for a store, and for a matrix that is all zero.</returns>
    /// <exception cref="NotSupportedException">Fewer than 32 lanes took part.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A load or store reaches outside its buffer or array.</exception>
    internal static float[]? Compute(in SimdCall call, in WaveMatrixArguments arguments, int lanes)
    {
        if (lanes != CpuSimdGroup.Width)
        {
            throw new NotSupportedException(
                $"{call}: a wave matrix operation needs all {CpuSimdGroup.Width} lanes of a SIMD group, but {lanes} took part. Each lane holds two " +
                "elements of the matrix, so on the GPU a group that is not full (a threadgroup or a grid that is not a multiple of 32 threads, or lanes " +
                "that returned from Execute earlier) loads and stores only part of it, and multiplies with elements nobody holds. Use a [ThreadGroupSize] " +
                "that is a multiple of 32, dispatch whole threadgroups, and let a SIMD group return early only as a whole.");
        }

        bool isHalf = call.Scalar == SimdScalar.Half;

        switch (call.Op)
        {
            case SimdOp.MatrixFilled:
            {
                float[] matrix = new float[Elements];
                Array.Fill(matrix, BitConverter.UInt32BitsToSingle(arguments.ValueBits));
                return matrix;
            }

            case SimdOp.MatrixDiagonal:
            {
                float[] matrix = new float[Elements];
                float value = BitConverter.UInt32BitsToSingle(arguments.ValueBits);

                for (int i = 0; i < Size; i++)
                {
                    matrix[(i * Size) + i] = value;
                }

                return matrix;
            }

            case SimdOp.MatrixLoad:
                return Load(in call, in arguments);

            case SimdOp.MatrixStore:
                Store(in call, in arguments);
                return null;

            default:
                return Multiply((float[]?)arguments.A, (float[]?)arguments.B, (float[]?)arguments.C, isHalf);
        }
    }

    private static float[] Load(in SimdCall call, in WaveMatrixArguments arguments)
    {
        float[] matrix = new float[Elements];

        switch (arguments.A)
        {
            case Buffer<float> buffer:
                Read(buffer.AsReadOnlySpan(), matrix, in call, in arguments);
                GC.KeepAlive(buffer);
                break;
            case float[] array:
                Read(array, matrix, in call, in arguments);
                break;
            case Buffer<Half> buffer:
                Read(buffer.AsReadOnlySpan(), matrix, in call, in arguments);
                GC.KeepAlive(buffer);
                break;
            default:
                Read((Half[])arguments.A!, matrix, in call, in arguments);
                break;
        }

        return matrix;
    }

    private static void Store(in SimdCall call, in WaveMatrixArguments arguments)
    {
        ReadOnlySpan<float> matrix = (float[]?)arguments.B ?? s_zero;

        switch (arguments.A)
        {
            case Buffer<float> buffer:
                Write(matrix, buffer.AsSpan(), in call, in arguments);
                GC.KeepAlive(buffer);
                break;
            case float[] array:
                Write(matrix, array, in call, in arguments);
                break;
            case Buffer<Half> buffer:
                Write(matrix, buffer.AsSpan(), in call, in arguments);
                GC.KeepAlive(buffer);
                break;
            default:
                Write(matrix, (Half[])arguments.A!, in call, in arguments);
                break;
        }
    }

    private static readonly float[] s_zero = new float[Elements];

    private static void Read(ReadOnlySpan<float> source, Span<float> matrix, in SimdCall call, in WaveMatrixArguments arguments)
    {
        CheckRange(source.Length, in call, in arguments);

        for (int row = 0; row < Size; row++)
        {
            for (int column = 0; column < Size; column++)
            {
                matrix[(row * Size) + column] = source[Index(in arguments, row, column)];
            }
        }
    }

    private static void Read(ReadOnlySpan<Half> source, Span<float> matrix, in SimdCall call, in WaveMatrixArguments arguments)
    {
        CheckRange(source.Length, in call, in arguments);

        for (int row = 0; row < Size; row++)
        {
            for (int column = 0; column < Size; column++)
            {
                matrix[(row * Size) + column] = (float)source[Index(in arguments, row, column)];
            }
        }
    }

    private static void Write(ReadOnlySpan<float> matrix, Span<float> destination, in SimdCall call, in WaveMatrixArguments arguments)
    {
        CheckRange(destination.Length, in call, in arguments);

        for (int row = 0; row < Size; row++)
        {
            for (int column = 0; column < Size; column++)
            {
                destination[Index(in arguments, row, column)] = matrix[(row * Size) + column];
            }
        }
    }

    private static void Write(ReadOnlySpan<float> matrix, Span<Half> destination, in SimdCall call, in WaveMatrixArguments arguments)
    {
        CheckRange(destination.Length, in call, in arguments);

        for (int row = 0; row < Size; row++)
        {
            for (int column = 0; column < Size; column++)
            {
                // Exact: the elements of a half matrix are halves already.
                destination[Index(in arguments, row, column)] = (Half)matrix[(row * Size) + column];
            }
        }
    }

    /// <summary>Where element (<paramref name="row"/>, <paramref name="column"/>) of a matrix is in the memory a load or store names.</summary>
    private static int Index(in WaveMatrixArguments arguments, int row, int column) =>
        arguments.Transpose
            ? arguments.Offset + (column * arguments.Stride) + row
            : arguments.Offset + (row * arguments.Stride) + column;

    /// <summary>
    /// Throws unless all 64 elements lie in memory of <paramref name="length"/> elements. The last one
    /// is 7 rows and 7 columns after the first, transposed or not.
    /// </summary>
    private static void CheckRange(int length, in SimdCall call, in WaveMatrixArguments arguments)
    {
        long last = arguments.Offset + ((long)(Size - 1) * arguments.Stride) + (Size - 1);

        if (arguments.Stride < 0)
        {
            throw new ArgumentOutOfRangeException(
                "stride", arguments.Stride, $"{call}: the stride (the number of elements from one row of the matrix to the next) must not be negative.");
        }

        if (arguments.Offset < 0 || last >= length)
        {
            throw new ArgumentOutOfRangeException(
                "offset",
                arguments.Offset,
                $"{call}: an 8 x 8 matrix at offset {arguments.Offset} with a stride of {arguments.Stride} covers the elements {arguments.Offset} to {last}, " +
                $"which do not all lie in the {length} elements there are. On the GPU this reads or writes out of bounds, silently.");
        }
    }

    private static float[] Multiply(float[]? a, float[]? b, float[]? c, bool roundToHalf)
    {
        ReadOnlySpan<float> left = a ?? s_zero, right = b ?? s_zero, sum = c ?? s_zero;
        float[] result = new float[Elements];

        for (int row = 0; row < Size; row++)
        {
            for (int column = 0; column < Size; column++)
            {
                float element = sum[(row * Size) + column];

                for (int k = 0; k < Size; k++)
                {
                    element += left[(row * Size) + k] * right[(k * Size) + column];
                }

                result[(row * Size) + column] = roundToHalf ? (float)(Half)element : element;
            }
        }

        return result;
    }
}
