using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpCompute.Build.Tests;

/// <summary>
/// <see cref="ShaderPrecompiler"/>: from an assembly to a Metal library, incrementally, and without a
/// library when it cannot be made. The assembly is this one (Shaders.cs): seven shaders that compile
/// and one the Metal compiler rejects.
/// </summary>
public class ShaderPrecompilerTests
{
    private const int Recorded = 8;
    private const int Compiling = 7;

    [ToolchainFact]
    public void Builds_OneLibrary_WithEveryShaderThatCompiles_UnderTheNameTheRuntimeAsksFor()
    {
        using Workspace workspace = new();
        PrecompileResult result = workspace.Run();

        Assert.Equal(new PrecompileResult(PrecompileStatus.Built, Recorded, Compiled: Compiling, Reused: 0, Rejected: 1), result);

        string[] expected = [.. ShaderRecordReader.Read(ShaderRecordReaderTests.ThisAssembly)
            .Where(static record => record.EntryPoint != "Rejected_Kernel")
            .Select(static record => record.FunctionName)
            .Order(StringComparer.Ordinal)];

        Assert.Equal(Compiling, expected.Length);
        Assert.Equal(expected, Tool.FunctionNames(workspace.Library));

        // The state is what the library was linked from, and newer than nothing is left to do.
        Assert.Equal(expected, File.ReadAllLines(workspace.State));

        // The record the reader could not use is passed on to the build, with the tool's prefix.
        Assert.Contains(workspace.Log, static line => line.StartsWith("SharpCompute: 'SharpCompute.Build.Tests.ComputedSource' has a shader record", StringComparison.Ordinal));
        Assert.Contains(workspace.Log, static line => line.Contains("Shaders.metallib: 7 precompiled shaders (7 compiled, 0 unchanged, 1 rejected and left to run time).", StringComparison.Ordinal));
    }

    [ToolchainFact]
    public void ARejectedShader_IsReportedWithWhatTheCompilerSaid_AndLeftOut()
    {
        using Workspace workspace = new();
        workspace.Run();

        string report = Assert.Single(workspace.Log, static line => line.Contains("could not be precompiled", StringComparison.Ordinal));

        Assert.Contains("'SharpCompute.Build.Tests.Rejected'", report);
        Assert.Contains("compiled at run time", report);
        Assert.Contains("this_is_not_declared", report);
        Assert.DoesNotContain(Tool.FunctionNames(workspace.Library), static name => name.StartsWith("Rejected_Kernel", StringComparison.Ordinal));
    }

    [ToolchainFact]
    public void ASecondRun_CompilesAndLinksNothing()
    {
        using Workspace workspace = new();
        workspace.Run();

        DateTime library = File.GetLastWriteTimeUtc(workspace.Library);
        Dictionary<string, DateTime> intermediates = workspace.IntermediateTimes();
        File.SetLastWriteTimeUtc(workspace.State, DateTime.UtcNow.AddMinutes(-5));
        DateTime state = File.GetLastWriteTimeUtc(workspace.State);

        // With an xcrun that does not exist: an up-to-date run must not even look for the toolchain.
        PrecompileResult second = workspace.Run(xcrun: "/nonexistent/xcrun");

        Assert.Equal(new PrecompileResult(PrecompileStatus.UpToDate, Recorded, Compiled: 0, Reused: Compiling, Rejected: 1), second);
        Assert.Equal(library, File.GetLastWriteTimeUtc(workspace.Library));
        Assert.Equal(intermediates, workspace.IntermediateTimes());

        // The state file is rewritten, which is what tells the build that the assembly was looked at.
        Assert.True(File.GetLastWriteTimeUtc(workspace.State) > state);

        // The rejected shader is still reported, from what the first run kept; nothing else is said.
        Assert.Contains("this_is_not_declared", Assert.Single(workspace.Messages));
    }

    [ToolchainFact]
    public void OnlyAShaderWhoseAirIsMissing_IsCompiledAgain_AndTheLibraryIsLinkedAgain()
    {
        using Workspace workspace = new();
        workspace.Run();

        // What a changed shader looks like to the next run: no AIR under its (new) name.
        string air = Directory.EnumerateFiles(workspace.Intermediate, "*FillFast__sc*.air").Single();
        Dictionary<string, DateTime> before = workspace.IntermediateTimes();
        File.Delete(air);

        PrecompileResult result = workspace.Run();

        Assert.Equal(new PrecompileResult(PrecompileStatus.Built, Recorded, Compiled: 1, Reused: Compiling - 1, Rejected: 1), result);
        Assert.True(File.Exists(air));
        Assert.Equal(Compiling, Tool.FunctionNames(workspace.Library).Count);

        // Every other shader's AIR is the file it was.
        foreach ((string file, DateTime time) in workspace.IntermediateTimes().Where(pair => pair.Key.EndsWith(".air", StringComparison.Ordinal) && pair.Key != air))
        {
            Assert.Equal(before[file], time);
        }
    }

    [ToolchainFact]
    public void ALibraryThatWasDeleted_IsLinkedAgain_FromTheAirThatIsThere()
    {
        using Workspace workspace = new();
        workspace.Run();
        File.Delete(workspace.Library);

        PrecompileResult result = workspace.Run();

        Assert.Equal(new PrecompileResult(PrecompileStatus.Built, Recorded, Compiled: 0, Reused: Compiling, Rejected: 1), result);
        Assert.Equal(Compiling, Tool.FunctionNames(workspace.Library).Count);
    }

    [ToolchainFact]
    public void FilesOfShadersTheAssemblyNoLongerHas_AreRemoved()
    {
        using Workspace workspace = new();
        Directory.CreateDirectory(workspace.Intermediate);

        string[] stale =
        [
            Path.Combine(workspace.Intermediate, "Gone__sc00000000000000000000000000000000.air"),
            Path.Combine(workspace.Intermediate, "Gone__sc00000000000000000000000000000000.metal"),
            Path.Combine(workspace.Intermediate, "Gone__sc00000000000000000000000000000000.rejected"),
            Path.Combine(workspace.Intermediate, "Interrupted.air.tmp"),
        ];

        foreach (string file in stale)
        {
            File.WriteAllText(file, "left by an earlier build");
        }

        workspace.Run();

        Assert.All(stale, static file => Assert.False(File.Exists(file), file));

        // Per shader: its source and its AIR, or its source and why it was rejected.
        string[] extensions = [.. Directory.EnumerateFiles(workspace.Intermediate).Select(Path.GetExtension).Order(StringComparer.Ordinal)!];
        Assert.Equal(Compiling, extensions.Count(static extension => extension == ".air"));
        Assert.Equal(Recorded, extensions.Count(static extension => extension == ".metal"));
        Assert.Equal(1, extensions.Count(static extension => extension == ".rejected"));
        Assert.Equal(Compiling + Recorded + 1, extensions.Length);
    }

    [ToolchainFact]
    public void TheSourceThatIsCompiled_IsTheRuntimesText_AndTheLibraryHoldsNoPathOfThisMachine()
    {
        using Workspace workspace = new();
        workspace.Run();

        ShaderRecord exact = ShaderRecordReader.Read(ShaderRecordReaderTests.ThisAssembly).Single(static record => record.TypeName.EndsWith(".MulAddExact", StringComparison.Ordinal));
        Assert.Equal(exact.CompiledSource, File.ReadAllText(Path.Combine(workspace.Intermediate, exact.FunctionName + ".metal")));
        Assert.StartsWith("#pragma clang fp contract(off)\n#include <metal_stdlib>", exact.CompiledSource);

        // The compiler is run inside the intermediate directory with relative names.
        string library = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(workspace.Library));
        Assert.DoesNotContain(workspace.Directory.Path, library);
        Assert.DoesNotContain(Path.GetFileName(workspace.Directory.Path), library);
    }

    [ToolchainFact]
    public void OneCompilerAtATime_GivesTheSameLibraryContents()
    {
        using Workspace workspace = new();
        Assert.Equal(PrecompileStatus.Built, workspace.Run(parallelism: 1).Status);

        using Workspace other = new();
        other.Run(parallelism: 4);

        Assert.Equal(Tool.FunctionNames(other.Library), Tool.FunctionNames(workspace.Library));
    }

    [Fact]
    public void WithoutTheToolchain_ThereIsNoLibrary_AndTheBuildIsTold()
    {
        using Workspace workspace = new();

        // A library and a state file an earlier build left: neither may survive, or the application
        // would run with a library this build did not make.
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.Library)!);
        File.WriteAllText(workspace.Library, "an earlier build's library");
        File.WriteAllText(workspace.State, "an earlier build's state");

        PrecompileResult result = workspace.Run(xcrun: "/nonexistent/xcrun");

        Assert.Equal(new PrecompileResult(PrecompileStatus.ToolchainUnavailable, Recorded, 0, 0, 0), result);
        Assert.False(File.Exists(workspace.Library));
        Assert.False(File.Exists(workspace.State));

        string message = Assert.Single(workspace.Messages);
        Assert.Contains("Shaders.metallib was not built, because the Metal toolchain is not available", message);
        Assert.Contains("'/nonexistent/xcrun' could not be started", message);
        Assert.Contains("compiled at run time", message);
        Assert.Contains("xcodebuild -downloadComponent MetalToolchain", message);
    }

    [Fact]
    public void AnXcrunThatFails_IsNoToolchainEither()
    {
        // What Xcode without the Metal Toolchain component does: xcrun is there, `metal` exits non-zero.
        // The stand-in is a shell script, so this is a test for a Unix.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Workspace workspace = new();
        string xcrun = workspace.Directory.File("xcrun");
        File.WriteAllText(xcrun, "#!/bin/sh\necho \"error: cannot execute tool 'metal' due to missing Metal Toolchain\" >&2\nexit 72\n");
        File.SetUnixFileMode(xcrun, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        PrecompileResult result = workspace.Run(xcrun: xcrun);

        Assert.Equal(PrecompileStatus.ToolchainUnavailable, result.Status);
        Assert.False(File.Exists(workspace.Library));
        Assert.Contains("cannot execute tool 'metal' due to missing Metal Toolchain", Assert.Single(workspace.Messages));
    }

    [Fact]
    public void AnAssemblyWithoutShaders_GetsNoLibrary_AndNeedsNoToolchain()
    {
        using Workspace workspace = new(assembly: typeof(GraphicsDevice).Assembly.Location);
        Directory.CreateDirectory(workspace.Intermediate);
        File.WriteAllText(Path.Combine(workspace.Intermediate, "Old__sc0.air"), "left by an earlier build");
        File.WriteAllText(workspace.Library, "left by an earlier build");

        PrecompileResult result = workspace.Run(xcrun: "/nonexistent/xcrun");

        Assert.Equal(new PrecompileResult(PrecompileStatus.NoShaders, 0, 0, 0, 0), result);
        Assert.False(File.Exists(workspace.Library));
        Assert.False(Directory.Exists(workspace.Intermediate));
        Assert.Empty(workspace.Log);

        // Written, and empty: the build can skip the tool until the assembly changes.
        Assert.Empty(File.ReadAllLines(workspace.State));
    }

    [ToolchainFact]
    public void TheCommandLine_RunsThePrecompiler_AndFailsOnlyForMistakes()
    {
        using Workspace workspace = new();
        string tool = typeof(ShaderPrecompiler).Assembly.Location;

        (int exitCode, string output) = Tool.Run(
            Tool.DotNet, workspace.Directory.Path, "exec", tool, "--assembly", ShaderRecordReaderTests.ThisAssembly, "--output", workspace.Library,
            "--intermediate", workspace.Intermediate, "--state", workspace.State, "--xcrun", "xcrun", "--parallelism", "2");

        Assert.True(exitCode == 0, output);
        Assert.Contains("Shaders.metallib: 7 precompiled shaders", output);
        Assert.Equal(Compiling, Tool.FunctionNames(workspace.Library).Count);

        // An empty --parallelism is what the targets pass when the property is not set.
        (exitCode, output) = Tool.Run(
            Tool.DotNet, workspace.Directory.Path, "exec", tool, "--assembly", ShaderRecordReaderTests.ThisAssembly, "--output", workspace.Library,
            "--intermediate", workspace.Intermediate, "--state", workspace.State, "--xcrun", "/nonexistent/xcrun", "--parallelism", string.Empty);

        Assert.True(exitCode == 0, output);

        // Without the toolchain on a fresh folder: a message, and still success.
        using Workspace fresh = new();
        (exitCode, output) = Tool.Run(
            Tool.DotNet, fresh.Directory.Path, "exec", tool, "--assembly", ShaderRecordReaderTests.ThisAssembly, "--output", fresh.Library,
            "--intermediate", fresh.Intermediate, "--xcrun", "/nonexistent/xcrun");

        Assert.True(exitCode == 0, output);
        Assert.Contains("the Metal toolchain is not available", output);
        Assert.False(File.Exists(fresh.Library));

        Assert.Equal(2, Tool.Run(Tool.DotNet, fresh.Directory.Path, "exec", tool, "--assembly", ShaderRecordReaderTests.ThisAssembly).ExitCode);
        Assert.Equal(2, Tool.Run(Tool.DotNet, fresh.Directory.Path, "exec", tool, "--assembly", "a.dll", "--output", "a.metallib", "--intermediate", "obj", "--parallelism", "many").ExitCode);
        Assert.Equal(1, Tool.Run(Tool.DotNet, fresh.Directory.Path, "exec", tool, "--assembly", fresh.Directory.File("Missing.dll"), "--output", fresh.Library, "--intermediate", fresh.Intermediate).ExitCode);
    }

    /// <summary>A temporary folder to precompile this assembly's shaders into, and what the run logged.</summary>
    private sealed class Workspace(string? assembly = null) : IDisposable
    {
        public TemporaryDirectory Directory { get; } = new();

        public string Library => Directory.File("Shaders.metallib");

        public string Intermediate => Directory.File("shaders");

        public string State => Directory.File("Shaders.metallib.state");

        /// <summary>The lines of the most recent run.</summary>
        public List<string> Log { get; } = [];

        /// <summary>
        /// <see cref="Log"/> without the line every run of this assembly logs: the record whose source
        /// is not a literal (ComputedSource), which the reader skips.
        /// </summary>
        public IEnumerable<string> Messages => Log.Where(static line => !line.Contains("'SharpCompute.Build.Tests.ComputedSource' has a shader record", StringComparison.Ordinal));

        public PrecompileResult Run(string xcrun = "xcrun", int parallelism = 0)
        {
            Log.Clear();

            return ShaderPrecompiler.Run(new PrecompileRequest
            {
                AssemblyPath = assembly ?? ShaderRecordReaderTests.ThisAssembly,
                OutputPath = Library,
                IntermediateDirectory = Intermediate,
                StatePath = State,
                Xcrun = xcrun,
                MaxParallelism = parallelism,
                Log = Log.Add,
            });
        }

        public Dictionary<string, DateTime> IntermediateTimes() =>
            System.IO.Directory.EnumerateFiles(Intermediate).ToDictionary(static file => file, File.GetLastWriteTimeUtc);

        public void Dispose() => Directory.Dispose();
    }
}
