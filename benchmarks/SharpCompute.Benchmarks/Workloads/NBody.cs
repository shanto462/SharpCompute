using System;
using System.Numerics;
using System.Threading.Tasks;
using SharpCompute.Benchmarks.Shaders;

namespace SharpCompute.Benchmarks.Workloads;

/// <summary>
/// One O(n²) N-body step with softening (see <see cref="NBodyStep"/>). Reads the current state and
/// writes the next one, so every run computes the same thing.
/// </summary>
public sealed class NBody : IDisposable
{
    public const float Dt = 0.01f;
    public const float SofteningSquared = 0.01f;

    private readonly float[] _px, _py, _pz, _m;

    public NBody(int count, GraphicsDevice device)
    {
        Count = count;
        Random random = new(11);
        Positions = new Float4[count];
        Velocities = new Float4[count];

        // Bodies in the cube [−1, 1]³ with a total mass of about 1 (G = 1), so a step moves them a little.
        for (int i = 0; i < count; i++)
        {
            Positions[i] = new Float4((random.NextSingle() * 2f) - 1f, (random.NextSingle() * 2f) - 1f, (random.NextSingle() * 2f) - 1f, (0.5f + random.NextSingle()) / count);
            Velocities[i] = new Float4((random.NextSingle() - 0.5f) * 0.1f, (random.NextSingle() - 0.5f) * 0.1f, (random.NextSingle() - 0.5f) * 0.1f, 0f);
        }

        // Structure-of-arrays copy for the SIMD variant (built once here, not timed).
        _px = new float[count];
        _py = new float[count];
        _pz = new float[count];
        _m = new float[count];

        for (int i = 0; i < count; i++)
        {
            (_px[i], _py[i], _pz[i], _m[i]) = (Positions[i].X, Positions[i].Y, Positions[i].Z, Positions[i].W);
        }

        NextPositions = new Float4[count];
        NextVelocities = new Float4[count];

        Device = device;
        GpuPositions = device.AllocateReadOnlyBuffer<Float4>(Positions);
        GpuVelocities = device.AllocateReadOnlyBuffer<Float4>(Velocities);
        GpuNextPositions = device.AllocateReadWriteBuffer<Float4>(count);
        GpuNextVelocities = device.AllocateReadWriteBuffer<Float4>(count);
    }

    public int Count { get; }

    public Float4[] Positions { get; }

    public Float4[] Velocities { get; }

    public Float4[] NextPositions { get; }

    public Float4[] NextVelocities { get; }

    public GraphicsDevice Device { get; }

    public ReadOnlyBuffer<Float4> GpuPositions { get; }

    public ReadOnlyBuffer<Float4> GpuVelocities { get; }

    public ReadWriteBuffer<Float4> GpuNextPositions { get; }

    public ReadWriteBuffer<Float4> GpuNextVelocities { get; }

    /// <summary>Parallel.For over bodies, scalar inner loop over the Float4 array (same order as the GPU).</summary>
    public float CpuParallel()
    {
        Parallel.For(0, Count, i =>
        {
            Float4[] pos = Positions;
            Float4 self = pos[i];
            float ax = 0f, ay = 0f, az = 0f;

            for (int j = 0; j < pos.Length; j++)
            {
                Float4 other = pos[j];
                float dx = other.X - self.X, dy = other.Y - self.Y, dz = other.Z - self.Z;
                float distSquared = (dx * dx) + (dy * dy) + (dz * dz) + SofteningSquared;
                float inv = 1f / MathF.Sqrt(distSquared);
                float s = other.W * inv * inv * inv;
                ax += dx * s;
                ay += dy * s;
                az += dz * s;
            }

            Integrate(i, self, ax, ay, az);
        });

        return NextPositions[Count / 2].X;
    }

    /// <summary>Parallel.For over bodies, inner loop SIMD over a structure-of-arrays copy (<see cref="Vector{T}"/>).</summary>
    public float CpuParallelSimd()
    {
        Parallel.For(0, Count, i =>
        {
            Float4 self = Positions[i];
            int width = Vector<float>.Count;
            Vector<float> sx = new(self.X), sy = new(self.Y), sz = new(self.Z), soft = new(SofteningSquared);
            Vector<float> ax = Vector<float>.Zero, ay = Vector<float>.Zero, az = Vector<float>.Zero;
            ReadOnlySpan<float> px = _px, py = _py, pz = _pz, m = _m;
            int j = 0;

            for (; j <= px.Length - width; j += width)
            {
                Vector<float> dx = new Vector<float>(px[j..]) - sx;
                Vector<float> dy = new Vector<float>(py[j..]) - sy;
                Vector<float> dz = new Vector<float>(pz[j..]) - sz;
                Vector<float> distSquared = (dx * dx) + (dy * dy) + (dz * dz) + soft;
                Vector<float> inv = Vector<float>.One / Vector.SquareRoot(distSquared);
                Vector<float> s = new Vector<float>(m[j..]) * inv * inv * inv;
                ax += dx * s;
                ay += dy * s;
                az += dz * s;
            }

            float fx = Vector.Sum(ax), fy = Vector.Sum(ay), fz = Vector.Sum(az);

            for (; j < px.Length; j++)
            {
                float dx = px[j] - self.X, dy = py[j] - self.Y, dz = pz[j] - self.Z;
                float inv = 1f / MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz) + SofteningSquared);
                float s = m[j] * inv * inv * inv;
                fx += dx * s;
                fy += dy * s;
                fz += dz * s;
            }

            Integrate(i, self, fx, fy, fz);
        });

        return NextPositions[Count / 2].X;
    }

    public float Gpu()
    {
        Device.For(Count, new NBodyStep(GpuPositions, GpuVelocities, GpuNextPositions, GpuNextVelocities, Count, Dt, SofteningSquared));
        return GpuNextPositions[Count / 2].X;
    }

    /// <summary>The same step with <see cref="NBodyStepHandWritten"/>, the kernel written by hand in MSL.</summary>
    public void GpuHandWritten() =>
        Device.For(Count, new NBodyStepHandWritten(GpuPositions, GpuVelocities, GpuNextPositions, GpuNextVelocities, Count, Dt, SofteningSquared));

    public void Dispose()
    {
        GpuPositions.Dispose();
        GpuVelocities.Dispose();
        GpuNextPositions.Dispose();
        GpuNextVelocities.Dispose();
    }

    private void Integrate(int i, Float4 self, float ax, float ay, float az)
    {
        Float4 v = Velocities[i];
        float vx = v.X + (ax * Dt), vy = v.Y + (ay * Dt), vz = v.Z + (az * Dt);
        NextVelocities[i] = new Float4(vx, vy, vz, v.W);
        NextPositions[i] = new Float4(self.X + (vx * Dt), self.Y + (vy * Dt), self.Z + (vz * Dt), self.W);
    }
}
