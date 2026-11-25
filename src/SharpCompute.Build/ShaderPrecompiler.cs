using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SharpCompute.Build;

/// <summary>What <see cref="ShaderPrecompiler.Run"/> is given.</summary>
internal sealed class PrecompileRequest
{
    /// <summary>The compiled assembly whose shaders are precompiled.</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>The library to write (<c>&lt;AssemblyName&gt;.metallib</c>).</summary>
    public required string OutputPath { get; init; }

    /// <summary>A directory this assembly's build owns, for each shader's source and AIR.</summary>
    public required string IntermediateDirectory { get; init; }

    /// <summary>
    /// Written when the library is as it should be for the assembly (which includes "there is none,
    /// because the assembly has no shaders") and removed when it is not, so that a build can skip the
    /// tool while the assembly is older than this file. Null for no such file.
    /// </summary>
    public string? StatePath { get; init; }

    /// <summary>The <c>xcrun</c> to run.</summary>
    public string Xcrun { get; init; } = "xcrun";

    /// <summary>How many compilers run at once; 0 for a quarter of the processors, at most 4.</summary>
    public int MaxParallelism { get; init; }

    /// <summary>Receives the lines a build should show.</summary>
    public Action<string> Log { get; init; } = static _ => { };
}

/// <summary>How a run ended.</summary>
internal enum PrecompileStatus
{
    /// <summary>The assembly has no shader records. There is no library, and one left by an earlier build was removed.</summary>
    NoShaders,

    /// <summary>The library already holds exactly the assembly's shaders: nothing was compiled or linked.</summary>
    UpToDate,

    /// <summary>The library was linked.</summary>
    Built,

    /// <summary>The Metal toolchain cannot be run. There is no library, and the shaders are compiled at run time.</summary>
    ToolchainUnavailable,

    /// <summary>The toolchain ran, but no library could be made (no shader compiled, or the linker refused). There is no library.</summary>
    Failed,
}

/// <summary>The outcome of a run: how many shaders the assembly records, and what happened to them.</summary>
/// <param name="Status">How the run ended.</param>
/// <param name="Shaders">The shader records in the assembly.</param>
/// <param name="Compiled">The shaders compiled by this run.</param>
/// <param name="Reused">The shaders whose AIR from an earlier run was used again.</param>
/// <param name="Rejected">The shaders the compiler rejected; they are not in the library.</param>
internal readonly record struct PrecompileResult(PrecompileStatus Status, int Shaders, int Compiled, int Reused, int Rejected);

/// <summary>
/// Precompiles the shaders of one assembly: reads the generator's records from its metadata, compiles
/// each shader's MSL to AIR and links them into one library.
/// </summary>
/// <remarks>
/// <para>
/// The work is incremental at two levels. A shader's AIR is stored under the name its kernel has in
/// the library, which hashes the source and the options it was compiled from, so AIR that is there
/// is AIR that is right, and only new or changed shaders are compiled. The library is linked again
/// only when the set of those names differs from the set it was last linked from.
/// </para>
/// <para>
/// A shader the compiler rejects is remembered the same way, by a file that holds what the compiler
/// said: it is reported by every run, and compiled again only after a clean, since the same source
/// and options would be rejected again.
/// </para>
/// <para>
/// Nothing here is an error for the build. Precompiled shaders are an optimisation, and whatever is
/// not in the library is compiled at run time as before: without a toolchain no library is produced,
/// and a shader the compiler rejects is left out and reported. One left by an earlier build is removed
/// whenever this build cannot produce one, so the file next to an assembly is always this build's.
/// </para>
/// </remarks>
internal static class ShaderPrecompiler
{
    private const string SourceExtension = ".metal";
    private const string AirExtension = ".air";
    private const string RejectedExtension = ".rejected";

    // The longest file name stem used for a shader. The hash is at the end of the function name, so
    // the tail of a longer name is still unique.
    private const int MaxFileStem = 180;

    public static PrecompileResult Run(PrecompileRequest request)
    {
        IReadOnlyList<ShaderRecord> records = ShaderRecordReader.Read(request.AssemblyPath, line => request.Log("SharpCompute: " + line));
        string libraryName = Path.GetFileName(request.OutputPath);

        if (records.Count == 0)
        {
            DeleteDirectory(request.IntermediateDirectory);
            DeleteFile(request.OutputPath);
            WriteState(request, []);
            return new PrecompileResult(PrecompileStatus.NoShaders, 0, 0, 0, 0);
        }

        Directory.CreateDirectory(request.IntermediateDirectory);

        // Two records with one function name are one kernel: the same entry point, source and options.
        Dictionary<string, ShaderRecord> shaders = new(StringComparer.Ordinal);

        foreach (ShaderRecord record in records)
        {
            shaders.TryAdd(FileStem(record), record);
        }

        string[] stems = [.. shaders.Keys.Order(StringComparer.Ordinal)];
        RemoveStaleFiles(request.IntermediateDirectory, stems);

        // What earlier runs settled: AIR that is there, and shaders the compiler has already rejected.
        string[] reused = [.. stems.Where(stem => Exists(request, stem, AirExtension))];
        string[] rejectedBefore = [.. stems.Where(stem => !Exists(request, stem, AirExtension) && Exists(request, stem, RejectedExtension))];
        string[] missing = [.. stems.Except(reused, StringComparer.Ordinal).Except(rejectedBefore, StringComparer.Ordinal)];

        foreach (string stem in rejectedBefore)
        {
            request.Log(File.ReadAllText(Path.Combine(request.IntermediateDirectory, stem + RejectedExtension)));
        }

        if (missing.Length == 0 && ReadState(request) is { } linked && linked.SequenceEqual(reused, StringComparer.Ordinal) && (reused.Length == 0 || File.Exists(request.OutputPath)))
        {
            WriteState(request, reused);
            return new PrecompileResult(PrecompileStatus.UpToDate, records.Count, 0, reused.Length, rejectedBefore.Length);
        }

        MetalToolchain toolchain = new(request.Xcrun);

        if (toolchain.FindProblem() is { } problem)
        {
            request.Log(
                $"SharpCompute: {libraryName} was not built, because the Metal toolchain is not available ({problem}). " +
                "Shaders will be compiled at run time. Install it with 'xcodebuild -downloadComponent MetalToolchain', " +
                "or set SharpComputePrecompileShaders to false.");
            DeleteFile(request.OutputPath);
            DeleteState(request);
            return new PrecompileResult(PrecompileStatus.ToolchainUnavailable, records.Count, 0, 0, 0);
        }

        int rejected = Compile(request, toolchain, shaders, missing) + rejectedBefore.Length;
        string[] compiled = [.. stems.Where(stem => Exists(request, stem, AirExtension))];
        int compiledNow = compiled.Length - reused.Length;

        if (compiled.Length == 0)
        {
            request.Log($"SharpCompute: {libraryName} was not built: none of the assembly's {stems.Length} shaders compiled. They will be compiled at run time.");
            DeleteFile(request.OutputPath);
            WriteState(request, []);
            return new PrecompileResult(PrecompileStatus.Failed, records.Count, 0, 0, rejected);
        }

        // Linked beside the output and moved into place, so that a build that is interrupted leaves
        // the previous library or none, never half of one.
        string temporary = request.OutputPath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.OutputPath))!);
        ToolResult link = toolchain.Link(compiled.Select(static stem => stem + AirExtension), Path.GetFullPath(temporary), request.IntermediateDirectory);

        if (link.ExitCode != 0 || !File.Exists(temporary))
        {
            request.Log($"SharpCompute: {libraryName} was not built: the Metal linker failed. Shaders will be compiled at run time.\n{link.Output}");
            File.Delete(temporary);
            DeleteFile(request.OutputPath);
            DeleteState(request);
            return new PrecompileResult(PrecompileStatus.Failed, records.Count, compiledNow, reused.Length, rejected);
        }

        File.Move(temporary, request.OutputPath, overwrite: true);
        WriteState(request, compiled);

        request.Log(
            $"SharpCompute: {libraryName}: {compiled.Length} precompiled shader{(compiled.Length == 1 ? string.Empty : "s")} " +
            $"({compiledNow} compiled, {reused.Length} unchanged{(rejected == 0 ? string.Empty : $", {rejected} rejected and left to run time")}).");

        return new PrecompileResult(PrecompileStatus.Built, records.Count, compiledNow, reused.Length, rejected);
    }

    /// <summary>Compiles the given shaders, several at a time. Returns how many the compiler rejected.</summary>
    private static int Compile(PrecompileRequest request, MetalToolchain toolchain, Dictionary<string, ShaderRecord> shaders, string[] stems)
    {
        int parallelism = request.MaxParallelism > 0 ? request.MaxParallelism : Math.Clamp(Environment.ProcessorCount / 4, 1, 4);
        string?[] failures = new string?[stems.Length];

        Parallel.For(0, stems.Length, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, index =>
        {
            string stem = stems[index];
            ShaderRecord shader = shaders[stem];
            string air = Path.Combine(request.IntermediateDirectory, stem + AirExtension);
            string temporary = air + ".tmp";

            // The compiler is given file names relative to the directory it runs in, so that no path of
            // this machine ends up in the AIR.
            File.WriteAllText(Path.Combine(request.IntermediateDirectory, stem + SourceExtension), shader.CompiledSource);

            ToolResult result = toolchain.Compile(
                stem + SourceExtension, stem + AirExtension + ".tmp", shader.EntryPoint, shader.FunctionName, shader.CompileOptions, request.IntermediateDirectory);

            if (result.ExitCode == 0 && File.Exists(temporary))
            {
                File.Move(temporary, air, overwrite: true);
            }
            else
            {
                File.Delete(temporary);
                failures[index] = $"SharpCompute: the shader '{shader.TypeName}' could not be precompiled and will be compiled at run time:\n{result.Output}";
                File.WriteAllText(Path.Combine(request.IntermediateDirectory, stem + RejectedExtension), failures[index]);
            }
        });

        foreach (string failure in failures.OfType<string>())
        {
            request.Log(failure);
        }

        return failures.Count(static failure => failure is not null);
    }

    private static string FileStem(ShaderRecord record) =>
        record.FunctionName.Length <= MaxFileStem ? record.FunctionName : record.FunctionName[^MaxFileStem..];

    private static bool Exists(PrecompileRequest request, string stem, string extension) =>
        File.Exists(Path.Combine(request.IntermediateDirectory, stem + extension));

    /// <summary>Removes the files of shaders the assembly no longer has, and what an interrupted run left behind.</summary>
    private static void RemoveStaleFiles(string directory, string[] stems)
    {
        HashSet<string> current = new(stems, StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(directory))
        {
            string extension = Path.GetExtension(file);

            if ((extension != SourceExtension && extension != AirExtension && extension != RejectedExtension) || !current.Contains(Path.GetFileNameWithoutExtension(file)))
            {
                File.Delete(file);
            }
        }
    }

    /// <summary><see cref="File.Delete"/>, which throws when the file's directory is not there, for a file that need not be.</summary>
    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The names the library was last linked from, or null when there is no record of it.</summary>
    private static string[]? ReadState(PrecompileRequest request) =>
        request.StatePath is { } path && File.Exists(path) ? File.ReadAllLines(path) : null;

    /// <summary>Records what the library holds. Always rewritten, so that the file is newer than the assembly it describes.</summary>
    private static void WriteState(PrecompileRequest request, string[] stems)
    {
        if (request.StatePath is { } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllLines(path, stems);
        }
    }

    private static void DeleteState(PrecompileRequest request)
    {
        if (request.StatePath is { } path)
        {
            DeleteFile(path);
        }
    }
}
