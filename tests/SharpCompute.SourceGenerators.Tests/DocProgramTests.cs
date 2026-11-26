using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// The examples the documentation presents as a whole <c>Program.cs</c>, read from the Markdown as it
/// is printed and compiled as an application with the generator: a reader who pastes one gets a
/// program that builds. Top-level statements after a type declaration (CS8803) are the mistake this
/// guards against.
/// </summary>
public sealed partial class DocProgramTests
{
    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/getting-started.md")]
    public void TheFirstCSharpExample_CompilesAsAProgram(string page)
    {
        string root = FindRoot() ?? throw new InvalidOperationException("SharpCompute.slnx not found above the test output.");
        Match block = CSharpBlockPattern().Match(File.ReadAllText(Path.Combine(root, page)));
        Assert.True(block.Success, $"{page} has no ```csharp block");

        CSharpCompilation compilation = GeneratorHarness.CreateCompilation(block.Groups[1].Value);
        compilation = compilation.WithOptions(compilation.Options.WithOutputKind(OutputKind.ConsoleApplication));
        GeneratorHarness.CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out var generatorDiagnostics);

        Diagnostic[] problems = [.. generatorDiagnostics.Concat(output.GetDiagnostics()).Where(static d => d.Severity >= DiagnosticSeverity.Warning)];
        Assert.True(problems.Length == 0, $"The first C# example of {page} does not build:\n" + string.Join("\n", problems.Select(static d => d.ToString())));
    }

    [GeneratedRegex(@"```csharp\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex CSharpBlockPattern();

    private static string? FindRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SharpCompute.slnx")))
            {
                return dir.FullName;
            }
        }

        return null;
    }
}
