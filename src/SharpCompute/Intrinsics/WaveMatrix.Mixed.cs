using System.Runtime.CompilerServices;

namespace SharpCompute;

// The mixed-precision overloads of Multiply and MultiplyAccumulate. Metal's simdgroup_multiply and
// simdgroup_multiply_accumulate take every combination of half and float operands and results, so
// each operand here may be either type, and the type the method is called on is the precision of
// the result. On the CPU every matrix is held as floats (a half matrix as floats that are exactly
// halves), so the overloads differ only in the type they wrap the result in.

public readonly partial struct WaveMatrix8x8
{
    /// <inheritdoc cref="WaveMatrix8x8.Multiply(WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrix8x8 Multiply(WaveMatrix8x8 a, WaveMatrixHalf8x8 b, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiply, SimdScalar.Float, a.Elements, b.Elements, null, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.Multiply(WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrix8x8 Multiply(WaveMatrixHalf8x8 a, WaveMatrix8x8 b, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiply, SimdScalar.Float, a.Elements, b.Elements, null, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.Multiply(WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrix8x8 Multiply(WaveMatrixHalf8x8 a, WaveMatrixHalf8x8 b, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiply, SimdScalar.Float, a.Elements, b.Elements, null, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrix8x8 MultiplyAccumulate(
        WaveMatrix8x8 a, WaveMatrix8x8 b, WaveMatrixHalf8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Float, a.Elements, b.Elements, c.Elements, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrix8x8 MultiplyAccumulate(
        WaveMatrix8x8 a, WaveMatrixHalf8x8 b, WaveMatrix8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Float, a.Elements, b.Elements, c.Elements, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrix8x8 MultiplyAccumulate(
        WaveMatrix8x8 a, WaveMatrixHalf8x8 b, WaveMatrixHalf8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Float, a.Elements, b.Elements, c.Elements, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrix8x8 MultiplyAccumulate(
        WaveMatrixHalf8x8 a, WaveMatrix8x8 b, WaveMatrix8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Float, a.Elements, b.Elements, c.Elements, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrix8x8 MultiplyAccumulate(
        WaveMatrixHalf8x8 a, WaveMatrix8x8 b, WaveMatrixHalf8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Float, a.Elements, b.Elements, c.Elements, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrix8x8 MultiplyAccumulate(
        WaveMatrixHalf8x8 a, WaveMatrixHalf8x8 b, WaveMatrix8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Float, a.Elements, b.Elements, c.Elements, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrix8x8 MultiplyAccumulate(
        WaveMatrixHalf8x8 a, WaveMatrixHalf8x8 b, WaveMatrixHalf8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Float, a.Elements, b.Elements, c.Elements, callerFile, callerLine));
}

public readonly partial struct WaveMatrixHalf8x8
{
    /// <inheritdoc cref="WaveMatrix8x8.Multiply(WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrixHalf8x8 Multiply(WaveMatrix8x8 a, WaveMatrix8x8 b, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiply, SimdScalar.Half, a.Elements, b.Elements, null, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.Multiply(WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrixHalf8x8 Multiply(WaveMatrix8x8 a, WaveMatrixHalf8x8 b, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiply, SimdScalar.Half, a.Elements, b.Elements, null, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.Multiply(WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrixHalf8x8 Multiply(WaveMatrixHalf8x8 a, WaveMatrix8x8 b, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiply, SimdScalar.Half, a.Elements, b.Elements, null, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrixHalf8x8 MultiplyAccumulate(
        WaveMatrix8x8 a, WaveMatrix8x8 b, WaveMatrix8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Half, a.Elements, b.Elements, c.Elements, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrixHalf8x8 MultiplyAccumulate(
        WaveMatrix8x8 a, WaveMatrix8x8 b, WaveMatrixHalf8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Half, a.Elements, b.Elements, c.Elements, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrixHalf8x8 MultiplyAccumulate(
        WaveMatrix8x8 a, WaveMatrixHalf8x8 b, WaveMatrix8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Half, a.Elements, b.Elements, c.Elements, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrixHalf8x8 MultiplyAccumulate(
        WaveMatrix8x8 a, WaveMatrixHalf8x8 b, WaveMatrixHalf8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Half, a.Elements, b.Elements, c.Elements, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrixHalf8x8 MultiplyAccumulate(
        WaveMatrixHalf8x8 a, WaveMatrix8x8 b, WaveMatrix8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Half, a.Elements, b.Elements, c.Elements, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrixHalf8x8 MultiplyAccumulate(
        WaveMatrixHalf8x8 a, WaveMatrix8x8 b, WaveMatrixHalf8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Half, a.Elements, b.Elements, c.Elements, callerFile, callerLine));

    /// <inheritdoc cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>
    public static WaveMatrixHalf8x8 MultiplyAccumulate(
        WaveMatrixHalf8x8 a, WaveMatrixHalf8x8 b, WaveMatrix8x8 c, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        new(CpuWaveMatrix.Multiply(SimdOp.MatrixMultiplyAccumulate, SimdScalar.Half, a.Elements, b.Elements, c.Elements, callerFile, callerLine));
}
