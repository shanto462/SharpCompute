using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Threading;
using SharpCompute.Interop;

namespace SharpCompute;

/// <summary>Where the compiled pipeline of a shader came from (<see cref="GraphicsDevice.GetPipelineOrigin{T}()"/>).</summary>
public enum PipelineOrigin
{
    /// <summary>The device has no pipeline for the shader yet: it has been neither dispatched nor prepared, or it is still being compiled.</summary>
    None = 0,

    /// <summary>The shader's MSL was compiled by this process, when the shader was first dispatched or prepared.</summary>
    CompiledAtRunTime,

    /// <summary>
    /// The kernel was compiled when the application was built and loaded from the <c>.metallib</c>
    /// next to the shader's assembly.
    /// </summary>
    Precompiled,
}

// Precompiled shaders: pipelines from the .metallib that the build made of an assembly's shaders.
public sealed unsafe partial class GraphicsDevice
{
    /// <summary>
    /// The <see cref="AppContext"/> switch that turns precompiled shaders off when set to false: every
    /// shader is then compiled from its MSL, whatever libraries are next to the assemblies.
    /// </summary>
    internal const string PrecompiledShadersSwitch = "SharpCompute.UsePrecompiledShaders";

    // One library per assembly that has shaders, opened the first time one of them needs a pipeline.
    // null when the assembly has no usable library.
    private readonly ConcurrentDictionary<Assembly, Lazy<ObjCHandle?>> _precompiledLibraries = new();
    private int _precompiledPipelineCount;
    private bool _usePrecompiledShaders;
    private Func<Assembly, string?> _precompiledLibraryLocator = FindPrecompiledLibrary;

    /// <summary>
    /// Whether the pipeline of <typeparamref name="T"/> on this device was compiled when the
    /// application was built or by this process: a diagnostic, for a status line or a log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A project that sets <c>SharpComputePrecompileShaders</c> gets its shaders compiled at build
    /// time into <c>&lt;AssemblyName&gt;.metallib</c>, which is copied next to the assembly. The device
    /// looks for that file next to the shader's assembly, then in <see cref="AppContext.BaseDirectory"/>
    /// (where it is when the application is published as a single file or with Native AOT), and takes
    /// a kernel from it only when the library holds the kernel that was compiled from exactly the
    /// shader's current MSL and compile options. Everything else is compiled from the MSL when it is
    /// first dispatched or prepared, as it is without the library: a missing or outdated library, a
    /// shader that is not in it, a hand-written descriptor. Results are the same either way; what
    /// differs is how long the first use of the shader takes.
    /// </para>
    /// <para>
    /// Setting the <see cref="AppContext"/> switch <c>SharpCompute.UsePrecompiledShaders</c> to false
    /// before a device is created makes that device compile everything from source.
    /// </para>
    /// <para>
    /// This reports the pipeline that <c>For</c>, <c>ForEach</c>, <c>ForAsync</c>, <c>ForIndirect</c>
    /// and <see cref="ComputeContext"/> use. The one <c>ForSplit</c> runs on the GPU has the same origin
    /// whenever both exist, since both come from the one kernel.
    /// </para>
    /// </remarks>
    /// <returns><see cref="PipelineOrigin.None"/> until the shader has been dispatched or prepared on this device.</returns>
    public PipelineOrigin GetPipelineOrigin<T>()
        where T : struct, IComputeShaderDescriptor<T> => GetPipelineOrigin<T>(windowed: false);

    /// <summary><see cref="GetPipelineOrigin{T}()"/> for the ordinary pipeline or, with <paramref name="windowed"/>, the split specialization.</summary>
    internal PipelineOrigin GetPipelineOrigin<T>(bool windowed)
        where T : struct, IComputeShaderDescriptor<T>
    {
        if (!_pipelines.TryGetValue(new PipelineKey(typeof(T), windowed, T.CompileOptions), out Lazy<ComputePipeline>? lazy) || !lazy.IsValueCreated)
        {
            return PipelineOrigin.None;
        }

        // IsValueCreated is only true for a pipeline that was built, so this never waits or throws.
        return lazy.Value.IsPrecompiled ? PipelineOrigin.Precompiled : PipelineOrigin.CompiledAtRunTime;
    }

    /// <summary>Tests only: how many pipelines this device has taken from precompiled libraries.</summary>
    internal int PrecompiledPipelineCount => Volatile.Read(ref _precompiledPipelineCount);

    /// <summary>Tests only: how many pipelines this device has built or tried to build, from source or from a precompiled library.</summary>
    internal int PipelineCreationCount => PipelineCompileCount + PrecompiledPipelineCount;

    /// <summary>
    /// Tests only: false makes this device compile every shader from source. Set it before the device
    /// creates its first pipeline. The default is the <see cref="PrecompiledShadersSwitch"/> switch.
    /// </summary>
    internal bool UsePrecompiledShaders
    {
        get => _usePrecompiledShaders;
        set => _usePrecompiledShaders = value;
    }

    /// <summary>
    /// Tests only: replaces where this device looks for the precompiled library of an assembly (a
    /// file path, or null for none). Set it before the device creates its first pipeline.
    /// </summary>
    internal Func<Assembly, string?> PrecompiledLibraryLocator
    {
        get => _precompiledLibraryLocator;
        set => _precompiledLibraryLocator = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The value of <see cref="PrecompiledShadersSwitch"/>; true unless the application has set it to false.</summary>
    private static bool PrecompiledShadersEnabled => !AppContext.TryGetSwitch(PrecompiledShadersSwitch, out bool enabled) || enabled;

    /// <summary>
    /// The pipeline of a shader from its assembly's precompiled library, or null when there is no
    /// library, when the library does not hold this shader as it is now, or when Metal cannot build
    /// the pipeline from it. The caller then compiles the MSL, which also reports a real error properly.
    /// </summary>
    private ComputePipeline? TryCreatePrecompiledPipeline(
        string mslSource, string entryPoint, Type shaderType, bool windowed, ShaderCompileOptions options)
    {
        if (!_usePrecompiledShaders || GetPrecompiledLibrary(shaderType.Assembly) is not { } library)
        {
            return null;
        }

        // The hash in the name is what makes the library trustworthy: it is found only if it was
        // built from this entry point, this MSL and these options (PrecompiledShaderContract).
        string functionName = PrecompiledShaderContract.FunctionName(entryPoint, mslSource, (int)options);

        using AutoreleasePool pool = AutoreleasePool.Push();
        using ObjCHandle.Lease device = _device.Acquire(this);
        using ObjCHandle.Lease handle = library.Acquire(this);

        ComputePipeline? pipeline = BuildPipeline(device.Pointer, handle.Pointer, functionName, windowed, isPrecompiled: true, out _, out _);

        if (pipeline is not null)
        {
            Interlocked.Increment(ref _precompiledPipelineCount);
        }

        return pipeline;
    }

    /// <summary>
    /// The precompiled library (<c>id&lt;MTLLibrary&gt;</c>) of <paramref name="assembly"/> on this device,
    /// opened on first use; null when it has none.
    /// </summary>
    private ObjCHandle? GetPrecompiledLibrary(Assembly assembly) =>
        _precompiledLibraries.GetOrAdd(
            assembly,
            static (assembly, device) => new Lazy<ObjCHandle?>(() => device.LoadPrecompiledLibrary(assembly), LazyThreadSafetyMode.ExecutionAndPublication),
            this).Value;

    /// <summary>
    /// <c>newLibraryWithURL:error:</c> on the file the locator names. A file that is not there, cannot
    /// be read or is not a library for this device gives null: precompiled shaders are an
    /// optimisation, so a bad file is not an error, and the answer is remembered so that the file is
    /// not opened again for every shader.
    /// </summary>
    private ObjCHandle? LoadPrecompiledLibrary(Assembly assembly)
    {
        string? path = _precompiledLibraryLocator(assembly);

        if (path is null || !File.Exists(path))
        {
            return null;
        }

        using AutoreleasePool pool = AutoreleasePool.Push();
        using ObjCHandle.Lease device = _device.Acquire(this);

        nint pathString = NSString.Create(Path.GetFullPath(path));

        try
        {
            // fileURLWithPath: returns an autoreleased NSURL; the pool above drains it.
            nint url = ObjC.SendIntPtr(NSUrlClass.Value, Selectors.FileURLWithPath, pathString);

            if (url == 0)
            {
                return null;
            }

            nint error = 0;
            nint library = ObjC.SendIntPtr(device.Pointer, Selectors.NewLibraryWithURLError, url, &error);

            return library == 0 ? null : new ObjCHandle(library);
        }
        finally
        {
            ObjC.Release(pathString);
        }
    }

    /// <summary>
    /// <c>&lt;AssemblyName&gt;.metallib</c> next to the assembly, or else in the application's base
    /// directory; null when neither exists.
    /// </summary>
    /// <remarks>
    /// The two are the same folder for an ordinary application. They differ for an assembly loaded
    /// from elsewhere (a plug-in, a test host), whose library is next to it, and for an application
    /// published as a single file or with Native AOT, whose assemblies have no location: the build
    /// puts the library next to the executable then, which is the base directory.
    /// </remarks>
    [UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000:Avoid accessing Assembly file path when publishing as a single file",
        Justification = "An empty location is expected and handled: the library is then looked for in AppContext.BaseDirectory.")]
    private static string? FindPrecompiledLibrary(Assembly assembly)
    {
        if (assembly.GetName().Name is not { Length: > 0 } name)
        {
            return null;
        }

        string fileName = name + PrecompiledShaderContract.LibraryExtension;
        string location = assembly.Location;

        if (location.Length > 0 && Path.GetDirectoryName(location) is { Length: > 0 } directory)
        {
            string besideAssembly = Path.Combine(directory, fileName);

            if (File.Exists(besideAssembly))
            {
                return besideAssembly;
            }
        }

        string inBaseDirectory = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(inBaseDirectory) ? inBaseDirectory : null;
    }

    /// <summary>Releases the libraries that were opened. Pipelines already built from them keep their own references.</summary>
    private void DisposePrecompiledLibraries()
    {
        foreach (Lazy<ObjCHandle?> library in _precompiledLibraries.Values)
        {
            // One that is still being opened is not waited for: its handle is released by its finalizer.
            if (library.IsValueCreated)
            {
                library.Value?.Dispose();
            }
        }

        _precompiledLibraries.Clear();
    }

    private static class NSUrlClass
    {
        internal static readonly nint Value = ObjC.GetClass("NSURL");
    }
}
