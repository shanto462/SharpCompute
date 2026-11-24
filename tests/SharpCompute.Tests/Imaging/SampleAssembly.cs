using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SharpCompute.Tests.Imaging;

/// <summary>
/// Loads a built sample assembly (ImageProcessing, Paint) so tests can exercise its public types.
/// Like <c>ViewerAssembly</c>, this project has no project reference to the samples: it loads the
/// output that <c>dotnet build SharpCompute.slnx</c> produces for the same configuration.
/// </summary>
internal static class SampleAssembly
{
    internal const string ImageProcessing = "SharpCompute.Samples.ImageProcessing";
    internal const string Paint = "SharpCompute.Samples.Paint";

    private static readonly ConcurrentDictionary<string, string?> s_paths = new();
    private static readonly ConcurrentDictionary<string, Assembly> s_assemblies = new();

    internal static string NotBuilt(string name) =>
        $"{name} has not been built for this configuration; build SharpCompute.slnx (dotnet test SharpCompute.slnx builds it).";

    internal static string? PathOf(string name) => s_paths.GetOrAdd(name, Locate);

    internal static Assembly Get(string name) =>
        s_assemblies.GetOrAdd(name, static n => Assembly.LoadFrom(PathOf(n) ?? throw new InvalidOperationException(NotBuilt(n))));

    /// <summary>A public type by its name relative to <paramref name="assembly"/>'s namespaces (e.g. "Imaging.FilterSpec").</summary>
    internal static Type Type(string assembly, string typeName)
    {
        string fullName = typeName.StartsWith("SharpCompute.", StringComparison.Ordinal) ? typeName : $"{assembly}.{typeName}";
        return Get(assembly).GetType(fullName, throwOnError: true)!;
    }

    /// <summary>Creates an instance as <c>dynamic</c>, so tests read naturally.</summary>
    internal static dynamic Create(string assembly, string typeName, params object?[] args) =>
        Activator.CreateInstance(Type(assembly, typeName), args)!;

    /// <summary>Calls a public static method, picking the overload by argument count (and names when ambiguous).</summary>
    internal static object? Call(string assembly, string typeName, string method, params object?[] args)
    {
        MethodInfo[] candidates = [.. Type(assembly, typeName).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == method && m.GetParameters().Length == args.Length)];

        MethodInfo target = candidates.Length == 1
            ? candidates[0]
            : candidates.Single(m => m.GetParameters().Zip(args).All(p => p.Second is null || p.First.ParameterType.IsInstanceOfType(p.Second) || p.First.ParameterType.IsByRef));

        try
        {
            return target.Invoke(null, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }
    }

    /// <summary>An enum value of a sample type, by name.</summary>
    internal static object Enum(string assembly, string typeName, string value) => System.Enum.Parse(Type(assembly, typeName), value);

    private static string? Locate(string name)
    {
        // The tests run from tests/SharpCompute.Tests/bin/<Configuration>/<tfm>/.
        DirectoryInfo output = new(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        string tfm = output.Name;
        string? configuration = output.Parent?.Name;

        for (DirectoryInfo? dir = output; dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SharpCompute.slnx")))
            {
                string candidate = Path.Combine(dir.FullName, "samples", name, "bin", configuration ?? "Debug", tfm, name + ".dll");
                return File.Exists(candidate) ? candidate : null;
            }
        }

        return null;
    }
}

/// <summary>A test of a sample's code that needs nothing but the built sample.</summary>
public sealed class SampleFactAttribute : FactAttribute
{
    public SampleFactAttribute(string assembly)
    {
        if (SampleAssembly.PathOf(assembly) is null)
        {
            Skip = SampleAssembly.NotBuilt(assembly);
        }
    }
}

/// <summary>A GPU test of a sample: skipped without a Metal device or the built sample.</summary>
public sealed class SampleGpuFactAttribute : FactAttribute
{
    public SampleGpuFactAttribute(string assembly)
    {
        if (!GraphicsDevice.IsSupported)
        {
            Skip = MetalFactAttribute.SkipReason;
        }
        else if (SampleAssembly.PathOf(assembly) is null)
        {
            Skip = SampleAssembly.NotBuilt(assembly);
        }
    }
}

/// <summary>A GPU theory over a sample: skipped without a Metal device or the built sample.</summary>
public sealed class SampleGpuTheoryAttribute : TheoryAttribute
{
    public SampleGpuTheoryAttribute(string assembly)
    {
        if (!GraphicsDevice.IsSupported)
        {
            Skip = MetalFactAttribute.SkipReason;
        }
        else if (SampleAssembly.PathOf(assembly) is null)
        {
            Skip = SampleAssembly.NotBuilt(assembly);
        }
    }
}
