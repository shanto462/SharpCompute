using System;
using System.IO;
using System.Linq;

namespace SharpCompute.Build.Tests;

/// <summary>
/// SharpCompute.Build.targets, end to end: <c>dotnet build</c> and <c>dotnet publish</c> of a small
/// application (the Fixture folder: an executable with two shaders that references a library with
/// one) in a temporary folder, and then the application itself, which prints where its pipelines
/// came from. This is the only place the generator, the build step and the runtime's own search for
/// the library are run together as a consumer runs them.
/// </summary>
public class BuildTargetTests
{
    private const string Precompiled = "Fill=Precompiled Scale=Precompiled AddFast=Precompiled value=7";
    private const string CompiledAtRunTime = "Fill=CompiledAtRunTime Scale=CompiledAtRunTime AddFast=CompiledAtRunTime value=7";

    [ToolchainFact]
    public void Build_PrecompilesEveryShader_AndTheLibrariesFollowTheirAssemblies()
    {
        using FixtureProject project = new();
        string output = project.Build();

        Assert.Contains("SharpCompute: Fixture.metallib: 2 precompiled shaders (2 compiled, 0 unchanged).", output);
        Assert.Contains("SharpCompute: Fixture.Library.metallib: 1 precompiled shader (1 compiled, 0 unchanged).", output);

        // The application's library, and the referenced library's, next to the assemblies.
        Assert.Equal(["Fixture.Library.metallib", "Fixture.metallib"], project.Libraries(project.Output));
        Assert.Equal(2, Tool.FunctionNames(Path.Combine(project.Output, "Fixture.metallib")).Count);
        Assert.Single(Tool.FunctionNames(Path.Combine(project.Output, "Fixture.Library.metallib")));
        Assert.Equal(["Fixture.Library.metallib"], project.Libraries(Path.Combine(project.Directory, "Library", "bin", "Debug", "net11.0")));

        project.AssertRuns(Precompiled);
    }

    [ToolchainFact]
    public void ASecondBuild_DoesNothing_AndAnEditThatLeavesTheShadersAloneCompilesNone()
    {
        using FixtureProject project = new();
        project.Build();

        string library = Path.Combine(project.Output, "Fixture.metallib");
        string intermediateLibrary = Path.Combine(project.Intermediate, "Fixture.metallib");
        string state = Path.Combine(project.Intermediate, "Fixture.metallib.state");
        (DateTime library, DateTime intermediate, DateTime state) first = (File.GetLastWriteTimeUtc(library), File.GetLastWriteTimeUtc(intermediateLibrary), File.GetLastWriteTimeUtc(state));

        // Nothing changed: the tool is not even started (it would rewrite the state file).
        string second = project.Build();

        Assert.DoesNotContain("SharpCompute:", second);
        Assert.Equal(first, (File.GetLastWriteTimeUtc(library), File.GetLastWriteTimeUtc(intermediateLibrary), File.GetLastWriteTimeUtc(state)));
        Assert.Equal(["Fixture.Library.metallib", "Fixture.metallib"], project.Libraries(project.Output));

        // The assembly changed, its shaders did not: the tool looks, and compiles and links nothing.
        File.AppendAllText(Path.Combine(project.Directory, "Program.cs"), "\n// An edit that is not in a shader.\n");
        string third = project.Build();

        Assert.DoesNotContain("SharpCompute:", third);
        Assert.Equal(first.library, File.GetLastWriteTimeUtc(library));
        Assert.Equal(first.intermediate, File.GetLastWriteTimeUtc(intermediateLibrary));
        Assert.True(File.GetLastWriteTimeUtc(state) > first.state);

        // A shader changed: that one is compiled, the other is not.
        string program = Path.Combine(project.Directory, "Program.cs");
        File.WriteAllText(program, File.ReadAllText(program).Replace("data[ThreadIds.X] = value;", "data[ThreadIds.X] = value + 0f;", StringComparison.Ordinal));
        string fourth = project.Build();

        Assert.Contains("SharpCompute: Fixture.metallib: 2 precompiled shaders (1 compiled, 1 unchanged).", fourth);
        Assert.DoesNotContain("Fixture.Library.metallib", fourth);
        project.AssertRuns(Precompiled);
    }

    [Fact]
    public void WithoutTheToolchain_TheBuildSucceedsWithAMessage_AndTheApplicationCompilesAtRunTime()
    {
        using FixtureProject project = new();
        string output = project.Build("-p:SharpComputeXcrunPath=/nonexistent/xcrun");

        // A message, not a warning: the build of a project that treats warnings as errors goes through.
        Assert.Contains("SharpCompute: Fixture.metallib was not built, because the Metal toolchain is not available", output);
        Assert.Contains("SharpCompute: Fixture.Library.metallib was not built, because the Metal toolchain is not available", output);
        Assert.Contains("0 Warning(s)", output);
        Assert.Contains("0 Error(s)", output);
        Assert.Empty(project.Libraries(project.Output));
        Assert.Empty(project.Libraries(project.Intermediate));

        project.AssertRuns(CompiledAtRunTime);

        if (!ToolchainFactAttribute.IsAvailable)
        {
            return;
        }

        // The case is looked at again by the next build: with the toolchain there, the libraries appear.
        string later = project.Build();

        Assert.Contains("SharpCompute: Fixture.metallib: 2 precompiled shaders", later);
        Assert.Equal(["Fixture.Library.metallib", "Fixture.metallib"], project.Libraries(project.Output));
        project.AssertRuns(Precompiled);
    }

    [Fact]
    public void WithoutTheBuildTool_TheBuildSucceedsWithAMessage()
    {
        using FixtureProject project = new();
        string output = project.Build("-p:SharpComputeBuildToolPath=/nonexistent/SharpCompute.Build.dll");

        Assert.Contains("SharpCompute: Fixture.metallib was not built, because the build tool was not found (/nonexistent/SharpCompute.Build.dll)", output);
        Assert.Contains("0 Warning(s)", output);
        Assert.Contains("0 Error(s)", output);
        Assert.Empty(project.Libraries(project.Output));

        project.AssertRuns(CompiledAtRunTime);
    }

    [ToolchainFact]
    public void TurnedOff_NothingIsPrecompiled_AndTheLibrariesOfAnEarlierBuildAreRemoved()
    {
        using FixtureProject project = new();
        project.Build();
        Assert.Equal(["Fixture.Library.metallib", "Fixture.metallib"], project.Libraries(project.Output));

        string output = project.Build("-p:SharpComputePrecompileShaders=false");

        Assert.DoesNotContain("SharpCompute:", output);
        Assert.Empty(project.Libraries(project.Output));
        Assert.Empty(project.Libraries(project.Intermediate));
        project.AssertRuns(CompiledAtRunTime);
    }

    [ToolchainFact]
    public void Publish_IncludesTheLibraries_AndASingleFileApplicationFindsThemNextToItself()
    {
        using FixtureProject project = new();

        string published = Path.Combine(project.Directory, "published");
        project.DotNet("publish", "Fixture.csproj", "-c", "Release", "-o", published);
        Assert.Equal(["Fixture.Library.metallib", "Fixture.metallib"], project.Libraries(published));

        // Published again without building: the libraries are still collected.
        string again = Path.Combine(project.Directory, "published-again");
        project.DotNet("publish", "Fixture.csproj", "-c", "Release", "--no-build", "-o", again);
        Assert.Equal(["Fixture.Library.metallib", "Fixture.metallib"], project.Libraries(again));

        // In a single-file application the assemblies have no location, so the runtime looks in the
        // application's base directory, where the publish put the libraries.
        string singleFile = Path.Combine(project.Directory, "single-file");
        project.DotNet("publish", "Fixture.csproj", "-c", "Release", "-r", "osx-arm64", "--self-contained", "false", "-p:PublishSingleFile=true", "-o", singleFile);

        Assert.Equal(["Fixture.Library.metallib", "Fixture.metallib"], project.Libraries(singleFile));
        Assert.False(File.Exists(Path.Combine(singleFile, "Fixture.dll")));

        (int exitCode, string output) = Tool.Run(Path.Combine(singleFile, "Fixture"), singleFile);
        FixtureProject.AssertOutput(Precompiled, exitCode, output);
    }

    /// <summary>A copy of the Fixture folder that uses the binaries next to this test assembly.</summary>
    private sealed class FixtureProject : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();

        public FixtureProject()
        {
            string binaries = AppContext.BaseDirectory;
            Copy(Path.Combine(binaries, "Fixture"), Directory);

            // The projects are stored as .txt so that nothing takes them for projects of the repository.
            File.Move(Path.Combine(Directory, "Fixture.csproj.txt"), Path.Combine(Directory, "Fixture.csproj"));
            File.Move(Path.Combine(Directory, "Library", "Fixture.Library.csproj.txt"), Path.Combine(Directory, "Library", "Fixture.Library.csproj"));

            File.WriteAllText(
                Path.Combine(Directory, "Directory.Build.props"),
                $"<Project><PropertyGroup><SharpComputeTestBinaries>{Path.TrimEndingDirectorySeparator(binaries)}/</SharpComputeTestBinaries></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(Directory, "Directory.Build.targets"), "<Project />");
        }

        public string Directory => _directory.Path;

        public string Output => Path.Combine(Directory, "bin", "Debug", "net11.0");

        public string Intermediate => Path.Combine(Directory, "obj", "Debug", "net11.0");

        /// <summary><c>dotnet build</c> of the application; returns what the build printed.</summary>
        public string Build(params string[] arguments) => DotNet(["build", "Fixture.csproj", .. arguments]);

        public string DotNet(params string[] arguments)
        {
            (int exitCode, string output) = Tool.Run(Tool.DotNet, Directory, [.. arguments, "-nodeReuse:false", "--nologo"]);
            Assert.True(exitCode == 0, $"dotnet {string.Join(' ', arguments)} failed:\n{output}");
            return output;
        }

        /// <summary>The names of the Metal libraries directly in <paramref name="directory"/>.</summary>
        public string[] Libraries(string directory) =>
            System.IO.Directory.Exists(directory)
                ? [.. System.IO.Directory.EnumerateFiles(directory, "*.metallib").Select(Path.GetFileName).Order(StringComparer.Ordinal)!]
                : [];

        /// <summary>Runs the built application and compares the line it prints.</summary>
        public void AssertRuns(string expected)
        {
            (int exitCode, string output) = Tool.Run(Tool.DotNet, Output, Path.Combine(Output, "Fixture.dll"));
            AssertOutput(expected, exitCode, output);
        }

        public static void AssertOutput(string expected, int exitCode, string output)
        {
            // Exit code 2 is the application saying that this machine has no Metal device: then there
            // is nothing to dispatch, and what the build produced has been checked already.
            if (exitCode == 2 && output.Contains("no Metal device", StringComparison.Ordinal))
            {
                return;
            }

            Assert.True(exitCode == 0, output);
            Assert.Equal(expected, output.Trim());
        }

        public void Dispose() => _directory.Dispose();

        private static void Copy(string source, string destination)
        {
            System.IO.Directory.CreateDirectory(destination);

            foreach (string file in System.IO.Directory.EnumerateFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }

            foreach (string directory in System.IO.Directory.EnumerateDirectories(source))
            {
                Copy(directory, Path.Combine(destination, Path.GetFileName(directory)));
            }
        }
    }
}
