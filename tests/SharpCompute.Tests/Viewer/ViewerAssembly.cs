using System;
using System.IO;
using System.Reflection;

namespace SharpCompute.Tests.Viewer;

/// <summary>
/// Locates and loads the built <c>SharpCompute.Samples.Viewer</c> assembly, whose demo shaders these
/// tests exercise. The tests load it from the Viewer's own output folder, for the same configuration
/// as the tests. This project's reference to the Viewer is build-only
/// (<c>ReferenceOutputAssembly="false"</c>): it makes any build of the tests build the Viewer too,
/// without referencing or copying it.
/// </summary>
internal static class ViewerAssembly
{
    internal const string Name = "SharpCompute.Samples.Viewer";

    private static readonly Lazy<string?> s_path = new(Locate);
    private static readonly Lazy<Assembly> s_assembly = new(() => Assembly.LoadFrom(Path ?? throw new InvalidOperationException(NotBuilt)));

    internal static string NotBuilt => Imaging.SampleAssembly.NotBuilt(Name);

    /// <summary>Full path of the Viewer's dll, or null when it hasn't been built.</summary>
    internal static string? Path => s_path.Value;

    internal static Assembly Value => s_assembly.Value;

    /// <summary>Creates an instance of a public Viewer type, as <c>dynamic</c> so tests read naturally.</summary>
    internal static dynamic Create(string typeName, params object[] args) =>
        Activator.CreateInstance(Value.GetType($"{Name}.{typeName}", throwOnError: true)!, args)!;

    private static string? Locate()
    {
        // The tests run from tests/SharpCompute.Tests/bin/<Configuration>/<tfm>/.
        DirectoryInfo output = new(AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        string tfm = output.Name;
        string? configuration = output.Parent?.Name;

        for (DirectoryInfo? dir = output; dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "SharpCompute.slnx")))
            {
                string candidate = System.IO.Path.Combine(dir.FullName, "samples", Name, "bin", configuration ?? "Debug", tfm, Name + ".dll");
                return File.Exists(candidate) ? candidate : null;
            }
        }

        return null;
    }
}

/// <summary>A GPU test of the Viewer's demos: skipped without a Metal device or a built Viewer.</summary>
public sealed class ViewerFactAttribute : FactAttribute
{
    public ViewerFactAttribute()
    {
        if (!GraphicsDevice.IsSupported)
        {
            Skip = MetalFactAttribute.SkipReason;
        }
        else if (ViewerAssembly.Path is null)
        {
            Skip = ViewerAssembly.NotBuilt;
        }
    }
}
