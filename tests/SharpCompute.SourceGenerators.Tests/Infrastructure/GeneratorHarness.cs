using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>Runs <see cref="ComputeShaderGenerator"/> over C# snippets, against the real SharpCompute assembly.</summary>
internal static class GeneratorHarness
{
    public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(CreateReferences);

    private static readonly CSharpCompilationOptions CompilationOptions = new(
        OutputKind.DynamicallyLinkedLibrary,
        nullableContextOptions: NullableContextOptions.Enable,
        allowUnsafe: true);

    /// <summary>Compiles the snippets and runs the generator once.</summary>
    public static GeneratorRun Run(params string[] sources)
    {
        CSharpCompilation compilation = CreateCompilation(sources);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);

        return new GeneratorRun(compilation, (CSharpCompilation)output, driver);
    }

    public static CSharpCompilation CreateCompilation(params string[] sources) =>
        CSharpCompilation.Create(
            "SharpCompute.GeneratedTests",
            sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, ParseOptions, path: $"Source{index}.cs")),
            References.Value,
            CompilationOptions);

    public static GeneratorDriver CreateDriver() =>
        CSharpGeneratorDriver.Create(
            new[] { new ComputeShaderGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        // The reference set of the running test host: the net11.0 framework plus SharpCompute.
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
        {
            string name = Path.GetFileNameWithoutExtension(path);

            if (name.StartsWith("System.", StringComparison.Ordinal) || name is "System" or "mscorlib" or "netstandard" or "Microsoft.CSharp")
            {
                paths.Add(path);
            }
        }

        paths.Add(typeof(IComputeShader).Assembly.Location);

        return paths.Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();
    }
}

/// <summary>The static descriptor members of a generated shader, read from the compiled assembly.</summary>
internal sealed record ShaderDescriptor(Type Type, string EntryPoint, string MslSource, int ThreadGroupSizeX, int ThreadGroupSizeY, int ThreadGroupSizeZ, int GridWindowIndex)
{
    /// <summary>
    /// The statements between the braces of the kernel function, without their indentation and without
    /// the grid-window prologue that rebases the built-ins (<see cref="KernelPrologue"/>; covered by GridWindowTests).
    /// </summary>
    public string KernelBody => string.Join("\n", FunctionBody("kernel void " + EntryPoint).Split('\n').Where(static line => !IsPrologue(line)));

    /// <summary>The grid-window prologue lines at the top of the kernel body (empty when the kernel reads no grid built-in).</summary>
    public string KernelPrologue => string.Join("\n", FunctionBody("kernel void " + EntryPoint).Split('\n').Where(IsPrologue));

    private static bool IsPrologue(string line) =>
        line.StartsWith("const uint3 __", StringComparison.Ordinal) && line.Contains(" = __windowed ? ", StringComparison.Ordinal);

    /// <summary>The body of the function whose declaration starts with <paramref name="signaturePrefix"/>.</summary>
    public string FunctionBody(string signaturePrefix)
    {
        string[] lines = MslSource.Split('\n');
        int start = -1;

        // Skip forward declarations (they end in `;`) and find the definition.
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith(signaturePrefix, StringComparison.Ordinal) && !lines[i].EndsWith(';'))
            {
                start = i;
                break;
            }
        }

        Assert.True(start >= 0, $"No function starting with '{signaturePrefix}' in:\n{MslSource}");

        int open = Array.IndexOf(lines, "{", start);
        int close = Array.IndexOf(lines, "}", open);

        return string.Join("\n", lines.Skip(open + 1).Take(close - open - 1).Select(static line => line.Length >= 4 ? line[4..] : line));
    }

    /// <summary>Compiles the MSL with Metal and fails on errors and on warnings. No-op without a Metal device.</summary>
    public ShaderDescriptor AssertMslCompiles()
    {
        if (!MetalCompiler.IsAvailable)
        {
            return this;
        }

        MetalCompiler.Result result = MetalCompiler.Compile(MslSource, EntryPoint);

        Assert.True(result.Success, $"Metal rejected the generated MSL:\n{result.Messages}\n\n{MslSource}");
        // An unused local in the C# is an unused local in the MSL; that is the user's code, faithfully
        // translated. Every other warning would point at something the generator emitted badly.
        string[] warnings = result.Warnings
            .Split("\n", StringSplitOptions.RemoveEmptyEntries)
            .Where(static line => line.Contains(": warning:", StringComparison.Ordinal) && !line.Contains("[-Wunused-", StringComparison.Ordinal))
            .ToArray();

        Assert.True(warnings.Length == 0, $"Metal reported warnings for the generated MSL:\n{result.Warnings}\n\n{MslSource}");
        return this;
    }

    /// <summary>
    /// Checks that the sizes and offsets the generator asserts in MSL for <paramref name="mslStruct"/>
    /// are those the CLR actually uses for <paramref name="clrType"/>. Metal checks the other half
    /// (that MSL lays the struct out the same way) when it compiles the static_asserts.
    /// </summary>
    public void AssertLayoutMatchesClr(string mslStruct, Type clrType)
    {
        Match size = Regex.Match(MslSource, $@"static_assert\(sizeof\({Regex.Escape(mslStruct)}\) == (\d+),");
        Assert.True(size.Success, $"No size assert for '{mslStruct}' in:\n{MslSource}");
        Assert.Equal(Marshal.SizeOf(clrType), int.Parse(size.Groups[1].Value));

        MatchCollection offsets = Regex.Matches(MslSource, $@"static_assert\(__builtin_offsetof\({Regex.Escape(mslStruct)}, (\w+)\) == (\d+),");
        FieldInfo[] fields = clrType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Equal(fields.Length, offsets.Count);

        foreach (Match offset in offsets)
        {
            string name = offset.Groups[1].Value;
            FieldInfo field = fields.Single(f => f.Name == name || f.Name == $"<{name}>k__BackingField");
            Assert.Equal((int)Marshal.OffsetOf(clrType, field.Name), int.Parse(offset.Groups[2].Value));
        }
    }

    /// <summary>The generated nested <c>__Constants</c> struct, or null when the shader has no constants.</summary>
    public Type? ConstantsType => Type.GetNestedType("__Constants", BindingFlags.NonPublic);
}

internal sealed class GeneratorRun
{
    private static readonly string[] IgnoredWarnings = { "CS0649", "CS0169", "CS0414" };

    private Assembly? _assembly;

    public GeneratorRun(CSharpCompilation input, CSharpCompilation output, GeneratorDriver driver)
    {
        Input = input;
        Output = output;
        Driver = driver;
        Result = driver.GetRunResult().Results.Single();
    }

    public CSharpCompilation Input { get; }

    /// <summary>The input compilation plus the generated sources.</summary>
    public CSharpCompilation Output { get; }

    public GeneratorDriver Driver { get; }

    public GeneratorRunResult Result { get; }

    /// <summary>Diagnostics reported by the generator itself (SCGxxxx).</summary>
    public ImmutableArray<Diagnostic> Diagnostics => Result.Diagnostics;

    public ImmutableArray<GeneratedSourceResult> Sources => Result.GeneratedSources;

    /// <summary>The generated C# for the hint name ending in <paramref name="hintNameSuffix"/>.</summary>
    public string Source(string hintNameSuffix) =>
        Sources.Single(s => s.HintName.EndsWith(hintNameSuffix, StringComparison.Ordinal)).SourceText.ToString();

    /// <summary>Asserts that the generator reported nothing and that input plus generated code compile cleanly.</summary>
    public GeneratorRun AssertClean()
    {
        Assert.True(Result.Exception is null, Result.Exception?.ToString());
        Assert.True(Diagnostics.IsEmpty, "Unexpected generator diagnostics:\n" + string.Join("\n", Diagnostics));
        return AssertCompiles();
    }

    /// <summary>Asserts that input plus generated code compile with no errors and no warnings.</summary>
    public GeneratorRun AssertCompiles()
    {
        Diagnostic[] problems = Output.GetDiagnostics()
            .Where(static d => d.Severity >= DiagnosticSeverity.Warning && !IgnoredWarnings.Contains(d.Id))
            .ToArray();

        Assert.True(problems.Length == 0, "The compilation has problems:\n" + string.Join("\n", problems.Select(static p => p.ToString())) + "\n\n" + string.Join("\n\n", Sources.Select(static s => s.SourceText.ToString())));
        return this;
    }

    /// <summary>Emits the output compilation, loads it, and reads the descriptor of <paramref name="typeName"/>.</summary>
    public ShaderDescriptor Descriptor(string typeName)
    {
        Type type = LoadAssembly().GetType(typeName, throwOnError: true)!;
        MethodInfo read = typeof(GeneratorRun).GetMethod(nameof(Read), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type);
        ShaderDescriptor descriptor = (ShaderDescriptor)read.Invoke(null, null)!;

        // The loaded type must report the MSL this run generated. Every test shader is called
        // Tests.TestShader in an assembly of the same name, each in a load context of its own, and
        // the descriptor is read through reflection over static abstract members; this check tells
        // "the generator emitted something else" apart from "the wrong descriptor was read" when a
        // test's assertion on the MSL fails. (Three such assertions once failed together in one
        // run, after their MSL had compiled, and the output of that run was not kept.)
        if (GeneratedMsl(typeName) is { } generated)
        {
            Assert.True(
                generated == descriptor.MslSource,
                $"The descriptor loaded for '{typeName}' does not carry the MSL this run generated for it.\n--- generated\n{generated}\n--- loaded\n{descriptor.MslSource}");
        }

        return descriptor;
    }

    /// <summary>
    /// The MSL in this run's generated source for <paramref name="typeName"/>: the value of the raw
    /// string its <c>MslSource</c> member returns, read from the syntax rather than by loading the type.
    /// Null when the type has no such member with a string literal body.
    /// </summary>
    public string? GeneratedMsl(string typeName) =>
        Output.GetTypeByMetadataName(typeName)?
            .GetMembers()
            .OfType<IPropertySymbol>()
            .Where(static property => property.Name.EndsWith(".MslSource", StringComparison.Ordinal))
            .SelectMany(static property => property.DeclaringSyntaxReferences)
            .Select(static reference => reference.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .Select(static property => property.ExpressionBody?.Expression)
            .OfType<LiteralExpressionSyntax>()
            .Select(static literal => literal.Token.ValueText)
            .FirstOrDefault();

    /// <summary>
    /// The one-stop check for a valid shader: no diagnostics, the C# compiles, the descriptor loads,
    /// and Metal accepts the MSL without warnings.
    /// </summary>
    public ShaderDescriptor Verify(string typeName) => AssertClean().Descriptor(typeName).AssertMslCompiles();

    public Type LoadType(string typeName) => LoadAssembly().GetType(typeName, throwOnError: true)!;

    /// <summary>The source text a diagnostic points at.</summary>
    public string TextAt(Diagnostic diagnostic)
    {
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        SyntaxTree tree = Input.SyntaxTrees.Single(t => t.FilePath == span.Path);

        return tree.GetText().ToString(diagnostic.Location.SourceSpan);
    }

    /// <summary>Asserts that exactly one diagnostic with <paramref name="id"/> was reported, at <paramref name="expectedText"/>.</summary>
    public Diagnostic AssertDiagnostic(string id, string expectedText)
    {
        Diagnostic[] matches = Diagnostics.Where(d => d.Id == id).ToArray();

        Assert.True(matches.Length == 1, $"Expected exactly one {id}, got:\n" + string.Join("\n", Diagnostics));
        Assert.Equal(expectedText, TextAt(matches[0]));
        return matches[0];
    }

    /// <summary>Asserts that the only diagnostic is <paramref name="id"/>, located at <paramref name="expectedText"/>.</summary>
    public Diagnostic AssertOnlyDiagnostic(string id, string expectedText)
    {
        Assert.True(Diagnostics.Length == 1, "Expected exactly one diagnostic, got:\n" + string.Join("\n", Diagnostics));
        return AssertDiagnostic(id, expectedText);
    }

    private Assembly LoadAssembly()
    {
        if (_assembly is not null)
        {
            return _assembly;
        }

        using var stream = new MemoryStream();
        var emit = Output.Emit(stream);

        Assert.True(emit.Success, "Emit failed:\n" + string.Join("\n", emit.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;

        // SharpCompute itself resolves from the default context, so descriptor interfaces unify.
        return _assembly = new AssemblyLoadContext("GeneratedShaders", isCollectible: true).LoadFromStream(stream);
    }

    private static ShaderDescriptor Read<T>()
        where T : struct, IComputeShaderDescriptor<T> =>
        new(typeof(T), T.EntryPoint, T.MslSource, T.ThreadGroupSizeX, T.ThreadGroupSizeY, T.ThreadGroupSizeZ, T.GridWindowIndex);
}
