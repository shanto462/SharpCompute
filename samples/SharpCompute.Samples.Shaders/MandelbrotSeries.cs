// Original SharpCompute sample (MIT), not a Shadertoy port.
using System;
using System.Numerics;

namespace SharpCompute.Samples.Shaders;

/// <summary>
/// A series approximation for the first iterations of a deep-zoom frame: for every pixel,
/// <c>δ_n ≈ a·u + b·u² + c·u³</c>, where <c>u</c> is the pixel's offset from the view center in
/// pixels (as a complex number). The kernel starts each pixel at iteration <see cref="Skip"/> from
/// that value instead of iterating from 0, which saves most of the work at depth, where every
/// pixel first follows the reference for tens of thousands of iterations.
/// </summary>
/// <param name="Skip">Iterations skipped (0 means no approximation).</param>
/// <param name="A">Coefficient of <c>u</c>.</param>
/// <param name="B">Coefficient of <c>u²</c>.</param>
/// <param name="C">Coefficient of <c>u³</c>.</param>
public readonly record struct MandelbrotSeries(int Skip, Float2 A, Float2 B, Float2 C)
{
    /// <summary>No approximation: start at iteration 0 with <c>δ = 0</c>.</summary>
    public static MandelbrotSeries None => default;

    /// <summary>Default error bound at the skip point, in pixels (see <see cref="Compute"/>).</summary>
    public const double DefaultTolerancePixels = 1e-4;

    private const int ProbeCount = 16;

    /// <summary>
    /// Finds the longest valid skip for a view whose pixels lie within <paramref name="radiusPixels"/>
    /// of the center, spaced <paramref name="pixelScale"/> apart.
    /// </summary>
    /// <remarks>
    /// The coefficients follow <c>a' = 2Z·a + s</c>, <c>b' = 2Z·b + a²</c>, <c>c' = 2Z·c + 2a·b</c>
    /// (pixel-scaled, so they don't overflow at depth). The approximation is checked against
    /// <see cref="ProbeCount"/> probe orbits iterated exactly (double-precision perturbation), spaced
    /// evenly on the circle of radius <paramref name="radiusPixels"/>, which encloses every pixel.
    /// The error is a polynomial in <c>u</c>, so its largest value over the disk is on that circle
    /// (maximum-modulus principle); the probes sample the circle at 16 points rather than bound it, so
    /// this is a check, not a proof, and the tolerance is kept far below a pixel. The skip ends before
    /// any probe's error, measured as an equivalent pixel shift (error / |a|), exceeds
    /// <paramref name="tolerancePixels"/>, before any probe escapes or needs rebasing, and at
    /// <paramref name="maxSkip"/> at most.
    /// </remarks>
    public static MandelbrotSeries Compute(ReadOnlySpan<Float2> orbit, double pixelScale, double radiusPixels, int maxSkip, double tolerancePixels = DefaultTolerancePixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelScale);
        ArgumentOutOfRangeException.ThrowIfNegative(radiusPixels);

        int limit = Math.Min(maxSkip, orbit.Length - 1);

        Span<Complex> probeU = stackalloc Complex[ProbeCount];
        Span<Complex> probeDelta = stackalloc Complex[ProbeCount];

        for (int k = 0; k < ProbeCount; k++)
        {
            probeU[k] = Complex.FromPolarCoordinates(radiusPixels, 2 * Math.PI * (k + 0.5) / ProbeCount);
        }

        Complex a = Complex.Zero, b = Complex.Zero, c = Complex.Zero;
        MandelbrotSeries best = None;

        for (int n = 0; n < limit; n++)
        {
            Complex z = ToComplex(orbit[n]);
            Complex twoZ = 2 * z;

            // Update b and c before a: they use the previous a.
            Complex nextC = (twoZ * c) + (2 * a * b);
            Complex nextB = (twoZ * b) + (a * a);
            a = (twoZ * a) + pixelScale;
            b = nextB;
            c = nextC;

            if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c))
            {
                break;
            }

            Complex zNext = ToComplex(orbit[n + 1]);
            double pixel = Complex.Abs(a);
            bool valid = pixel > 0;

            for (int k = 0; k < ProbeCount && valid; k++)
            {
                Complex u = probeU[k];
                Complex delta = probeDelta[k];
                delta = ((twoZ + delta) * delta) + (u * pixelScale);
                probeDelta[k] = delta;

                Complex full = zNext + delta;
                double fullMagnitude = Complex.Abs(full);
                double approximationError = Complex.Abs((((c * u) + b) * u + a) * u - delta);

                valid = IsFinite(delta)
                    && fullMagnitude <= 2
                    && fullMagnitude >= Complex.Abs(delta)
                    && approximationError <= tolerancePixels * pixel;
            }

            if (!valid)
            {
                break;
            }

            best = new MandelbrotSeries(n + 1, ToFloat2(a), ToFloat2(b), ToFloat2(c));
        }

        // A coefficient that flushed to 0 in float can't be right; don't skip then.
        return best.A.X == 0f && best.A.Y == 0f ? None : best;
    }

    private static Complex ToComplex(Float2 value) => new(value.X, value.Y);

    private static Float2 ToFloat2(Complex value) => new((float)value.Real, (float)value.Imaginary);

    private static bool IsFinite(Complex value) => double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);
}
