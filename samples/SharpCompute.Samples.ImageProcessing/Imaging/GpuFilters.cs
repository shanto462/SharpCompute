// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;

namespace SharpCompute.Samples.Imaging;

/// <summary>
/// Runs <see cref="FilterSpec"/> filters on the GPU over <see cref="Float4"/> image buffers of one
/// size. Multi-pass filters use internal scratch buffers, allocated on first use. Every dispatch is
/// synchronous, so the destination is ready when <see cref="Apply"/> returns.
/// </summary>
public sealed class GpuFilters : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Dictionary<int, ReadOnlyBuffer<float>> _weights = [];
    private ReadWriteBuffer<Float4>? _scratch;
    private ReadWriteBuffer<Float4>? _scratch2;

    public GpuFilters(GraphicsDevice device, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _device = device;
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    private int PixelCount => Width * Height;

    /// <summary>
    /// Filters <paramref name="source"/> into <paramref name="destination"/>; they must be different
    /// buffers of <see cref="Width"/> × <see cref="Height"/> pixels. <paramref name="source"/> is not modified.
    /// </summary>
    public void Apply(FilterSpec filter, ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (ReferenceEquals(source, destination))
        {
            throw new ArgumentException("The source and destination must be different buffers (filters read neighbors).", nameof(destination));
        }

        if (source.Length != PixelCount || destination.Length != PixelCount)
        {
            throw new ArgumentException($"Both buffers must hold {Width}×{Height} = {PixelCount} pixels.");
        }

        switch (filter.Kind)
        {
            case FilterKind.Blur:
                Blur(source, destination, (int)filter[0]);
                break;

            case FilterKind.Bokeh:
                _device.For(Width, Height, new BokehBlur(source, destination, Width, Height, filter[0], filter[1]));
                break;

            case FilterKind.Sharpen:
                ReadWriteBuffer<Float4> blurred = Scratch2;
                Blur(source, blurred, FilterSpec.SharpenBlurRadius);
                _device.For(PixelCount, new UnsharpMask(source, blurred, destination, filter[0]));
                break;

            case FilterKind.Edges:
                _device.For(Width, Height, new SobelEdges(source, destination, Width, Height));
                break;

            case FilterKind.Grade:
                _device.For(PixelCount, new ColorGrade(source, destination, CpuFilters.ExposureGain(filter[0]), filter[1], filter[2]));
                break;

            case FilterKind.Pixelate:
                _device.For(Width, Height, new Pixelate(source, destination, Width, Height, (int)filter[0]));
                break;

            case FilterKind.Invert:
                _device.For(PixelCount, new PointFilter(source, destination, 0, 0f));
                break;

            case FilterKind.Posterize:
                _device.For(PixelCount, new PointFilter(source, destination, 1, filter[0]));
                break;

            case FilterKind.Grayscale:
                _device.For(PixelCount, new PointFilter(source, destination, 2, 0f));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(filter), filter.Kind, "Unknown filter.");
        }
    }

    public void Dispose()
    {
        _scratch?.Dispose();
        _scratch2?.Dispose();

        foreach (ReadOnlyBuffer<float> weights in _weights.Values)
        {
            weights.Dispose();
        }

        _weights.Clear();
    }

    private ReadWriteBuffer<Float4> Scratch => _scratch ??= _device.AllocateReadWriteBuffer<Float4>(PixelCount);

    private ReadWriteBuffer<Float4> Scratch2 => _scratch2 ??= _device.AllocateReadWriteBuffer<Float4>(PixelCount);

    private void Blur(ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination, int radius)
    {
        if (!_weights.TryGetValue(radius, out ReadOnlyBuffer<float>? weights))
        {
            weights = _device.AllocateReadOnlyBuffer<float>(FilterSpec.GaussianWeights(radius));
            _weights.Add(radius, weights);
        }

        ReadWriteBuffer<Float4> horizontal = Scratch;
        _device.For(Width, Height, new GaussianBlurPass(source, horizontal, weights, Width, Height, radius, 1, 0));
        _device.For(Width, Height, new GaussianBlurPass(horizontal, destination, weights, Width, Height, radius, 0, 1));
    }
}
