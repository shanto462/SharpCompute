using System;
using System.Threading.Tasks;

namespace SharpCompute;

// Pipeline warm-up: compile a shader before its first dispatch needs it.
public sealed partial class GraphicsDevice
{
    /// <summary>
    /// Compiles the pipeline of <typeparamref name="T"/> and caches it, so that the first dispatch of
    /// the shader does not pay for it. No-op when it is already compiled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A shader is compiled from its MSL the first time it is used on a device, which takes
    /// milliseconds to tens of milliseconds: a visible stall when that first use is a frame. Call this
    /// at startup (or <see cref="PrepareAsync{T}()"/> from a loading screen) for the shaders a frame
    /// will dispatch. A shader is compiled once however it is first used: a dispatch that arrives
    /// while it is being prepared on another thread waits for that compilation instead of starting
    /// its own.
    /// </para>
    /// <para>
    /// This prepares the pipeline that <c>For</c>, <c>ForEach</c>, <c>ForAsync</c>,
    /// <c>ForIndirect</c> and <see cref="ComputeContext"/> use. The separate specialization that
    /// <c>ForSplit</c> runs on the GPU is still compiled by the first split dispatch.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    /// <exception cref="ShaderCompilationException">The shader failed to compile.</exception>
    public void Prepare<T>()
        where T : struct, IComputeShaderDescriptor<T>
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        GetPipeline<T>();
    }

    /// <summary>
    /// Compiles the pipeline of <typeparamref name="T"/> on a thread-pool thread and caches it; the
    /// task completes when it is ready. Completes at once when it is already compiled.
    /// </summary>
    /// <inheritdoc cref="Prepare{T}()" path="/remarks"/>
    /// <exception cref="ObjectDisposedException">The device has been disposed. (Thrown.)</exception>
    /// <exception cref="ShaderCompilationException">The shader failed to compile. (Stored in the returned task.)</exception>
    public Task PrepareAsync<T>()
        where T : struct, IComputeShaderDescriptor<T>
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        return IsPipelineCached<T>() ? Task.CompletedTask : Task.Run(() => { GetPipeline<T>(); });
    }

    /// <summary>
    /// Compiles and caches the pipelines of several shaders, one after the other:
    /// <c>device.Prepare(ShaderReference.Of&lt;Blur&gt;(), ShaderReference.Of&lt;Sharpen&gt;())</c>.
    /// </summary>
    /// <inheritdoc cref="Prepare{T}()" path="/remarks"/>
    /// <exception cref="ArgumentNullException">An element of <paramref name="shaders"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    /// <exception cref="ShaderCompilationException">A shader failed to compile; the ones after it are not compiled.</exception>
    public void Prepare(params ReadOnlySpan<ShaderReference> shaders)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        foreach (ShaderReference shader in shaders)
        {
            ArgumentNullException.ThrowIfNull(shader, nameof(shaders));
        }

        foreach (ShaderReference shader in shaders)
        {
            shader.Prepare(this);
        }
    }

    /// <summary>
    /// Compiles and caches the pipelines of several shaders in parallel on the thread pool; the task
    /// completes when all are ready.
    /// </summary>
    /// <inheritdoc cref="Prepare{T}()" path="/remarks"/>
    /// <exception cref="ArgumentNullException"><paramref name="shaders"/> or one of its elements is null. (Thrown.)</exception>
    /// <exception cref="ObjectDisposedException">The device has been disposed. (Thrown.)</exception>
    /// <exception cref="ShaderCompilationException">
    /// A shader failed to compile. (Stored in the returned task, which completes only after every shader
    /// has been tried; the others are prepared regardless.)
    /// </exception>
    public Task PrepareAsync(params ShaderReference[] shaders)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        foreach (ShaderReference shader in shaders)
        {
            ArgumentNullException.ThrowIfNull(shader, nameof(shaders));
        }

        Task[] tasks = new Task[shaders.Length];

        for (int i = 0; i < shaders.Length; i++)
        {
            tasks[i] = shaders[i].PrepareAsync(this);
        }

        return Task.WhenAll(tasks);
    }
}

/// <summary>
/// A shader type as a value, for APIs that take several shaders at once
/// (<see cref="GraphicsDevice.Prepare(ReadOnlySpan{ShaderReference})"/>): <c>ShaderReference.Of&lt;Blur&gt;()</c>.
/// </summary>
/// <remarks>
/// A <see cref="Type"/> cannot stand in for a shader here: everything the runtime needs from a shader
/// is a static member of its generated descriptor, reachable only through the type parameter, and
/// getting from a <see cref="Type"/> back to that would take reflection, which trimming and Native
/// AOT do not support. A reference captures the type parameter when it is created instead. There is
/// one instance per shader type, so references compare by reference; they are immutable and thread-safe.
/// </remarks>
public abstract class ShaderReference
{
    private protected ShaderReference()
    {
    }

    /// <summary>The shader struct this reference stands for.</summary>
    public abstract Type ShaderType { get; }

    /// <summary>The reference for the shader <typeparamref name="T"/>.</summary>
    public static ShaderReference Of<T>()
        where T : struct, IComputeShaderDescriptor<T> => Reference<T>.Instance;

    public override string ToString() => ShaderType.ToString();

    internal abstract void Prepare(GraphicsDevice device);

    internal abstract Task PrepareAsync(GraphicsDevice device);

    private sealed class Reference<T> : ShaderReference
        where T : struct, IComputeShaderDescriptor<T>
    {
        internal static readonly Reference<T> Instance = new();

        public override Type ShaderType => typeof(T);

        internal override void Prepare(GraphicsDevice device) => device.Prepare<T>();

        internal override Task PrepareAsync(GraphicsDevice device) => device.PrepareAsync<T>();
    }
}
