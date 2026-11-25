using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>The pipeline must be cacheable: equatable, symbol-free models and no work for unrelated edits.</summary>
public class IncrementalTests
{
    private const string ShaderSource = """
        using SharpCompute;

        namespace App;

        [GeneratedComputeShaderDescriptor]
        public readonly partial struct Scale : IComputeShader
        {
            public readonly ReadWriteBuffer<float> buffer;
            public readonly float factor;

            public void Execute() => buffer[ThreadIds.X] *= Twice(factor);

            private static float Twice(float value) => value * 2;
        }
        """;

    private const string UnrelatedSource = """
        namespace App;

        public static class Unrelated
        {
            public static int Answer() => 42;
        }
        """;

    private static IncrementalStepRunReason[] Reasons(GeneratorRunResult result, string stepName) =>
        result.TrackedSteps[stepName].SelectMany(static step => step.Outputs).Select(static output => output.Reason).ToArray();

    private static IncrementalStepRunReason[] OutputReasons(GeneratorRunResult result) =>
        result.TrackedOutputSteps.SelectMany(static pair => pair.Value).SelectMany(static step => step.Outputs).Select(static output => output.Reason).ToArray();

    private static ShaderResult[] Models(GeneratorRunResult result) =>
        result.TrackedSteps[ComputeShaderGenerator.ShaderResultStep].SelectMany(static step => step.Outputs).Select(static output => (ShaderResult)output.Value).ToArray();

    [Fact]
    public void UnrelatedEdit_ReusesTheModelAndSkipsTheOutput()
    {
        CSharpCompilation compilation = GeneratorHarness.CreateCompilation(ShaderSource, UnrelatedSource);
        GeneratorDriver driver = GeneratorHarness.CreateDriver().RunGenerators(compilation);
        GeneratorRunResult first = driver.GetRunResult().Results.Single();

        Assert.Equal(new[] { IncrementalStepRunReason.New }, Reasons(first, ComputeShaderGenerator.ShaderResultStep));
        Assert.Single(first.GeneratedSources);

        // Edit a file that has nothing to do with the shader.
        SyntaxTree unrelated = compilation.SyntaxTrees.Single(static t => t.FilePath == "Source1.cs");
        CSharpCompilation edited = compilation.ReplaceSyntaxTree(
            unrelated,
            CSharpSyntaxTree.ParseText(UnrelatedSource.Replace("42", "43"), GeneratorHarness.ParseOptions, path: "Source1.cs"));

        driver = driver.RunGenerators(edited);
        GeneratorRunResult second = driver.GetRunResult().Results.Single();

        // The transform runs again (the compilation changed) but produces an equal model...
        Assert.All(Reasons(second, ComputeShaderGenerator.ShaderResultStep), static reason =>
            Assert.True(reason is IncrementalStepRunReason.Unchanged or IncrementalStepRunReason.Cached, reason.ToString()));

        // ...so the source output is served from the cache.
        Assert.NotEmpty(OutputReasons(second));
        Assert.All(OutputReasons(second), static reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
        Assert.Equal(first.GeneratedSources.Single().SourceText.ToString(), second.GeneratedSources.Single().SourceText.ToString());
    }

    [Fact]
    public void AddingAnUnrelatedFile_ReusesTheModelAndSkipsTheOutput()
    {
        CSharpCompilation compilation = GeneratorHarness.CreateCompilation(ShaderSource);
        GeneratorDriver driver = GeneratorHarness.CreateDriver().RunGenerators(compilation);

        driver = driver.RunGenerators(compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(UnrelatedSource, GeneratorHarness.ParseOptions, path: "New.cs")));
        GeneratorRunResult second = driver.GetRunResult().Results.Single();

        Assert.All(Reasons(second, ComputeShaderGenerator.ShaderResultStep), static reason =>
            Assert.True(reason is IncrementalStepRunReason.Unchanged or IncrementalStepRunReason.Cached, reason.ToString()));
        Assert.All(OutputReasons(second), static reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
    }

    [Fact]
    public void TriviaEditInTheShaderFile_ProducesAnEqualModel()
    {
        CSharpCompilation compilation = GeneratorHarness.CreateCompilation(ShaderSource);
        GeneratorDriver driver = GeneratorHarness.CreateDriver().RunGenerators(compilation);

        // Comments and blank lines move every location in the file, but a clean shader stores none.
        CSharpCompilation edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText("// A new comment\n\n" + ShaderSource.Replace("*= Twice(factor);", "*=   Twice(factor); // scale"), GeneratorHarness.ParseOptions, path: "Source0.cs"));

        driver = driver.RunGenerators(edited);
        GeneratorRunResult second = driver.GetRunResult().Results.Single();

        Assert.Equal(new[] { IncrementalStepRunReason.Unchanged }, Reasons(second, ComputeShaderGenerator.ShaderResultStep));
        Assert.All(OutputReasons(second), static reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
    }

    [Fact]
    public void EditingASharedHelperInAnotherFile_RegeneratesTheShadersThatUseIt_AndOnlyThose()
    {
        const string Uses = """
            using SharpCompute;

            namespace App;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Uses(ReadWriteBuffer<float> buffer) : IComputeShader
            {
                public void Execute() => buffer[ThreadIds.X] = Shared.Twice(buffer[ThreadIds.X]);
            }
            """;

        const string Shared = """
            namespace App;

            public static class Shared
            {
                public static float Twice(float value) => value * 2;

                public static float Unused(float value) => value + 1;
            }
            """;

        CSharpCompilation compilation = GeneratorHarness.CreateCompilation(ShaderSource, Uses, Shared);
        GeneratorDriver driver = GeneratorHarness.CreateDriver().RunGenerators(compilation);

        // The helper is in a file of its own: the shader's MSL depends on a file the shader is not in.
        SyntaxTree shared = compilation.SyntaxTrees.Single(static t => t.FilePath == "Source2.cs");
        CSharpCompilation edited = compilation.ReplaceSyntaxTree(
            shared,
            CSharpSyntaxTree.ParseText(Shared.Replace("value * 2", "value * 3"), GeneratorHarness.ParseOptions, path: "Source2.cs"));

        driver = driver.RunGenerators(edited);
        GeneratorRunResult second = driver.GetRunResult().Results.Single();

        Assert.Equal(
            new[] { IncrementalStepRunReason.Modified, IncrementalStepRunReason.Unchanged },
            Reasons(second, ComputeShaderGenerator.ShaderResultStep).OrderBy(static r => r.ToString()).ToArray());
        Assert.Contains("return value * 3.0f;", second.GeneratedSources.Single(static s => s.HintName.Contains("Uses")).SourceText.ToString());

        // An edit to a helper that no shader calls regenerates nothing.
        edited = edited.ReplaceSyntaxTree(
            edited.SyntaxTrees.Single(static t => t.FilePath == "Source2.cs"),
            CSharpSyntaxTree.ParseText(Shared.Replace("value * 2", "value * 3").Replace("value + 1", "value + 2"), GeneratorHarness.ParseOptions, path: "Source2.cs"));

        driver = driver.RunGenerators(edited);
        GeneratorRunResult third = driver.GetRunResult().Results.Single();

        Assert.All(Reasons(third, ComputeShaderGenerator.ShaderResultStep), static reason =>
            Assert.True(reason is IncrementalStepRunReason.Unchanged or IncrementalStepRunReason.Cached, reason.ToString()));
        Assert.All(OutputReasons(third), static reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
    }

    [Fact]
    public void EditingTheShader_RegeneratesIt()
    {
        CSharpCompilation compilation = GeneratorHarness.CreateCompilation(ShaderSource, UnrelatedSource);
        GeneratorDriver driver = GeneratorHarness.CreateDriver().RunGenerators(compilation);

        SyntaxTree shader = compilation.SyntaxTrees.Single(static t => t.FilePath == "Source0.cs");
        CSharpCompilation edited = compilation.ReplaceSyntaxTree(
            shader,
            CSharpSyntaxTree.ParseText(ShaderSource.Replace("value * 2", "value * 3"), GeneratorHarness.ParseOptions, path: "Source0.cs"));

        driver = driver.RunGenerators(edited);
        GeneratorRunResult second = driver.GetRunResult().Results.Single();

        Assert.Equal(new[] { IncrementalStepRunReason.Modified }, Reasons(second, ComputeShaderGenerator.ShaderResultStep));
        Assert.Contains(IncrementalStepRunReason.Modified, OutputReasons(second));
        Assert.Contains("return value * 3.0f;", second.GeneratedSources.Single().SourceText.ToString());
    }

    [Fact]
    public void EditingOneShader_LeavesTheOthersCached()
    {
        string otherShader = ShaderSource.Replace("Scale", "Other");
        CSharpCompilation compilation = GeneratorHarness.CreateCompilation(ShaderSource, otherShader);
        GeneratorDriver driver = GeneratorHarness.CreateDriver().RunGenerators(compilation);

        SyntaxTree tree = compilation.SyntaxTrees.Single(static t => t.FilePath == "Source1.cs");
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(
            tree,
            CSharpSyntaxTree.ParseText(otherShader.Replace("value * 2", "value * 5"), GeneratorHarness.ParseOptions, path: "Source1.cs")));

        GeneratorRunResult second = driver.GetRunResult().Results.Single();
        IncrementalStepRunReason[] reasons = OutputReasons(second);

        Assert.Equal(2, reasons.Length);
        Assert.Single(reasons, IncrementalStepRunReason.Cached);
        Assert.Single(reasons, IncrementalStepRunReason.Modified);
    }

    [Fact]
    public void IndependentRuns_ProduceEqualModels()
    {
        // Equality must be structural: two separate compilations share no symbols or syntax nodes.
        ShaderResult first = Models(GeneratorHarness.Run(ShaderSource, UnrelatedSource).Result).Single();
        ShaderResult second = Models(GeneratorHarness.Run(ShaderSource, UnrelatedSource).Result).Single();

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());

        ShaderResult different = Models(GeneratorHarness.Run(ShaderSource.Replace("value * 2", "value * 3")).Result).Single();

        Assert.NotEqual(first, different);
    }

    [Fact]
    public void ModelsWithDiagnostics_AreEquatableToo()
    {
        const string Broken = """
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Broken : IComputeShader
            {
                public readonly ReadWriteBuffer<float> buffer;
                public readonly double scale;

                public void Execute() => buffer[0] = 1;
            }
            """;

        ShaderResult first = Models(GeneratorHarness.Run(Broken).Result).Single();
        ShaderResult second = Models(GeneratorHarness.Run(Broken).Result).Single();

        Assert.Single(first.Diagnostics);
        Assert.Equal(first, second);

        // Moving the diagnostic changes the model, so the new location is reported.
        ShaderResult moved = Models(GeneratorHarness.Run("\n" + Broken).Result).Single();

        Assert.NotEqual(first, moved);
    }

    [Fact]
    public void Models_HoldNoSymbolsSyntaxOrCompilations()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            TestSources.Body("buffer[ThreadIds.X] = undefined + (double)factor;", "public readonly double bad;"),
            ShaderSource);

        foreach (ShaderResult model in Models(run.Result))
        {
            AssertSymbolFree(model, new HashSet<object>(ReferenceEqualityComparer.Instance), "ShaderResult");
        }
    }

    /// <summary>Walks the object graph and fails on anything that would root a compilation.</summary>
    private static void AssertSymbolFree(object? value, HashSet<object> visited, string path)
    {
        if (value is null || value is string || value.GetType().IsPrimitive || value.GetType().IsEnum)
        {
            return;
        }

        Type type = value.GetType();

        Assert.False(value is ISymbol, $"{path} holds a symbol ({type}).");
        Assert.False(value is SyntaxNode or SyntaxTree or SyntaxToken or SyntaxReference, $"{path} holds syntax ({type}).");
        Assert.False(value is Compilation or SemanticModel or Location or IOperation, $"{path} holds {type}.");

        // Immutable Roslyn value types and descriptors are fine and need no further inspection.
        if (value is DiagnosticDescriptor or TextSpan or LinePositionSpan or LinePosition)
        {
            return;
        }

        if (!type.IsValueType && !visited.Add(value))
        {
            return;
        }

        Assert.True(type.Assembly == typeof(ShaderResult).Assembly || type.IsArray, $"{path} holds an unexpected type {type}.");

        if (value is IEnumerable enumerable)
        {
            int index = 0;

            foreach (object? item in enumerable)
            {
                AssertSymbolFree(item, visited, $"{path}[{index++}]");
            }

            return;
        }

        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            AssertSymbolFree(field.GetValue(value), visited, path + "." + field.Name);
        }
    }

    [Fact]
    public void EquatableArray_ComparesByValue()
    {
        var a = new EquatableArray<string>(new[] { "x", "y" });
        var b = new EquatableArray<string>(new[] { "x", "y" });
        var c = new EquatableArray<string>(new[] { "x", "z" });

        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.Equals(c));
        Assert.True(a != c);
        Assert.False(a.Equals(new EquatableArray<string>(new[] { "x" })));
        Assert.True(default(EquatableArray<string>).Equals(EquatableArray<string>.Empty));
        Assert.Equal(2, a.Count);
        Assert.Equal(new[] { "x", "y" }, a);
    }
}
