using System;
using System.Threading.Tasks;
using SharpCompute.Interop;

namespace SharpCompute;

// Batched (ComputeContext) and asynchronous (ForAsync / ForEachAsync) dispatch. The synchronous
// For / ForEach in GraphicsDevice.cs keep their own one-command-buffer-per-call path.
public sealed partial class GraphicsDevice
{
    /// <summary>
    /// Creates a <see cref="ComputeContext"/> that records dispatches into one command buffer and
    /// runs them together when it is executed or disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    public ComputeContext CreateComputeContext()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return new ComputeContext(this);
    }

    /// <summary>Dispatches <paramref name="x"/> threads; the task completes when the GPU has finished.</summary>
    /// <inheritdoc cref="ForAsync{T}(int, int, int, in T)" path="/remarks"/>
    /// <inheritdoc cref="ForAsync{T}(int, int, int, in T)" path="/exception"/>
    public Task ForAsync<T>(int x, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => DispatchAsync(x, 1, 1, in shader);

    /// <summary>Dispatches an <paramref name="x"/> × <paramref name="y"/> grid; the task completes when the GPU has finished.</summary>
    /// <inheritdoc cref="ForAsync{T}(int, int, int, in T)" path="/remarks"/>
    /// <inheritdoc cref="ForAsync{T}(int, int, int, in T)" path="/exception"/>
    public Task ForAsync<T>(int x, int y, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => DispatchAsync(x, y, 1, in shader);

    /// <summary>
    /// Dispatches an <paramref name="x"/> × <paramref name="y"/> × <paramref name="z"/> grid; the task
    /// completes when the GPU has finished.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dispatch is encoded and committed before this method returns, so the calling thread is free
    /// while the GPU runs. Every buffer and texture the shader binds stays alive until the task
    /// completes, even if it is disposed in the meantime; do not touch their contents from the CPU
    /// before then. Continuations run asynchronously (never on Metal's completion thread).
    /// </para>
    /// <para>
    /// Work committed to one device runs in submission order, and later dispatches see the writes of
    /// earlier ones, so dependent <c>ForAsync</c> calls can be issued back to back and awaited once.
    /// Metal keeps at most 64 command buffers in flight per queue; beyond that, this call blocks until
    /// one completes.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is negative. (Thrown, not stored in the task.)</exception>
    /// <exception cref="ArgumentException">A bound resource belongs to a different device. (Thrown.)</exception>
    /// <exception cref="ObjectDisposedException">The device or a bound resource has been disposed. (Thrown.)</exception>
    /// <exception cref="ShaderCompilationException">The shader failed to compile. (Thrown.)</exception>
    /// <exception cref="MetalException">The GPU failed to execute the shader. (Stored in the returned task.)</exception>
    public Task ForAsync<T>(int x, int y, int z, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => DispatchAsync(x, y, z, in shader);

    /// <summary>
    /// Runs a pixel shader once per pixel of <paramref name="texture"/>; the task completes when the
    /// GPU has finished. Binding and grid follow <see cref="ForEach{T}"/>.
    /// </summary>
    /// <inheritdoc cref="ForAsync{T}(int, int, int, in T)" path="/remarks"/>
    /// <exception cref="ArgumentException"><paramref name="texture"/> was allocated from a different device. (Thrown.)</exception>
    /// <exception cref="ObjectDisposedException">The device or <paramref name="texture"/> has been disposed. (Thrown.)</exception>
    /// <exception cref="MetalException">The GPU failed to execute the shader. (Stored in the returned task.)</exception>
    public Task ForEachAsync<T>(ReadWriteTexture2D<Rgba32> texture, in T shader)
        where T : struct, IComputeShader<Float4>, IComputeShaderDescriptor<T>
    {
        ValidateOutputTexture(texture);

        CommandBatch batch = new(this, commandCapacity: 8);

        try
        {
            EncodePixelShader(batch, texture, in shader);
        }
        catch
        {
            batch.Abandon();
            throw;
        }

        return batch.CommitAsync();
    }

    /// <summary>Validates a <c>For</c> grid; false when it is empty and there is nothing to dispatch.</summary>
    internal bool ValidateGrid(int x, int y, int z)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegative(z);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        return x != 0 && y != 0 && z != 0;
    }

    /// <summary>The <c>ForEach</c> preconditions: a live texture from this device.</summary>
    internal void ValidateOutputTexture(ReadWriteTexture2D<Rgba32> texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);

        if (!ReferenceEquals(texture.Device, this))
        {
            throw new ArgumentException(
                "The texture was allocated from a different GraphicsDevice than the one dispatching the shader.", nameof(texture));
        }
    }

    /// <summary>Encodes a non-empty, validated <c>For</c> grid into <paramref name="batch"/>.</summary>
    internal void EncodeGrid<T>(CommandBatch batch, int x, int y, int z, in T shader)
        where T : struct, IComputeShaderDescriptor<T> =>
        Encode(batch, new MTLSize((nuint)x, (nuint)y, (nuint)z), InferRank(y, z), output: null, in shader);

    /// <summary>Encodes a validated <c>ForEach</c> into <paramref name="batch"/>.</summary>
    internal void EncodePixelShader<T>(CommandBatch batch, ReadWriteTexture2D<Rgba32> texture, in T shader)
        where T : struct, IComputeShaderDescriptor<T> =>

        // Pixel shaders are rank 2 by contract, as in ForEach.
        Encode(batch, new MTLSize((nuint)texture.Width, (nuint)texture.Height, 1), rank: 2, texture, in shader);

    private Task DispatchAsync<T>(int x, int y, int z, in T shader)
        where T : struct, IComputeShaderDescriptor<T>
    {
        if (!ValidateGrid(x, y, z))
        {
            return Task.CompletedTask;
        }

        CommandBatch batch = new(this, commandCapacity: 8);

        try
        {
            EncodeGrid(batch, x, y, z, in shader);
        }
        catch
        {
            batch.Abandon();
            throw;
        }

        return batch.CommitAsync();
    }

    private void Encode<T>(CommandBatch batch, MTLSize grid, int rank, ReadWriteTexture2D<Rgba32>? output, in T shader)
        where T : struct, IComputeShaderDescriptor<T>
    {
        ComputePipeline pipeline = GetPipeline<T>();
        MTLSize threadsPerThreadgroup = ResolveThreadGroupSize(
            T.ThreadGroupSizeX, T.ThreadGroupSizeY, T.ThreadGroupSizeZ, rank, pipeline.MaxTotalThreadsPerThreadgroup, typeof(T));

        batch.Encode(pipeline, grid, threadsPerThreadgroup, output, in shader);
    }
}
