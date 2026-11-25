// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using SharpCompute.Presentation;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab.Pages.Simd;

/// <summary>
/// Stream compaction without atomics: of 32,768 particles, the ones inside a moving circle are packed
/// to the front of a second buffer, in their original order. Every kept particle works out its own
/// slot from three prefix sums, so no two threads ever write the same element.
/// </summary>
/// <remarks>
/// <para>
/// The particles are 32 × 32 × 32: 32 "blocks" of 32 SIMD groups of 32 lanes, and every dispatch has a
/// threadgroup of 32 threads, so that a SIMD group is exactly one level of that hierarchy. Pass A
/// takes a vote in each group (<c>WaveActiveBallot</c>, <c>WaveActiveCountBits</c>); pass B scans the
/// 1,024 group counts (<c>WavePrefixSum</c>) and totals each block (<c>WaveActiveSum</c>); pass C is
/// the same kernel over the 32 block totals; pass D adds the two offsets and the lane's own prefix sum.
/// </para>
/// <para>
/// The predicate is integer arithmetic on fixed-point positions, so the GPU and the C# loop of the
/// check agree on every particle, and the compacted buffers can be compared element by element.
/// </para>
/// </remarks>
public sealed class CompactionPage : ILabPage
{
    /// <summary>The lanes of a SIMD group, the SIMD groups of a block, and the number of blocks.</summary>
    public const int Lanes = 32;

    /// <summary>The number of SIMD groups: 32 blocks of 32.</summary>
    public const int Groups = Lanes * Lanes;

    /// <summary>The number of particles.</summary>
    public const int Count = Groups * Lanes;

    /// <summary>Particle coordinates are fixed-point: this many units per side of the field. Small enough for a squared distance to fit an <see cref="int"/>.</summary>
    public const int Units = 1 << 14;

    /// <summary>
    /// The particles <see cref="Check"/> compacts again with <c>ForCpu</c>: the first 16 SIMD groups. On the
    /// CPU the lanes of a SIMD group are threads that meet at every operation, about a quarter of a
    /// millisecond each time, so the check takes a sample rather than all 1,024 groups.
    /// </summary>
    public const int CpuSample = 16 * Lanes;

    private static readonly string[] s_variants = ["small circle", "large circle"];

    private readonly GraphicsDevice _device;
    private readonly ComputeContext _context;
    private readonly ReadOnlyBuffer<Int2> _particles;
    private readonly ReadWriteBuffer<uint> _ballots;
    private readonly ReadWriteBuffer<int> _groupCounts;
    private readonly ReadWriteBuffer<int> _groupOffsets;
    private readonly ReadWriteBuffer<int> _blockTotals;
    private readonly ReadWriteBuffer<int> _blockOffsets;
    private readonly ReadWriteBuffer<int> _kept;
    private readonly ReadWriteBuffer<Int2> _compacted;
    private readonly ReadWriteBuffer<uint> _field;
    private readonly Int2[] _cpuCompacted = new Int2[Count];

    // The buffers of the ForCpu sample: the same four passes over CpuSample particles.
    private readonly ReadWriteBuffer<uint> _sampleBallots;
    private readonly ReadWriteBuffer<int> _sampleCounts;
    private readonly ReadWriteBuffer<int> _sampleOffsets;
    private readonly ReadWriteBuffer<int> _sampleBlockTotals;
    private readonly ReadWriteBuffer<int> _sampleBlockOffsets;
    private readonly ReadWriteBuffer<int> _sampleKept;
    private readonly ReadWriteBuffer<Int2> _sampleCompacted;

    private Int2 _center;
    private int _radius;
    private int _shownGroup;

    public CompactionPage(GraphicsDevice device)
    {
        _device = device;
        _context = device.CreateComputeContext();
        _particles = device.AllocateReadOnlyBuffer<Int2>(Count);
        _ballots = device.AllocateReadWriteBuffer<uint>(Groups);
        _groupCounts = device.AllocateReadWriteBuffer<int>(Groups);
        _groupOffsets = device.AllocateReadWriteBuffer<int>(Groups);
        _blockTotals = device.AllocateReadWriteBuffer<int>(Lanes);
        _blockOffsets = device.AllocateReadWriteBuffer<int>(Lanes);
        _kept = device.AllocateReadWriteBuffer<int>(1);
        _compacted = device.AllocateReadWriteBuffer<Int2>(Count);
        _field = device.AllocateReadWriteBuffer<uint>(DrawCompaction.FieldSize * DrawCompaction.FieldSize);
        _sampleBallots = device.AllocateReadWriteBuffer<uint>(CpuSample / Lanes);
        _sampleCounts = device.AllocateReadWriteBuffer<int>(CpuSample / Lanes);
        _sampleOffsets = device.AllocateReadWriteBuffer<int>(CpuSample / Lanes);
        _sampleBlockTotals = device.AllocateReadWriteBuffer<int>(1);
        _sampleBlockOffsets = device.AllocateReadWriteBuffer<int>(1);
        _sampleKept = device.AllocateReadWriteBuffer<int>(1);
        _sampleCompacted = device.AllocateReadWriteBuffer<Int2>(CpuSample);

        // SIMD group g owns cell (g % 32, g / 32) of a 32 x 32 grid over the field, and its 32
        // particles are scattered inside that cell: the hierarchy of the compaction is visible.
        Span<Int2> particles = _particles.AsSpan();
        const int Cell = Units / Lanes;

        for (int i = 0; i < Count; i++)
        {
            int group = i / Lanes;
            uint hash = Hash((uint)i);
            int x = ((group % Lanes) * Cell) + (int)(hash % Cell);
            int y = ((group / Lanes) * Cell) + (int)((hash >> 12) % Cell);
            particles[i] = new Int2(x, y);
        }
    }

    public string Name => "SIMD compaction";

    public string Feature => "ballots and prefix sums";


    public string Topic => LabTopics.Simd;


    /// <summary>The check's C# reference runs on one core, as its summary says.</summary>

    public string CpuNote => "one core";

    public string Help => "V circle size";

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    /// <summary>How many particles the GPU kept in the last <see cref="Compute"/>: the total of the scans.</summary>
    public int Kept { get; private set; }

    /// <summary>The kept particles, in particle order, in the first <see cref="Kept"/> elements.</summary>
    public ReadWriteBuffer<Int2> Compacted => _compacted;

    public void Compute(LabFrame frame)
    {
        float time = frame.Time;
        _center = new Int2(
            (int)(Units * (0.5f + (0.27f * MathF.Sin(time * 0.43f)))),
            (int)(Units * (0.5f + (0.27f * MathF.Cos(time * 0.31f)))));
        _radius = (int)(Units * (Variant == 0 ? 0.2f : 0.34f));

        // Four small passes in one command buffer; each sees what the one before wrote.
        _context.For(Count, new VoteInGroups(_particles, _ballots, _groupCounts, _center.X, _center.Y, _radius * _radius));
        _context.For(Groups, new ScanCounts(_groupCounts, _groupOffsets, _blockTotals));
        _context.For(Lanes, new ScanCounts(_blockTotals, _blockOffsets, _kept));
        _context.For(Count, new ScatterKept(_particles, _ballots, _groupOffsets, _blockOffsets, _compacted));
        _context.Execute();

        GpuMilliseconds = _context.LastGpuTime.TotalMilliseconds;
        Kept = _kept[0];
    }

    public LabCheck Check()
    {
        ReadOnlySpan<Int2> particles = _particles.AsReadOnlySpan();
        Span<Int2> expected = _cpuCompacted;
        int radiusSquared = _radius * _radius;

        // The compaction, the plain way: one loop, one counter.
        long start = Stopwatch.GetTimestamp();
        int count = 0;

        foreach (Int2 particle in particles)
        {
            if (IsInside(particle, _center, radiusSquared))
            {
                expected[count++] = particle;
            }
        }

        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        // A ballot has one bit per kept lane, so its population count is the group's count.
        ReadOnlySpan<uint> ballots = _ballots.AsReadOnlySpan();
        ReadOnlySpan<int> groupCounts = _groupCounts.AsReadOnlySpan();
        int badBallot = -1;

        for (int group = 0; group < Groups && badBallot < 0; group++)
        {
            if (BitOperations.PopCount(ballots[group]) != groupCounts[group])
            {
                badBallot = group;
            }
        }

        // The GPU's compaction is stable, so it must be the C# loop's, element by element.
        ReadOnlySpan<Int2> compacted = _compacted.AsReadOnlySpan();
        int firstDifference = -1;

        for (int slot = 0; slot < Math.Min(count, Kept) && firstDifference < 0; slot++)
        {
            if (compacted[slot].X != expected[slot].X || compacted[slot].Y != expected[slot].Y)
            {
                firstDifference = slot;
            }
        }

        bool onCpu = CompactOnCpu(expected, radiusSquared);
        bool passed = Kept == count && badBallot < 0 && firstDifference < 0 && onCpu;

        string summary = Kept != count
            ? Invariant($"the GPU kept {Kept:N0} particles, the C# loop {count:N0}")
            : badBallot >= 0
                ? Invariant($"the ballot of SIMD group {badBallot} does not have as many bits as its count")
                : firstDifference >= 0
                    ? Invariant($"kept {Kept:N0} like the C# loop, but slot {firstDifference:N0} holds a different particle")
                    : Invariant($"kept {Kept:N0} of {Count:N0} like a C# loop, in its order (CPU: one core); {(onCpu ? "ForCpu too" : "FORCPU DIFFERS")}");

        return new LabCheck(passed, summary, cpuMs);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        Rasterize();

        int shown = PickGroup(fresh: frame.Deterministic);
        uint ballot = _ballots[shown];
        const float Scale = DrawCompaction.FieldSize / (float)Units;

        _device.For(LabCanvas.Width, LabCanvas.Height, new DrawCompaction(
            canvas.Pixels, _field, _groupCounts, LabCanvas.Width, _center.X * Scale, _center.Y * Scale, _radius * Scale, shown, ballot));

        const int Column = 47;
        int row = LabView.FirstPageRow;

        // The check runs now and then; this count is of this very frame, so the two numbers belong together.
        int cpuKept = 0;

        foreach (Int2 particle in _particles.AsReadOnlySpan())
        {
            cpuKept += IsInside(particle, _center, _radius * _radius) ? 1 : 0;
        }

        canvas.Text(Column, row, Invariant($"KEPT {Kept} OF {Count}   CPU {cpuKept}"), TextColor.White);
        canvas.Text(Column, row + 1, "packed in particle order, without atomics", TextColor.Good);
        canvas.Text(Column, row + 3, "OPS: WAVEACTIVEBALLOT, WAVEACTIVECOUNTBITS, WAVEPREFIXSUM,", TextColor.Accent);
        canvas.Text(Column, row + 4, "     WAVEACTIVESUM, WAVEISFIRSTLANE, WAVEGETLANEINDEX", TextColor.Accent);

        canvas.Text(Column, row + 6, "A  32,768 threads, one per particle:", TextColor.White);
        canvas.Text(Column, row + 7, "   ballot = WaveActiveBallot(keep);", TextColor.Cool);
        canvas.Text(Column, row + 8, "   count  = WaveActiveCountBits(keep);", TextColor.Cool);
        canvas.Text(Column, row + 9, "   first lane: ballots[g] = ballot; counts[g] = count;", TextColor.Cool);

        canvas.Text(Column, row + 11, "B  1,024 threads, one per SIMD group, 32 to a block:", TextColor.White);
        canvas.Text(Column, row + 12, "   groupOffsets[g] = WavePrefixSum(count);", TextColor.Cool);
        canvas.Text(Column, row + 13, "   first lane: blockTotals[g / 32] = WaveActiveSum(count);", TextColor.Cool);

        canvas.Text(Column, row + 15, "C  32 threads, one per block: the kernel of B again", TextColor.White);
        canvas.Text(Column, row + 16, "   blockOffsets[b] = WavePrefixSum(total); sum = kept", TextColor.Cool);

        canvas.Text(Column, row + 18, "D  32,768 threads again:", TextColor.White);
        canvas.Text(Column, row + 19, "   keep = bit WaveGetLaneIndex() of ballots[g];", TextColor.Cool);
        canvas.Text(Column, row + 20, "   slot = blockOffsets[g / 32] + groupOffsets[g]", TextColor.Cool);
        canvas.Text(Column, row + 21, "        + WavePrefixSum(keep ? 1 : 0);", TextColor.Cool);
        canvas.Text(Column, row + 22, "   if (keep) compacted[slot] = particle;", TextColor.Cool);

        canvas.Text(Column, row + 24, "Field: a cell is one SIMD group of 32 particles, a row", TextColor.Dim);
        canvas.Text(Column, row + 25, "of cells one block. Gold: the compacted buffer, drawn", TextColor.Dim);
        canvas.Text(Column, row + 26, "back. A cell is tinted by its group's count.", TextColor.Dim);

        // The strip: the ballot of one SIMD group, lane by lane, and each kept lane's prefix sum.
        int stripRow = (DrawCompaction.StripY - LabCanvas.TextTop) / LabCanvas.CellHeight;
        int stripColumn = DrawCompaction.StripX / LabCanvas.CellWidth;
        int columnsPerLane = DrawCompaction.StripPitch / LabCanvas.CellWidth;
        int groupSlot = _blockOffsets[shown / Lanes] + _groupOffsets[shown];

        canvas.Text(2, stripRow - 2, Invariant($"SIMD group {shown} (the outlined cell):  ballot 0x{ballot:X8}   count {_groupCounts[shown]}"), TextColor.White);
        canvas.Text(1, stripRow - 1, "lane", TextColor.Dim);
        canvas.Text(1, stripRow + 2, "psum", TextColor.Accent);

        int prefix = 0;

        for (int lane = 0; lane < Lanes; lane++)
        {
            bool kept = ((ballot >> lane) & 1u) != 0;
            int column = stripColumn + (lane * columnsPerLane);

            canvas.Text(column, stripRow - 1, Invariant($"{lane,2}"), TextColor.Dim);

            if (kept)
            {
                canvas.Text(column, stripRow + 2, Invariant($"{prefix++,2}"), TextColor.Accent);
            }
        }

        canvas.Text(2, stripRow + 4, "Lit = kept. Under a kept lane: WavePrefixSum(keep ? 1 : 0), the kept lanes before it. Its slot is that", TextColor.Dim);
        canvas.Text(2, stripRow + 5, Invariant($"+ blockOffsets[{shown / Lanes}] + groupOffsets[{shown}] = {_blockOffsets[shown / Lanes]} + {_groupOffsets[shown]} = {groupSlot}, the kept particles before this group."), TextColor.Dim);
    }

    public bool OnKeyDown(Key key)
    {
        if (key != Key.V)
        {
            return false;
        }

        Variant = 1 - Variant;
        return true;
    }

    public void Dispose()
    {
        _context.Dispose();
        _particles.Dispose();
        _ballots.Dispose();
        _groupCounts.Dispose();
        _groupOffsets.Dispose();
        _blockTotals.Dispose();
        _blockOffsets.Dispose();
        _kept.Dispose();
        _compacted.Dispose();
        _field.Dispose();
        _sampleBallots.Dispose();
        _sampleCounts.Dispose();
        _sampleOffsets.Dispose();
        _sampleBlockTotals.Dispose();
        _sampleBlockOffsets.Dispose();
        _sampleKept.Dispose();
        _sampleCompacted.Dispose();
    }

    /// <summary>
    /// The four passes again with <c>ForCpu</c>, over the first <see cref="CpuSample"/> particles, compared
    /// with the start of the C# loop's result (<paramref name="expected"/>): the compaction is in particle
    /// order, so what the first particles keep is the first of what all of them keep.
    /// </summary>
    private bool CompactOnCpu(ReadOnlySpan<Int2> expected, int radiusSquared)
    {
        const int Groups = CpuSample / Lanes;

        _device.ForCpu(CpuSample, new VoteInGroups(_particles, _sampleBallots, _sampleCounts, _center.X, _center.Y, radiusSquared));
        _device.ForCpu(Groups, new ScanCounts(_sampleCounts, _sampleOffsets, _sampleBlockTotals));
        _device.ForCpu(1, new ScanCounts(_sampleBlockTotals, _sampleBlockOffsets, _sampleKept));
        _device.ForCpu(CpuSample, new ScatterKept(_particles, _sampleBallots, _sampleOffsets, _sampleBlockOffsets, _sampleCompacted));

        ReadOnlySpan<Int2> particles = _particles.AsReadOnlySpan()[..CpuSample];
        int count = 0;

        foreach (Int2 particle in particles)
        {
            if (IsInside(particle, _center, radiusSquared))
            {
                count++;
            }
        }

        return _sampleKept[0] == count && _sampleCompacted.AsReadOnlySpan()[..count].SequenceEqual(expected[..count]);
    }

    /// <summary>The predicate, as the kernel computes it: a squared distance in integers.</summary>
    private static bool IsInside(Int2 particle, Int2 center, int radiusSquared)
    {
        int dx = particle.X - center.X;
        int dy = particle.Y - center.Y;
        return (dx * dx) + (dy * dy) < radiusSquared;
    }

    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }

    /// <summary>
    /// Marks every particle's pixel in the field: 1 for a particle, 2 for one that is in the compacted
    /// buffer. A few thousand writes into shared memory, in a fixed order: cheaper and simpler on the
    /// CPU than a kernel whose threads would race for a pixel.
    /// </summary>
    private void Rasterize()
    {
        Span<uint> field = _field.AsSpan();
        field.Clear();

        foreach (Int2 particle in _particles.AsReadOnlySpan())
        {
            field[Pixel(particle)] = 1;
        }

        foreach (Int2 particle in _compacted.AsReadOnlySpan()[..Math.Clamp(Kept, 0, Count)])
        {
            field[Pixel(particle)] = 2;
        }

        static int Pixel(Int2 particle) =>
            ((particle.Y * DrawCompaction.FieldSize / Units) * DrawCompaction.FieldSize) + (particle.X * DrawCompaction.FieldSize / Units);
    }

    /// <summary>
    /// The SIMD group whose ballot the strip shows: one the circle cuts through, so that its ballot is
    /// mixed. In the window the choice is kept for as long as it stays mixed, so that the strip can be
    /// read; a reproducible frame always picks afresh.
    /// </summary>
    private int PickGroup(bool fresh)
    {
        ReadOnlySpan<int> counts = _groupCounts.AsReadOnlySpan();

        if (!fresh && counts[_shownGroup] is >= 6 and <= 26)
        {
            return _shownGroup;
        }

        int best = 0;

        for (int group = 1; group < Groups; group++)
        {
            if (Math.Abs(counts[group] - (Lanes / 2)) < Math.Abs(counts[best] - (Lanes / 2)))
            {
                best = group;
            }
        }

        _shownGroup = best;
        return best;
    }
}

/// <summary>
/// Pass A, one thread per particle: the SIMD group votes. Every lane gets the group's ballot (bit
/// <c>i</c> = lane <c>i</c> keeps its particle) and the number of lanes that do; the first lane stores both.
/// </summary>
/// <remarks>The threadgroup is one SIMD group, so group <c>g</c> is the particles <c>32g .. 32g + 31</c>.</remarks>
[ThreadGroupSize(CompactionPage.Lanes)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct VoteInGroups(
    ReadOnlyBuffer<Int2> particles, ReadWriteBuffer<uint> ballots, ReadWriteBuffer<int> groupCounts, int centerX, int centerY, int radiusSquared) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        int group = i / CompactionPage.Lanes;
        Int2 d = particles[i] - new Int2(centerX, centerY);
        bool keep = (d.X * d.X) + (d.Y * d.Y) < radiusSquared;

        uint ballot = Gpu.WaveActiveBallot(keep);
        int count = Gpu.WaveActiveCountBits(keep);

        if (Gpu.WaveIsFirstLane())
        {
            ballots[group] = ballot;
            groupCounts[group] = count;
        }
    }
}

/// <summary>
/// Passes B and C, one thread per count: the exclusive prefix sum of the 32 counts of a SIMD group
/// (the kept particles before each within the group), and the group's total, stored by its first lane.
/// Over the 1,024 group counts it yields the offsets inside each block and the 32 block totals; over
/// those, the offset of each block and, in <c>totals[0]</c>, the number of kept particles.
/// </summary>
[ThreadGroupSize(CompactionPage.Lanes)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ScanCounts(ReadWriteBuffer<int> counts, ReadWriteBuffer<int> offsets, ReadWriteBuffer<int> totals) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        int count = counts[i];

        offsets[i] = Gpu.WavePrefixSum(count);
        int total = Gpu.WaveActiveSum(count);

        if (Gpu.WaveIsFirstLane())
        {
            totals[i / CompactionPage.Lanes] = total;
        }
    }
}

/// <summary>
/// Pass D, one thread per particle: a kept particle's slot is the kept particles before its block,
/// plus those before its SIMD group within the block, plus those before its lane within the group.
/// The slots are distinct, so the writes need no atomics, and they are in particle order.
/// </summary>
[ThreadGroupSize(CompactionPage.Lanes)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ScatterKept(
    ReadOnlyBuffer<Int2> particles, ReadWriteBuffer<uint> ballots, ReadWriteBuffer<int> groupOffsets, ReadWriteBuffer<int> blockOffsets, ReadWriteBuffer<Int2> compacted) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        int group = i / CompactionPage.Lanes;

        // The ballot of pass A is the 32 votes of this group in one uint: no need to test the particle again.
        bool keep = ((ballots[group] >> Gpu.WaveGetLaneIndex()) & 1u) != 0u;
        int keptBefore = Gpu.WavePrefixSum(keep ? 1 : 0);
        int slot = blockOffsets[group / CompactionPage.Lanes] + groupOffsets[group] + keptBefore;

        if (keep)
        {
            compacted[slot] = particles[i];
        }
    }
}

/// <summary>
/// Draws the field (the particle marks, each cell tinted by its SIMD group's count, the circle and
/// the outline of the group the strip shows) and the strip: that group's ballot as 32 cells.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawCompaction(
    ReadWriteBuffer<uint> pixels, ReadWriteBuffer<uint> field, ReadWriteBuffer<int> groupCounts, int width,
    float centerX, float centerY, float radius, int shownGroup, uint ballot) : IComputeShader
{
    public const int FieldX = 32;
    public const int FieldY = 64;

    /// <summary>The field is 32 cells of <see cref="CellSize"/> pixels each way.</summary>
    public const int CellSize = 15;
    public const int FieldSize = CompactionPage.Lanes * CellSize;

    /// <summary>
    /// The strip: 32 cells, three text columns apart so that a number fits under each, at the top of
    /// text row 36 so that the lane numbers above it and the prefix sums below line up with it.
    /// </summary>
    public const int StripX = 60;
    public const int StripY = LabCanvas.TextTop + (36 * LabCanvas.CellHeight);
    public const int StripPitch = 36;
    public const int StripCell = 32;
    public const int StripHeight = 32;

    private static uint Pack(Float3 color)
    {
        Float3 c = Gpu.Round(Gpu.Saturate(color) * 255f);
        return (uint)c.X | ((uint)c.Y << 8) | ((uint)c.Z << 16) | 0xFF000000u;
    }

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float3 color = new Float3(0.055f, 0.065f, 0.085f);
        Float3 gold = new Float3(1f, 0.8f, 0.35f);
        int fx = x - FieldX;
        int fy = y - FieldY;
        int sx = x - StripX;
        int sy = y - StripY;

        // The cell of the shown group, for its outline (two pixels, just outside the cell).
        int shownX = (shownGroup % CompactionPage.Lanes) * CellSize;
        int shownY = (shownGroup / CompactionPage.Lanes) * CellSize;
        bool nearShown = fx >= shownX - 2 && fx < shownX + CellSize + 2 && fy >= shownY - 2 && fy < shownY + CellSize + 2;
        bool inShown = fx >= shownX && fx < shownX + CellSize && fy >= shownY && fy < shownY + CellSize;

        if (fx >= 0 && fx < FieldSize && fy >= 0 && fy < FieldSize)
        {
            int group = ((fy / CellSize) * CompactionPage.Lanes) + (fx / CellSize);
            uint mark = field[(fy * FieldSize) + fx];
            float distance = Gpu.Length(new Float2(fx + 0.5f - centerX, fy + 0.5f - centerY));

            color = Gpu.Lerp(new Float3(0.025f, 0.035f, 0.06f), new Float3(0.11f, 0.17f, 0.36f), groupCounts[group] / 32f);

            if (mark == 1u)
            {
                color = new Float3(0.3f, 0.36f, 0.5f);
            }

            if (Gpu.Abs(distance - radius) < 1f)
            {
                color = new Float3(0.45f, 0.85f, 1f);
            }

            if (mark == 2u)
            {
                color = gold;
            }
        }

        if (nearShown && !inShown)
        {
            color = new Float3(1f, 1f, 1f);
        }

        if (sx >= 0 && sx < CompactionPage.Lanes * StripPitch && sy >= 0 && sy < StripHeight)
        {
            int lane = sx / StripPitch;
            int u = sx % StripPitch;

            if (u < StripCell)
            {
                bool kept = ((ballot >> lane) & 1u) != 0u;
                bool edge = u == 0 || u == StripCell - 1 || sy == 0 || sy == StripHeight - 1;

                if (kept)
                {
                    color = gold * (0.8f + (0.2f * (1f - Gpu.Abs((sy - 15.5f) / 16f))));
                }
                else
                {
                    color = edge ? new Float3(0.3f, 0.36f, 0.5f) : new Float3(0.03f, 0.04f, 0.06f);
                }
            }
        }

        pixels[(y * width) + x] = Pack(color);
    }
}
