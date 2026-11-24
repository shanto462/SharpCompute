using System;

namespace SharpCompute.Tests.Viewer;

/// <summary>Smoke and accuracy tests for the Viewer's N-body step.</summary>
public class NBodyTests
{
    private const float TimeStep = 0.0025f;

    [ViewerFact]
    public void Step_With16kBodies_StaysFiniteAndConservesBodiesMassAndMomentum()
    {
        const int count = 16_384;

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using IDisposable disposable = ViewerAssembly.Create("Demos.NBodySimulation", device, count, 7);
        dynamic simulation = disposable;

        Float4[] initialPositions = Positions(simulation);
        Float4[] initialVelocities = Velocities(simulation);
        Assert.Equal(count, (int)simulation.Count);

        for (int i = 0; i < 20; i++)
        {
            simulation.Step(TimeStep);
        }

        Float4[] positions = Positions(simulation);
        Float4[] velocities = Velocities(simulation);

        Assert.Equal(count, positions.Length);
        Assert.Equal(count, velocities.Length);

        double scale = 0;

        for (int i = 0; i < count; i++)
        {
            Assert.True(float.IsFinite(positions[i].X) && float.IsFinite(positions[i].Y) && float.IsFinite(positions[i].Z), $"Body {i} position is not finite.");
            Assert.True(float.IsFinite(velocities[i].X) && float.IsFinite(velocities[i].Y) && float.IsFinite(velocities[i].Z), $"Body {i} velocity is not finite.");

            // Masses ride along unchanged in w, in the same slots.
            Assert.Equal(initialPositions[i].W, positions[i].W);

            // The bodies actually moved.
            Assert.NotEqual(initialPositions[i].X, positions[i].X);

            scale += positions[i].W * Math.Sqrt(Dot(velocities[i], velocities[i]));
        }

        // Pairwise forces are equal and opposite, so total momentum only drifts by float rounding.
        (double px0, double py0, double pz0) = Momentum(initialPositions, initialVelocities);
        (double px, double py, double pz) = Momentum(positions, velocities);
        double drift = Math.Sqrt(((px - px0) * (px - px0)) + ((py - py0) * (py - py0)) + ((pz - pz0) * (pz - pz0)));

        Assert.True(drift < 1e-4 * scale, $"Momentum drifted by {drift} (scale {scale}).");
    }

    [ViewerFact]
    public void Step_MatchesADoublePrecisionReference()
    {
        const int count = 512;
        const double softening2 = 0.025 * 0.025;

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using IDisposable disposable = ViewerAssembly.Create("Demos.NBodySimulation", device, count, 3);
        dynamic simulation = disposable;

        Float4[] positions = Positions(simulation);
        Float4[] velocities = Velocities(simulation);

        simulation.Step(TimeStep);

        Float4[] gpuPositions = Positions(simulation);
        Float4[] gpuVelocities = Velocities(simulation);

        for (int i = 0; i < count; i++)
        {
            double ax = 0, ay = 0, az = 0;

            for (int j = 0; j < count; j++)
            {
                double dx = positions[j].X - (double)positions[i].X;
                double dy = positions[j].Y - (double)positions[i].Y;
                double dz = positions[j].Z - (double)positions[i].Z;
                double inverse = 1 / Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz) + softening2);
                double f = positions[j].W * inverse * inverse * inverse;
                (ax, ay, az) = (ax + (dx * f), ay + (dy * f), az + (dz * f));
            }

            double vx = velocities[i].X + (ax * TimeStep);
            double vy = velocities[i].Y + (ay * TimeStep);
            double vz = velocities[i].Z + (az * TimeStep);

            double speed = Math.Sqrt((vx * vx) + (vy * vy) + (vz * vz));
            double tolerance = 1e-4 * Math.Max(speed, 1e-3);

            Assert.True(Math.Abs(gpuVelocities[i].X - vx) <= tolerance, $"Body {i}: vx {gpuVelocities[i].X} vs {vx}");
            Assert.True(Math.Abs(gpuVelocities[i].Y - vy) <= tolerance, $"Body {i}: vy {gpuVelocities[i].Y} vs {vy}");
            Assert.True(Math.Abs(gpuVelocities[i].Z - vz) <= tolerance, $"Body {i}: vz {gpuVelocities[i].Z} vs {vz}");

            Assert.Equal(positions[i].X + (vx * TimeStep), gpuPositions[i].X, 1e-5);
            Assert.Equal(positions[i].Y + (vy * TimeStep), gpuPositions[i].Y, 1e-5);
            Assert.Equal(positions[i].Z + (vz * TimeStep), gpuPositions[i].Z, 1e-5);
        }
    }

    private static Float4[] Positions(dynamic simulation) => ((ReadWriteBuffer<Float4>)simulation.Positions).ToArray();

    private static Float4[] Velocities(dynamic simulation) => ((ReadWriteBuffer<Float4>)simulation.Velocities).ToArray();

    private static double Dot(Float4 a, Float4 b) => ((double)a.X * b.X) + ((double)a.Y * b.Y) + ((double)a.Z * b.Z);

    private static (double X, double Y, double Z) Momentum(Float4[] positions, Float4[] velocities)
    {
        double x = 0, y = 0, z = 0;

        for (int i = 0; i < positions.Length; i++)
        {
            double m = positions[i].W;
            (x, y, z) = (x + (m * velocities[i].X), y + (m * velocities[i].Y), z + (m * velocities[i].Z));
        }

        return (x, y, z);
    }
}
