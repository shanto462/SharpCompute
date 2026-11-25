using System;
using System.Runtime.CompilerServices;

namespace SharpCompute;

// The matrix units of the GPU: 8 x 8 matrices that the lanes of a SIMD group hold and multiply
// together. The two types are the same operations for two element types; the mixed-precision
// overloads of Multiply and MultiplyAccumulate are in WaveMatrix.Mixed.cs.

/// <summary>
/// An 8 × 8 matrix of <see cref="float"/> that the 32 lanes of a SIMD group hold <i>together</i>, so
/// that the GPU's matrix units can multiply it: <see cref="MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
/// computes <c>a × b + c</c> for whole matrices in one step, several times faster than the same
/// arithmetic written per thread. It is Metal's <c>simdgroup_float8x8</c>; the same shape exists
/// elsewhere as a cooperative matrix (Vulkan's <c>KHR_cooperative_matrix</c>, CUDA's WMMA fragments),
/// which is why nothing here is named after one vendor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Operations and what they become in MSL.</b>
/// </para>
/// <list type="table">
/// <item><term><c>Filled(value)</c></term><description><c>make_filled_simdgroup_matrix&lt;float, 8, 8&gt;(value)</c>: every element.</description></item>
/// <item><term><c>Diagonal(value)</c></term><description><c>simdgroup_float8x8(value)</c>: the diagonal, zero elsewhere.</description></item>
/// <item><term><c>Load(source, offset, stride, transpose)</c></term><description><c>simdgroup_load</c> from a buffer (<c>device</c> memory) or a <c>[GroupShared]</c> array (<c>threadgroup</c> memory).</description></item>
/// <item><term><c>m.Store(destination, offset, stride, transpose)</c></term><description><c>simdgroup_store</c>.</description></item>
/// <item><term><c>Multiply(a, b)</c></term><description><c>simdgroup_multiply</c>: <c>a × b</c>.</description></item>
/// <item><term><c>MultiplyAccumulate(a, b, c)</c></term><description><c>simdgroup_multiply_accumulate</c>: <c>a × b + c</c>.</description></item>
/// </list>
/// <para>
/// <b>Where the elements are.</b> Element <c>(row, column)</c> of a matrix loaded at
/// <c>offset</c> with <c>stride</c> elements per row is <c>source[offset + row * stride + column]</c>;
/// with <c>transpose</c> it is <c>source[offset + column * stride + row]</c>, which reads a
/// column-major matrix, or the transpose of a row-major one. <c>Store</c> writes the same elements
/// back. The 64 elements must lie inside the buffer or array: nothing checks that on the GPU, and
/// the CPU throws <see cref="ArgumentOutOfRangeException"/>. A matrix that does not fit (the edge of
/// a size that is not a multiple of 8) is the caller's to handle: pad the data, as the ComputeLab
/// "Matrix units" page does, or finish the edge with scalar code.
/// </para>
/// <para>
/// <b>It is a collective value.</b> Each lane holds two of the 64 elements (measured on Apple M4 Pro),
/// and an operation is carried out by the lanes together. So every operation has to be reached by
/// <i>all 32 lanes</i> of the SIMD group, with the <i>same arguments</i>: the same matrices, the same
/// buffer, offset and stride, the same fill value. Compute those from what the whole SIMD group
/// shares (<see cref="SimdGroupIds.Index"/>, the threadgroup's position, captured constants, loop
/// counters), never from <see cref="ThreadIds"/> or <see cref="Gpu.WaveGetLaneIndex"/>. On the GPU
/// nothing checks this; a group with fewer than 32 lanes (a threadgroup or grid that is not a
/// multiple of 32, or lanes that returned early) loads and stores only the elements its lanes hold,
/// and lanes that disagree each go their own way. Give the shader a <see cref="ThreadGroupSizeAttribute"/>
/// that is a multiple of 32 and dispatch whole threadgroups.
/// </para>
/// <para>
/// <b>It has no layout in memory.</b> A matrix can be a local, a parameter and a return value of a
/// helper; it cannot be the element of a buffer or of a <c>[GroupShared]</c> array, a field of a
/// struct or of the shader, or a <c>static readonly</c> field (SCG0048, SCG0050). <c>default</c> is
/// the zero matrix.
/// </para>
/// <para>
/// <b>Speed.</b> Measured on Apple M4 Pro, a matrix product written with these operations reaches the
/// rate of the hardware (about 5 TFLOP/s, against 1 TFLOP/s for the best scalar kernel), but only in
/// the right shape. Keep at most <i>eight</i> accumulators live per SIMD group (2 × 4 tiles): sixteen
/// spill out of the registers, and the kernel runs seven times slower, at 0.7 TFLOP/s. Load operands
/// <i>directly from the buffers</i>: staging them through <c>[GroupShared]</c> memory first made that
/// kernel slower still, 0.4 TFLOP/s. And write an offset as a plain sum, <c>row * n + k</c>: the
/// generator adds the terms to the buffer's address one at a time, which the GPU compiler turns into
/// far better addressing than one 32-bit sum (5.1 against 4.4 TFLOP/s). The price of that is the one
/// place where signed arithmetic is not C#'s on the GPU: each term wraps as usual, but their sum is
/// formed in 64 bits and does not, where the terms add up to more than an <see cref="int"/> holds.
/// An offset into a buffer never does.
/// </para>
/// <para>
/// <b>On the CPU</b> (<c>GraphicsDevice.ForCpu</c>, <c>ForSplit</c>) the lanes of a SIMD group meet at
/// every operation, as they do for <see cref="Gpu.WaveActiveSum(float, string, int)"/>, and the matrix
/// is storage the group shares. The meeting checks what the GPU does not, and throws
/// <see cref="NotSupportedException"/> when the lanes are at different calls, when they bring
/// different arguments, or when fewer than 32 take part. The results agree with the GPU's within
/// rounding: the order in which a product's eight terms are added is unspecified on the GPU, so
/// compare with a tolerance. The trailing <c>callerFile</c> and <c>callerLine</c> parameters are
/// filled in by the compiler and identify the call for that meeting; leave them out.
/// </para>
/// <para>
/// Outside a shader every member throws <see cref="InvalidOperationException"/>, like the other
/// shader built-ins. Thread-safe in the sense that matters for a shader: a matrix is immutable, and
/// its operations are the meetings of the lanes.
/// </para>
/// </remarks>
public readonly partial struct WaveMatrix8x8
{
    /// <summary>The number of rows.</summary>
    public const int Rows = CpuWaveMatrix.Size;

    /// <summary>The number of columns.</summary>
    public const int Columns = CpuWaveMatrix.Size;

    // The 64 elements in row-major order, shared by the lanes of the SIMD group that made the matrix
    // and never written again; null is the zero matrix, which is what `default` has to be.
    private readonly float[]? _elements;

    internal WaveMatrix8x8(float[]? elements) => _elements = elements;

    internal float[]? Elements => _elements;

    /// <summary>A matrix with every element equal to <paramref name="value"/> (<c>make_filled_simdgroup_matrix</c>).</summary>
    /// <param name="value">The value of the 64 elements; the same in every lane.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    public static WaveMatrix8x8 Filled(float value, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Filled(SimdOp.MatrixFilled, SimdScalar.Float, value, callerFile, callerLine));

    /// <summary>
    /// A matrix with <paramref name="value"/> on the diagonal and zero elsewhere (the constructor
    /// <c>simdgroup_float8x8(value)</c>): <c>Diagonal(1f)</c> is the identity.
    /// </summary>
    /// <param name="value">The value of the eight diagonal elements; the same in every lane.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    public static WaveMatrix8x8 Diagonal(float value, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Filled(SimdOp.MatrixDiagonal, SimdScalar.Float, value, callerFile, callerLine));

    /// <summary>
    /// Loads 64 elements of a buffer (<c>simdgroup_load</c> from <c>device</c> memory): element
    /// <c>(row, column)</c> is <c>source[offset + row * stride + column]</c>, or
    /// <c>source[offset + column * stride + row]</c> with <paramref name="transpose"/>.
    /// </summary>
    /// <param name="source">A captured buffer, or a view of one.</param>
    /// <param name="offset">
    /// The index of the matrix's first element. Write it as a sum of terms: on the GPU they are added to
    /// the buffer's address one by one, without wrapping (see the remarks of <see cref="WaveMatrix8x8"/>).
    /// </param>
    /// <param name="stride">The number of elements from one row to the next in <paramref name="source"/> (its width when it holds a row-major matrix). Not negative.</param>
    /// <param name="transpose">True to load the transpose: the rows of the matrix are then columns of <paramref name="source"/>.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    /// <exception cref="ArgumentOutOfRangeException">On the CPU: an element lies outside <paramref name="source"/>, or <paramref name="stride"/> is negative.</exception>
    /// <exception cref="NotSupportedException">On the CPU: the lanes of the SIMD group do not make this call together, with the same arguments, all 32 of them.</exception>
    public static WaveMatrix8x8 Load(
        ReadOnlyBuffer<float> source, int offset, int stride, bool transpose = false, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Load(SimdScalar.Float, source, offset, stride, transpose, callerFile, callerLine));

    /// <inheritdoc cref="Load(ReadOnlyBuffer{float}, int, int, bool, string, int)"/>
    public static WaveMatrix8x8 Load(
        ReadWriteBuffer<float> source, int offset, int stride, bool transpose = false, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Load(SimdScalar.Float, source, offset, stride, transpose, callerFile, callerLine));

    /// <summary>
    /// Loads 64 elements of a <see cref="GroupSharedAttribute"/> array (<c>simdgroup_load</c> from
    /// <c>threadgroup</c> memory), laid out as in <see cref="Load(ReadOnlyBuffer{float}, int, int, bool, string, int)"/>.
    /// The elements must have been written by the threadgroup, with a barrier between the writes and
    /// this call.
    /// </summary>
    /// <param name="source">A <c>[GroupShared]</c> array of the shader.</param>
    /// <param name="offset">The index of the matrix's first element.</param>
    /// <param name="stride">The number of elements from one row to the next in <paramref name="source"/>. Not negative.</param>
    /// <param name="transpose">True to load the transpose.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    /// <exception cref="ArgumentOutOfRangeException">On the CPU: an element lies outside <paramref name="source"/>, or <paramref name="stride"/> is negative.</exception>
    /// <exception cref="NotSupportedException">On the CPU: the lanes of the SIMD group do not make this call together, with the same arguments, all 32 of them.</exception>
    public static WaveMatrix8x8 Load(
        float[] source, int offset, int stride, bool transpose = false, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Load(SimdScalar.Float, source, offset, stride, transpose, callerFile, callerLine));

    /// <summary>
    /// Writes the 64 elements to a buffer (<c>simdgroup_store</c> to <c>device</c> memory): element
    /// <c>(row, column)</c> goes to <c>destination[offset + row * stride + column]</c>, or to
    /// <c>destination[offset + column * stride + row]</c> with <paramref name="transpose"/>.
    /// </summary>
    /// <param name="destination">A captured buffer, or a view of one.</param>
    /// <param name="offset">The index the matrix's first element is written to. Write it as a sum of terms (see the remarks of <see cref="WaveMatrix8x8"/>).</param>
    /// <param name="stride">The number of elements from one row to the next in <paramref name="destination"/>. Not negative; below 8 the rows overlap and the result is unspecified.</param>
    /// <param name="transpose">True to store the transpose.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    /// <exception cref="ArgumentOutOfRangeException">On the CPU: an element lies outside <paramref name="destination"/>, or <paramref name="stride"/> is negative.</exception>
    /// <exception cref="NotSupportedException">On the CPU: the lanes of the SIMD group do not make this call together, with the same arguments, all 32 of them.</exception>
    public void Store(
        ReadWriteBuffer<float> destination, int offset, int stride, bool transpose = false, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        CpuWaveMatrix.Store(SimdScalar.Float, _elements, destination, offset, stride, transpose, callerFile, callerLine);

    /// <summary>
    /// Writes the 64 elements to a <see cref="GroupSharedAttribute"/> array (<c>simdgroup_store</c> to
    /// <c>threadgroup</c> memory), laid out as in <see cref="Store(ReadWriteBuffer{float}, int, int, bool, string, int)"/>.
    /// Other threads of the threadgroup see them after a barrier.
    /// </summary>
    /// <param name="destination">A <c>[GroupShared]</c> array of the shader.</param>
    /// <param name="offset">The index the matrix's first element is written to.</param>
    /// <param name="stride">The number of elements from one row to the next in <paramref name="destination"/>. Not negative.</param>
    /// <param name="transpose">True to store the transpose.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    /// <exception cref="ArgumentOutOfRangeException">On the CPU: an element lies outside <paramref name="destination"/>, or <paramref name="stride"/> is negative.</exception>
    /// <exception cref="NotSupportedException">On the CPU: the lanes of the SIMD group do not make this call together, with the same arguments, all 32 of them.</exception>
    public void Store(
        float[] destination, int offset, int stride, bool transpose = false, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        CpuWaveMatrix.Store(SimdScalar.Float, _elements, destination, offset, stride, transpose, callerFile, callerLine);

    /// <summary>
    /// The matrix product <c>a × b</c> (<c>simdgroup_multiply</c>). The operands may each be a
    /// <see cref="WaveMatrix8x8"/> or a <see cref="WaveMatrixHalf8x8"/>; the result has the precision
    /// of the type this is called on.
    /// </summary>
    /// <param name="a">The left operand.</param>
    /// <param name="b">The right operand.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    /// <exception cref="NotSupportedException">On the CPU: the lanes of the SIMD group do not make this call together, with the same matrices, all 32 of them.</exception>
    public static WaveMatrix8x8 Multiply(WaveMatrix8x8 a, WaveMatrix8x8 b, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiply, SimdScalar.Float, a.Elements, b.Elements, null, callerFile, callerLine));

    /// <summary>
    /// <c>a × b + c</c> (<c>simdgroup_multiply_accumulate</c>), the operation the matrix units exist
    /// for: <c>sum = MultiplyAccumulate(a, b, sum)</c> in a loop over the shared dimension is a matrix
    /// product. The operands may each be a <see cref="WaveMatrix8x8"/> or a
    /// <see cref="WaveMatrixHalf8x8"/> (half operands with a float accumulator, for instance); the
    /// result has the precision of the type this is called on.
    /// </summary>
    /// <param name="a">The left operand of the product.</param>
    /// <param name="b">The right operand of the product.</param>
    /// <param name="c">The matrix the product is added to.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    /// <exception cref="NotSupportedException">On the CPU: the lanes of the SIMD group do not make this call together, with the same matrices, all 32 of them.</exception>
    public static WaveMatrix8x8 MultiplyAccumulate(
        WaveMatrix8x8 a, WaveMatrix8x8 b, WaveMatrix8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Float, a.Elements, b.Elements, c.Elements, callerFile, callerLine));
}

/// <summary>
/// An 8 × 8 matrix of <see cref="Half"/> that the 32 lanes of a SIMD group hold together: Metal's
/// <c>simdgroup_half8x8</c>. Everything said of <see cref="WaveMatrix8x8"/> holds for it, with
/// <see cref="Half"/> elements in buffers and <c>[GroupShared]</c> arrays; the two can be mixed in
/// <c>Multiply</c> and <c>MultiplyAccumulate</c>.
/// </summary>
/// <remarks>
/// A half has 11 bits of precision and overflows to infinity above 65,504, which a sum of products
/// reaches quickly. The usual arrangement is half <i>operands</i> (half the memory traffic of float)
/// with a float accumulator: <c>WaveMatrix8x8.MultiplyAccumulate(halfA, halfB, floatSum)</c>.
/// On the CPU a product is computed in <see cref="float"/> and rounded to half once at the end; the
/// GPU may round at every step, so results that are not exactly representable differ by more than
/// they do for float.
/// </remarks>
public readonly partial struct WaveMatrixHalf8x8
{
    /// <summary>The number of rows.</summary>
    public const int Rows = CpuWaveMatrix.Size;

    /// <summary>The number of columns.</summary>
    public const int Columns = CpuWaveMatrix.Size;

    // The elements as floats, each of them exactly a half: one representation serves both types on
    // the CPU, and the mixed-precision products need no conversion.
    private readonly float[]? _elements;

    internal WaveMatrixHalf8x8(float[]? elements) => _elements = elements;

    internal float[]? Elements => _elements;

    /// <inheritdoc cref="WaveMatrix8x8.Filled(float, string, int)"/>
    public static WaveMatrixHalf8x8 Filled(Half value, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Filled(SimdOp.MatrixFilled, SimdScalar.Half, (float)value, callerFile, callerLine));

    /// <summary>
    /// A matrix with <paramref name="value"/> on the diagonal and zero elsewhere (the constructor
    /// <c>simdgroup_half8x8(value)</c>).
    /// </summary>
    /// <param name="value">The value of the eight diagonal elements; the same in every lane.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    public static WaveMatrixHalf8x8 Diagonal(Half value, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Filled(SimdOp.MatrixDiagonal, SimdScalar.Half, (float)value, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.Load(ReadOnlyBuffer{float}, int, int, bool, string, int)"/>
    public static WaveMatrixHalf8x8 Load(
        ReadOnlyBuffer<Half> source, int offset, int stride, bool transpose = false, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Load(SimdScalar.Half, source, offset, stride, transpose, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.Load(ReadOnlyBuffer{float}, int, int, bool, string, int)"/>
    public static WaveMatrixHalf8x8 Load(
        ReadWriteBuffer<Half> source, int offset, int stride, bool transpose = false, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Load(SimdScalar.Half, source, offset, stride, transpose, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.Load(float[], int, int, bool, string, int)"/>
    public static WaveMatrixHalf8x8 Load(
        Half[] source, int offset, int stride, bool transpose = false, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Load(SimdScalar.Half, source, offset, stride, transpose, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.Store(ReadWriteBuffer{float}, int, int, bool, string, int)"/>
    public void Store(
        ReadWriteBuffer<Half> destination, int offset, int stride, bool transpose = false, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        CpuWaveMatrix.Store(SimdScalar.Half, _elements, destination, offset, stride, transpose, callerFile, callerLine);

    /// <inheritdoc cref="WaveMatrix8x8.Store(float[], int, int, bool, string, int)"/>
    public void Store(
        Half[] destination, int offset, int stride, bool transpose = false, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        CpuWaveMatrix.Store(SimdScalar.Half, _elements, destination, offset, stride, transpose, callerFile, callerLine);

    /// <inheritdoc cref="WaveMatrix8x8.Multiply(WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrixHalf8x8 Multiply(WaveMatrixHalf8x8 a, WaveMatrixHalf8x8 b, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiply, SimdScalar.Half, a.Elements, b.Elements, null, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrixHalf8x8 MultiplyAccumulate(
        WaveMatrixHalf8x8 a, WaveMatrixHalf8x8 b, WaveMatrixHalf8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Half, a.Elements, b.Elements, c.Elements, callerFile, callerLine));
}
