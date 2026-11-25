// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using SharpCompute.Presentation;

namespace SharpCompute.Samples.ComputeLab.Pages;

/// <summary>The lab's clock for one frame.</summary>
/// <param name="Time">Seconds of animation (stops while paused).</param>
/// <param name="Deterministic">
/// True for the headless PNGs: a page must then draw nothing that varies from run to run (the order
/// atomics hand out slots in, timings).
/// </param>
public readonly record struct LabFrame(float Time, bool Deterministic);

/// <summary>The outcome of comparing a page's GPU result with the CPU.</summary>
/// <param name="Passed">True when the GPU and the CPU agree.</param>
/// <param name="Summary">One line saying what was compared, shown on the page and in the headless summary.</param>
/// <param name="CpuMilliseconds">How long the CPU took for the work the GPU is timed on.</param>
public readonly record struct LabCheck(bool Passed, string Summary, double CpuMilliseconds);

/// <summary>
/// One page of the lab: one core compute feature, shown, checked and timed. To add a page, implement
/// this and add it to <see cref="LabPages.Create"/>; the window, the keys, the text and the headless
/// mode come with it.
/// </summary>
/// <remarks>
/// A frame is <see cref="Compute"/>, now and then <see cref="Check"/>, then <see cref="Draw"/>, all on
/// one thread. <see cref="Compute"/> prepares the inputs and runs the page's feature kernel on the GPU;
/// <see cref="Check"/> runs the same work on the CPU, on the inputs of the last <see cref="Compute"/>.
/// </remarks>
public interface ILabPage : IDisposable
{
    /// <summary>The page's name, also the stem of its PNG file (lowercase).</summary>
    string Name { get; }

    /// <summary>The feature the page is about, e.g. "atomics".</summary>
    string Feature { get; }

    /// <summary>The page's own keys for the help line, or an empty string.</summary>
    string Help { get; }

    /// <summary>The page's settings worth timing separately (at least one), e.g. "256 bins" and "4 bins".</summary>
    IReadOnlyList<string> Variants { get; }

    /// <summary>The index of the active entry of <see cref="Variants"/>.</summary>
    int Variant { get; set; }

    /// <summary>How long the feature kernel's GPU dispatch of the last <see cref="Compute"/> took (commit to completion).</summary>
    double GpuMilliseconds { get; }

    /// <summary>Prepares this frame's inputs and runs the feature kernel on the GPU.</summary>
    void Compute(LabFrame frame);

    /// <summary>Runs the same work on the CPU (<c>ForCpu</c> of the same shader, or plain C#) and compares it with the GPU's result.</summary>
    LabCheck Check();

    /// <summary>Draws the page into every pixel of <paramref name="canvas"/> and adds its labels; rows 0..2 and the last row are the lab's.</summary>
    void Draw(LabCanvas canvas, LabFrame frame);

    /// <summary>A key the lab itself doesn't use. Returns true when it changed what the page computes.</summary>
    bool OnKeyDown(Key key) => false;
}

/// <summary>The pages, in ←/→ order.</summary>
public static class LabPages
{
    public static List<ILabPage> Create(GraphicsDevice device) =>
    [
        new HistogramPage(device),
        new CompactionPage(device),
        new BitsPage(device),
    ];
}
