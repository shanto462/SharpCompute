using System;
using System.Linq;
using System.Reflection;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// Dispatches a generated shader on the real runtime. The shader type lives in the assembly emitted by
/// <see cref="GeneratorRun"/>, so the generic dispatch methods are called through reflection.
/// </summary>
internal static class GpuDispatch
{
    /// <summary>
    /// Creates <paramref name="typeName"/> with a <c>ReadWriteBuffer&lt;float&gt;</c> of
    /// <paramref name="outputLength"/> elements followed by <paramref name="arguments"/>, runs
    /// <c>device.For(threads, shader)</c>, and returns the buffer's contents.
    /// </summary>
    public static float[] For(GeneratorRun run, string typeName, int threads, int outputLength, params object[] arguments)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> output = device.AllocateReadWriteBuffer<float>(outputLength);

        Type type = run.LoadType(typeName);
        object shader = Activator.CreateInstance(type, [output, .. arguments])!;

        MethodInfo method = typeof(GraphicsDevice)
            .GetMethods()
            .Single(static m => m.Name == nameof(GraphicsDevice.For) && m.GetParameters().Length == 2)
            .MakeGenericMethod(type);

        try
        {
            method.Invoke(device, [threads, shader]);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }

        return output.ToArray();
    }

    /// <summary>Runs <c>device.For(1, default(T))</c> and returns what it throws.</summary>
    public static Exception? ForDefault(GeneratorRun run, string typeName)
    {
        Type type = run.LoadType(typeName);
        MethodInfo method = typeof(GraphicsDevice)
            .GetMethods()
            .Single(static m => m.Name == nameof(GraphicsDevice.For) && m.GetParameters().Length == 2)
            .MakeGenericMethod(type);

        try
        {
            method.Invoke(GraphicsDevice.GetDefault(), [1, Activator.CreateInstance(type)]);
            return null;
        }
        catch (TargetInvocationException e)
        {
            return e.InnerException;
        }
    }
}
