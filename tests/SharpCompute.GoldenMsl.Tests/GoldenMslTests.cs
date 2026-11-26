using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SharpCompute.Build;
using Xunit.Abstractions;

namespace SharpCompute.GoldenMsl.Tests;

/// <summary>
/// The regression net for the generator's output: the MSL of every shader in the solution is
/// compared, byte for byte, with its snapshot in <c>tests/golden-msl</c>. A change that is not meant
/// to alter the emitted MSL passes without touching a snapshot; one that is meant to regenerates
/// them (<c>scripts/update-golden-msl.sh</c>) and shows exactly what it altered as a diff.
/// </summary>
public class GoldenMslTests(ITestOutputHelper output)
{
    /// <summary>How many differing shaders a failure describes in full; a sweep can change hundreds.</summary>
    private const int MaxDetailed = 10;

    private const string HowToUpdate =
        "If the change is intended, regenerate the snapshots with scripts/update-golden-msl.sh and review the diff of " + GoldenCorpus.SnapshotFolder + ".";

    [Fact]
    public void EmittedMsl_MatchesTheSnapshots()
    {
        IReadOnlyList<GoldenShader> shaders = GoldenCorpus.Read();

        if (GoldenCorpus.IsUpdateRequested)
        {
            (int written, int deleted) = GoldenCorpus.Update(shaders);
            output.WriteLine($"{GoldenCorpus.SnapshotFolder}: {shaders.Count} shaders, {written} snapshots written, {deleted} deleted.");
            return;
        }

        string? failure = Compare(shaders, GoldenCorpus.ReadSnapshot, GoldenCorpus.SnapshotFiles());
        Assert.True(failure is null, failure);
    }

    [Fact]
    public void Corpus_CoversEveryProjectOfTheSolution()
    {
        // The projects that are read by design are every project but these: the generator and the
        // build tool have no shaders of their own, and this project only reads the others.
        string[] notRead = ["SharpCompute.SourceGenerators", "SharpCompute.Build", "SharpCompute.GoldenMsl.Tests"];

        string[] projects = [.. XDocument.Load(Path.Combine(GoldenCorpus.Root, "SharpCompute.slnx"))
            .Descendants("Project")
            .Select(static project => Path.GetFileNameWithoutExtension(((string?)project.Attribute("Path") ?? string.Empty).Replace('\\', '/')))
            .Except(notRead)
            .Order(StringComparer.Ordinal)];

        string[] assemblies = [.. GoldenCorpus.AssemblyPaths().Select(static path => Path.GetFileNameWithoutExtension(path))];

        Assert.True(projects.Length >= 19, $"only {projects.Length} projects found in SharpCompute.slnx: is its format still recognized?");
        Assert.Equal(projects, assemblies);
    }

    [Fact]
    public void Corpus_HoldsTheSolutionsShaders_AndNothingOfTheMachine()
    {
        IReadOnlyList<GoldenShader> shaders = GoldenCorpus.Read();

        // A reader that stopped recognizing the generator's output would find nothing, and nothing matches nothing.
        Assert.True(shaders.Count >= 300, $"only {shaders.Count} shaders found");

        foreach (string assembly in (string[])["SharpCompute.Tests", "SharpCompute.Benchmarks", "SharpCompute.Samples.ComputeLab", "SharpCompute.Samples.Shaders"])
        {
            Assert.Contains(shaders, shader => shader.Assembly == assembly);
        }

        Assert.Contains(shaders, static shader => shader.TypeName.Contains("Conformance", StringComparison.Ordinal));

        // In order and by name alone, so two runs (on two machines) list the same files.
        Assert.Equal(shaders.Select(static shader => shader.RelativePath).Order(StringComparer.Ordinal), shaders.Select(static shader => shader.RelativePath));

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (GoldenShader shader in shaders)
        {
            Assert.Matches(@"^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+\.metal$", shader.RelativePath);
            Assert.StartsWith("#include <metal_stdlib>", shader.Text[(shader.Text.IndexOf('\n') + 1)..], StringComparison.Ordinal);

            // Git stores the snapshots with LF endings, and a path of this checkout would differ on the next machine.
            Assert.False(shader.Text.Contains('\r'), $"{shader.TypeName}: the MSL has a carriage return");
            Assert.False(shader.Text.Contains(GoldenCorpus.Root, StringComparison.Ordinal), $"{shader.TypeName}: the MSL names the checkout folder");
            Assert.False(home.Length > 1 && shader.Text.Contains(home, StringComparison.Ordinal), $"{shader.TypeName}: the MSL names the home folder");
        }
    }

    [Fact]
    public void Corpus_ReadTwice_IsIdentical()
    {
        Assert.Equal(GoldenCorpus.Read(), GoldenCorpus.Read());
    }

    [Theory]
    [InlineData("My.App", "My.App.Shaders.Blur", "My.App/My.App.Shaders.Blur.metal")]
    [InlineData("My.App", "Outer.Inner", "My.App/Outer.Inner.metal")]
    [InlineData("My-App_1", "N.Generic`1", "My-App_1/N.Generic_1.metal")]
    [InlineData("App", "N.<Shader>d__0", "App/N._Shader_d__0.metal")]
    [InlineData("App", "N.Ünï/..\\Code", "App/N._n__.._Code.metal")]
    public void RelativePath_IsTheAssemblyAndTheSanitizedTypeName(string assembly, string typeName, string expected)
    {
        Assert.Equal(expected, GoldenCorpus.RelativePath(assembly, typeName));
    }

    [Theory]
    [InlineData(0, "None")]
    [InlineData(1, "FastMath")]
    [InlineData(2, "NoFpContract")]
    [InlineData(3, "FastMath, NoFpContract")]
    [InlineData(6, "NoFpContract, 4")]
    public void Render_IsAHeaderLine_TheExactMsl_AndALineFeed(int options, string expectedOptions)
    {
        ShaderRecord record = new("N.Shader", "N_Shader", options, "kernel void N_Shader()\n{\n}");

        Assert.Equal(
            $"// N.Shader (App): entry point N_Shader, compile options {expectedOptions}\nkernel void N_Shader()\n{{\n}}\n",
            GoldenCorpus.Render("App", record));
    }

    [Fact]
    public void Render_KeepsATrailingLineFeedOfTheMslApart()
    {
        // "X" and "X\n" are different emitted texts, so they are different snapshots.
        Assert.NotEqual(
            GoldenCorpus.Render("App", new ShaderRecord("N.S", "N_S", 0, "X")),
            GoldenCorpus.Render("App", new ShaderRecord("N.S", "N_S", 0, "X\n")));
    }

    [Fact]
    public void FirstDifference_IsNullForEqualTexts()
    {
        Assert.Null(GoldenCorpus.FirstDifference("a\nb\n", "a\nb\n"));
    }

    [Fact]
    public void FirstDifference_GivesTheLineAndWhatFollowsOnBothSides()
    {
        (int line, string[] expected, string[] actual) = GoldenCorpus.FirstDifference("a\nb\nc\nd\ne\nf\n", "a\nb\nC\nd\ne\nf\n", context: 2)!.Value;

        Assert.Equal(3, line);
        Assert.Equal(["c", "d"], expected);
        Assert.Equal(["C", "d"], actual);
    }

    [Fact]
    public void FirstDifference_WhenOneTextIsAPrefixOfTheOther_StartsWhereItEnds()
    {
        (int line, string[] expected, string[] actual) = GoldenCorpus.FirstDifference("a\nb", "a\nb\nc\nd")!.Value;

        Assert.Equal(3, line);
        Assert.Empty(expected);
        Assert.Equal(["c", "d"], actual);
    }

    [Fact]
    public void Compare_PassesWhenEverySnapshotMatches()
    {
        GoldenShader[] shaders = [Shader("N.A", "one\n"), Shader("N.B", "two\n")];

        Assert.Null(Compare(shaders, Snapshots(("App/N.A.metal", "one\n"), ("App/N.B.metal", "two\n")), ["App/N.A.metal", "App/N.B.metal"]));
    }

    [Fact]
    public void Compare_NamesTheShaderAndShowsTheFirstDifferingLines()
    {
        GoldenShader[] shaders = [Shader("N.A", "one\n"), Shader("N.B", "x\ny = 2;\nz\n")];

        string failure = Compare(shaders, Snapshots(("App/N.A.metal", "one\n"), ("App/N.B.metal", "x\ny = 1;\nz\n")), ["App/N.A.metal", "App/N.B.metal"])!;

        Assert.Contains("1 of 2 shaders", failure, StringComparison.Ordinal);
        Assert.Contains("N.B (App)", failure, StringComparison.Ordinal);
        Assert.Contains("tests/golden-msl/App/N.B.metal, line 2", failure, StringComparison.Ordinal);
        Assert.Contains("- y = 1;", failure, StringComparison.Ordinal);
        Assert.Contains("+ y = 2;", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("N.A", failure, StringComparison.Ordinal);
        Assert.Contains("scripts/update-golden-msl.sh", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_ReportsAShaderWithoutASnapshot_AndASnapshotWithoutAShader()
    {
        GoldenShader[] shaders = [Shader("N.New", "one\n")];

        string failure = Compare(shaders, Snapshots(("App/N.Gone.metal", "old\n")), ["App/N.Gone.metal"])!;

        Assert.Contains("N.New (App) has no snapshot", failure, StringComparison.Ordinal);
        Assert.Contains("tests/golden-msl/App/N.Gone.metal is the snapshot of a shader that no longer exists", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_DescribesTheFirstFewDifferences_AndCountsTheRest()
    {
        GoldenShader[] shaders = [.. Enumerable.Range(0, MaxDetailed + 5).Select(static i => Shader($"N.S{i:00}", "new\n"))];
        string[] files = [.. shaders.Select(static shader => shader.RelativePath)];

        string failure = Compare(shaders, static _ => "old\n", files)!;

        Assert.Contains($"{MaxDetailed + 5} of {MaxDetailed + 5} shaders", failure, StringComparison.Ordinal);
        Assert.Contains("N.S09 (App)", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("N.S10 (App)", failure, StringComparison.Ordinal);
        Assert.Contains("and 5 more", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_ShadersWithoutASnapshot_DoNotUseUpTheDescribedDifferences()
    {
        // More new shaders than the cap, sorted ahead of the one shader whose MSL changed.
        GoldenShader[] shaders = [.. Enumerable.Range(0, MaxDetailed + 2).Select(static i => Shader($"N.New{i:00}", "new\n")), Shader("N.Old", "x = 2;\n")];

        string failure = Compare(shaders, static path => path == "App/N.Old.metal" ? "x = 1;\n" : null, ["App/N.Old.metal"])!;

        Assert.Contains($"{MaxDetailed + 3} of {MaxDetailed + 3} shaders", failure, StringComparison.Ordinal);
        Assert.Contains($"N.New{MaxDetailed + 1:00} (App) has no snapshot", failure, StringComparison.Ordinal);
        Assert.Contains("N.Old (App) differs from tests/golden-msl/App/N.Old.metal, line 1", failure, StringComparison.Ordinal);
        Assert.Contains("- x = 1;", failure, StringComparison.Ordinal);
        Assert.Contains("+ x = 2;", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("more shaders", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// Null when the snapshots are exactly the shaders; otherwise the failure message: each shader
    /// that differs with its file and first differing lines, each shader without a snapshot and each
    /// snapshot without a shader, and how to regenerate.
    /// </summary>
    private static string? Compare(IReadOnlyList<GoldenShader> shaders, Func<string, string?> readSnapshot, IReadOnlyList<string> snapshotFiles)
    {
        List<string> problems = [];

        // Counted apart: only shaders whose text differs are capped, so a batch of new shaders
        // (each one line here) never uses up the detailed diffs.
        int missing = 0;
        int changed = 0;

        foreach (GoldenShader shader in shaders)
        {
            string file = $"{GoldenCorpus.SnapshotFolder}/{shader.RelativePath}";

            if (readSnapshot(shader.RelativePath) is not { } snapshot)
            {
                missing++;
                problems.Add($"{shader.TypeName} ({shader.Assembly}) has no snapshot: {file} does not exist.");
            }
            else if (GoldenCorpus.FirstDifference(snapshot, shader.Text) is var (line, expected, actual))
            {
                if (++changed > MaxDetailed)
                {
                    continue;
                }

                StringBuilder message = new($"{shader.TypeName} ({shader.Assembly}) differs from {file}, line {line}:");

                foreach (string text in expected)
                {
                    message.Append("\n    - ").Append(text);
                }

                foreach (string text in actual)
                {
                    message.Append("\n    + ").Append(text);
                }

                problems.Add(message.ToString());
            }
        }

        HashSet<string> wanted = new(shaders.Select(static shader => shader.RelativePath), StringComparer.OrdinalIgnoreCase);
        string[] stale = [.. snapshotFiles.Where(file => !wanted.Contains(file))];
        problems.AddRange(stale.Select(static file => $"{GoldenCorpus.SnapshotFolder}/{file} is the snapshot of a shader that no longer exists."));

        if (problems.Count == 0)
        {
            return null;
        }

        int different = missing + changed;
        int undescribed = changed - MaxDetailed;
        string rest = undescribed > 0 ? $"\n\n... and {undescribed} more shaders that differ from their snapshots." : string.Empty;

        return $"The emitted MSL of {different} of {shaders.Count} shaders is not what {GoldenCorpus.SnapshotFolder} holds"
            + (stale.Length > 0 ? $", and {stale.Length} snapshots have no shader" : string.Empty)
            + $". (- snapshot, + emitted)\n\n{string.Join("\n\n", problems)}{rest}\n\n{HowToUpdate}";
    }

    private static GoldenShader Shader(string typeName, string text) => new("App", typeName, GoldenCorpus.RelativePath("App", typeName), text);

    private static Func<string, string?> Snapshots(params (string Path, string Text)[] files) =>
        path => files.FirstOrDefault(file => file.Path == path).Text;
}
