using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>Every diagnostic: its ID, the text it points at, and (where it matters) its message.</summary>
public class DiagnosticTests
{
    private const string SimpleMembers = """
            public readonly ReadWriteBuffer<float> buffer;

            public void Execute() => buffer[ThreadIds.X] = 1;
        """;

    private static GeneratorRun RunBody(string body, string extraMembers = "", string trailer = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(body, extraMembers) + trailer);

    private static GeneratorRun RunShader(string members, string attributes = "", string trailer = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(members, attributes) + trailer);

    /// <summary>A shader with errors gets a stub descriptor, so call sites do not produce a second wave of errors.</summary>
    private static void AssertStub(GeneratorRun run)
    {
        GeneratedSourceResult source = Assert.Single(run.Sources);

        Assert.Contains("This shader has SharpCompute errors", source.SourceText.ToString());
        Assert.DoesNotContain("metal_stdlib", source.SourceText.ToString());
        Assert.DoesNotContain(run.Output.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
    }

    // ---- SCG0001 - SCG0005: the shader declaration ------------------------------------------------

    [Fact]
    public void MSG0001_ShaderMustBePartial()
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly struct NotPartial : IComputeShader
            {
            {{SimpleMembers}}
            }
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0001", "NotPartial");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'NotPartial' must be declared 'partial'", diagnostic.GetMessage());
        Assert.Empty(run.Sources);
    }

    [Fact]
    public void MSG0002_ContainingTypeMustBePartial()
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            using SharpCompute;

            public partial class Outer
            {
                public class Middle
                {
                    [GeneratedComputeShaderDescriptor]
                    public readonly partial struct Nested : IComputeShader
                    {
            {{SimpleMembers}}
                    }
                }
            }
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0002", "Middle");

        Assert.Contains("Type 'Middle' contains shader struct 'Nested'", diagnostic.GetMessage());
        Assert.Empty(run.Sources);
    }

    [Fact]
    public void MSG0003_MissingComputeShaderInterface()
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct NoInterface
            {
            {{SimpleMembers}}
            }
            """);

        run.AssertOnlyDiagnostic("SCG0003", "NoInterface");
        Assert.Empty(run.Sources);
    }

    [Fact]
    public void MSG0004_ShaderShouldBeReadonly_IsAWarning()
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public partial struct Mutable : IComputeShader
            {
            {{SimpleMembers}}
            }
            """);

        Assert.Equal(DiagnosticSeverity.Warning, run.AssertOnlyDiagnostic("SCG0004", "Mutable").Severity);
        Assert.Contains("metal_stdlib", Assert.Single(run.Sources).SourceText.ToString());
    }

    [Theory]
    [InlineData("public readonly partial struct Shader<T> : IComputeShader where T : unmanaged", "generic")]
    [InlineData("public readonly partial record struct Shader : IComputeShader", "a record struct")]
    [InlineData("file readonly partial struct Shader : IComputeShader", "a file-local type")]
    public void MSG0005_UnsupportedShaderDeclaration(string declaration, string what)
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            {{declaration}}
            {
                public void Execute() { }
            }
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0005", "Shader");

        Assert.Equal($"Shader struct 'Shader' cannot be {what}", diagnostic.GetMessage());
        Assert.Empty(run.Sources);
    }

    [Fact]
    public void MSG0005_ShaderNestedInAGenericType()
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            using SharpCompute;

            public partial class Outer<T>
            {
                [GeneratedComputeShaderDescriptor]
                public readonly partial struct Shader : IComputeShader
                {
                    public void Execute() { }
                }
            }
            """);

        Assert.Contains("nested in the generic type 'Outer'", run.AssertOnlyDiagnostic("SCG0005", "Shader").GetMessage());
    }

    // ---- SCG0006: captured state ------------------------------------------------------------------

    [Theory]
    [InlineData("double", "'double' is not available in Metal Shading Language; use 'float' instead")]
    [InlineData("nint", "native-sized integers are not supported; use 'long' or 'ulong' instead")]
    [InlineData("nuint", "native-sized integers are not supported")]
    [InlineData("bool", "'bool' has no stable layout across the CPU/GPU boundary")]
    [InlineData("char", "'char' is not supported; use 'ushort' instead")]
    [InlineData("decimal", "'decimal' is not available on the GPU; use 'float' instead")]
    [InlineData("string", "strings are not available on the GPU")]
    [InlineData("object", "managed objects are not available on the GPU")]
    [InlineData("float[]", "arrays are managed objects; use a ReadWriteBuffer<T> or ReadOnlyBuffer<T> instead")]
    [InlineData("System.Collections.Generic.List<float>", "managed (reference) types are not available on the GPU")]
    [InlineData("System.DayOfWeek", "enums are not supported")]
    [InlineData("float?", "nullable value types are not supported")]
    [InlineData("ReadWriteBuffer<double>", "its element type 'double' is not supported")]
    [InlineData("ReadOnlyBuffer<bool>", "its element type 'bool' is not supported")]
    public void MSG0006_UnsupportedFieldType(string type, string reason)
    {
        GeneratorRun run = RunShader($$"""
                public readonly ReadWriteBuffer<float> buffer;
                public readonly {{type}} state;

                public void Execute() => buffer[0] = 1;
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0006", type);

        Assert.StartsWith("Field 'state' of type '", diagnostic.GetMessage());
        Assert.Contains(reason, diagnostic.GetMessage());
        AssertStub(run);
    }

    [Fact]
    public void MSG0006_UnsupportedPrimaryConstructorParameterType()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Shader(ReadWriteBuffer<float> buffer, double scale) : IComputeShader
            {
                public void Execute() => buffer[0] = 1;
            }
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0006", "double");

        Assert.StartsWith("Primary constructor parameter 'scale' of type 'double'", diagnostic.GetMessage());
    }

    [Fact]
    public void MSG0006_TypesWithoutAPackedForm_AreLimitedToLocals()
    {
        // Without `Packed`, the MSL type (float3: 16 bytes, 16-byte aligned) does not match the C# struct.
        const string Loose = """

            [MslType("float3")]
            public struct Loose3
            {
                public float X;
                public float Y;
                public float Z;

                public Loose3(float x, float y, float z) { X = x; Y = y; Z = z; }
            }

            public struct Holder { public float Scale; public Loose3 Value; }
            """;

        GeneratorRun asConstant = RunShader("""
                public readonly Loose3 value;

                public void Execute() { }
            """, trailer: Loose);

        Assert.Contains("its [MslType(\"float3\")] declares no Packed type", asConstant.AssertOnlyDiagnostic("SCG0006", "Loose3").GetMessage());

        GeneratorRun asElement = RunShader("""
                public readonly ReadWriteBuffer<Loose3> values;

                public void Execute() { }
            """, trailer: Loose);

        Assert.Contains("its element type 'Loose3' is not supported (its [MslType(\"float3\")] declares no Packed type", asElement.AssertOnlyDiagnostic("SCG0006", "ReadWriteBuffer<Loose3>").GetMessage());

        GeneratorRun asStructField = RunShader("""
                public readonly Holder holder;

                public void Execute() { }
            """, trailer: Loose);

        Assert.Contains("its field 'Value' has the unsupported type 'Loose3'", asStructField.AssertOnlyDiagnostic("SCG0009", "Holder").GetMessage());

        // As a local, parameter, or return value it is fine.
        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(
            """
            Loose3 local = Make(buffer[0]);
            buffer[1] = Sum(local);
            """,
            """
            private static Loose3 Make(float value) => new Loose3(value, value * 2, value * 3);

            private static float Sum(Loose3 value) => value.X + value.Y + value.Z;
            """) + Loose).Verify("Tests.TestShader");

        Assert.Contains("static float3 Make(float value);", descriptor.MslSource);
        Assert.Equal("float3 local = ::Make(buffer[0]);\nbuffer[1] = ::Sum(local);", descriptor.KernelBody);
    }

    [Fact]
    public void MSG0006_IsNotRepeatedForEveryUseOfTheField()
    {
        GeneratorRun run = RunShader("""
                public readonly ReadWriteBuffer<float> buffer;
                public readonly bool enabled;

                public void Execute()
                {
                    if (enabled)
                    {
                        buffer[0] = enabled ? 1 : 0;
                    }
                }
            """);

        run.AssertOnlyDiagnostic("SCG0006", "bool");
    }

    // ---- SCG0007: types inside the shader body ----------------------------------------------------

    [Theory]
    [InlineData("double d = 1.0;", "1.0", "'double'")]
    [InlineData("double d; d = buffer[0];", "double", "'double'")]
    [InlineData("float f = (float)Math.Sqrt(buffer[0]);", "Math.Sqrt(buffer[0])", "'double'")]
    [InlineData("nint n = 5;", "5", "'nint'")]
    [InlineData("nuint n = 5;", "5", "'nuint'")]
    [InlineData("var day = DayOfWeek.Monday;", "DayOfWeek.Monday", "'DayOfWeek'")]
    [InlineData("float? maybe = buffer[0];", "buffer[0]", "'float?'")]
    [InlineData("var c = 'x';", "'x'", "'char'")]
    [InlineData("decimal m = 1.5m;", "1.5m", "'decimal'")]
    public void MSG0007_UnsupportedTypeInBody(string statement, string location, string type)
    {
        GeneratorRun run = RunBody(statement);
        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0007", location);

        Assert.StartsWith($"Type {type} is not supported in a compute shader", diagnostic.GetMessage());
        AssertStub(run);
    }

    [Fact]
    public void MSG0007_UnsupportedHelperSignature()
    {
        GeneratorRun run = RunBody(
            "buffer[0] = Half(buffer[0]);",
            "private static float Half(double value) => 0.5f;");

        // Both the argument conversion and the parameter are doubles.
        Assert.All(run.Diagnostics, static d => Assert.Equal("SCG0007", d.Id));
        Assert.Contains(run.Diagnostics, d => run.TextAt(d) == "double");
    }

    // ---- SCG0008: primary constructor parameters --------------------------------------------------

    [Fact]
    public void MSG0008_ParameterOnlyUsedToInitializeAField_IsFine()
    {
        // The field is the captured state; the parameter is not stored, so there is no double storage.
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Shader(ReadWriteBuffer<float> buffer, float factor) : IComputeShader
            {
                private readonly float _factor = factor;

                public void Execute() => buffer[ThreadIds.X] *= _factor;
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Shader");

        Assert.Equal("buffer[int(__tid.x)] *= __c._factor;", descriptor.KernelBody);
        Assert.Contains("__constants._factor = this._factor;", run.Source("Shader.SharpCompute.g.cs"));
        Assert.DoesNotContain("__constants.factor", run.Source("Shader.SharpCompute.g.cs"));
    }

    [Fact]
    public void MSG0008_ParameterAlsoStoredInField()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Shader(ReadWriteBuffer<float> buffer, float factor) : IComputeShader
            {
                private readonly float _factor = factor;

                public void Execute() => buffer[ThreadIds.X] *= _factor + factor;
            }
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0008", "factor");

        Assert.Equal("Primary constructor parameter 'factor' is also stored in the field '_factor'; capture it either as the parameter or as a field, not both", diagnostic.GetMessage());
        // Reported on the use inside the initializer (0-based line 5), not on the parameter declaration.
        Assert.Equal(5, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        AssertStub(run);
    }

    [Fact]
    public void MSG0008_ParameterAlsoStoredInProperty()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Shader(ReadWriteBuffer<float> buffer, float factor) : IComputeShader
            {
                public float Doubled { get; } = factor * 2;

                public void Execute() => buffer[ThreadIds.X] *= factor;
            }
            """);

        Assert.Contains("the property 'Doubled'", run.AssertOnlyDiagnostic("SCG0008", "factor").GetMessage());
    }

    [Fact]
    public void MSG0008_ParameterHiddenByFieldOfTheSameName()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Shader(ReadWriteBuffer<float> buffer, float factor) : IComputeShader
            {
                public readonly float factor;

                public void Execute() => buffer[ThreadIds.X] *= factor;
            }
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0008", "factor");

        Assert.Equal(3, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    // ---- SCG0009 / SCG0010: user structs ----------------------------------------------------------

    [Theory]
    [InlineData("[StructLayout(LayoutKind.Explicit)] public struct Bad { [FieldOffset(0)] public float A; [FieldOffset(0)] public int B; }", "it uses LayoutKind.Explicit")]
    [InlineData("[StructLayout(LayoutKind.Auto)] public struct Bad { public float A; public int B; }", "it uses LayoutKind.Auto")]
    [InlineData("[StructLayout(LayoutKind.Sequential, Pack = 1)] public struct Bad { public Half A; public float B; }", "its StructLayout Pack = 1 is below the natural alignment (4) of its fields")]
    [InlineData("[StructLayout(LayoutKind.Sequential, Pack = 2)] public struct Bad { public TestVectors.Vec3 A; }", "its StructLayout Pack = 2 is below the natural alignment (4) of its fields")]
    [InlineData("[StructLayout(LayoutKind.Sequential, Size = 64)] public struct Bad { public float A; }", "it specifies an explicit StructLayout Size")]
    [InlineData("public struct Bad { }", "it has no instance fields")]
    [InlineData("public struct Bad { public float A; public double B; }", "its field 'B' has the unsupported type 'double'")]
    [InlineData("public struct Bad { public float A; public bool B; }", "its field 'B' has the unsupported type 'bool'")]
    [InlineData("public unsafe struct Bad { public fixed float A[4]; }", "its field 'A' is a fixed-size buffer")]
    public void MSG0009_InvalidStructLayout(string declaration, string reason)
    {
        string trailer = "\n" + declaration.Replace("[StructLayout", "[System.Runtime.InteropServices.StructLayout").Replace("LayoutKind.", "System.Runtime.InteropServices.LayoutKind.").Replace("[FieldOffset", "[System.Runtime.InteropServices.FieldOffset");

        // As a buffer element type...
        GeneratorRun asElement = RunShader("""
                public readonly ReadWriteBuffer<Bad> items;

                public void Execute() { }
            """, trailer: trailer);

        Diagnostic diagnostic = asElement.AssertOnlyDiagnostic("SCG0009", "ReadWriteBuffer<Bad>");
        Assert.StartsWith("Struct 'Bad' cannot be used in a compute shader: " + reason, diagnostic.GetMessage());
        AssertStub(asElement);

        // ...as a constant...
        GeneratorRun asConstant = RunShader("""
                public readonly Bad item;

                public void Execute() { }
            """, trailer: trailer);

        asConstant.AssertOnlyDiagnostic("SCG0009", "Bad");

        // ...and as a local.
        GeneratorRun asLocal = RunShader("""
                public void Execute()
                {
                    Bad local;
                }
            """, trailer: trailer);

        asLocal.AssertOnlyDiagnostic("SCG0009", "Bad");
    }

    [Fact]
    public void MSG0009_StructWithManagedField()
    {
        // Such a struct cannot even be a buffer element in C# (CS8377), but it can be a field.
        GeneratorRun run = RunShader("""
                public readonly Named item;

                public void Execute() { }
            """, trailer: "\npublic struct Named { public float Value; public string Name; }");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0009", "Named");

        Assert.Equal("Struct 'Named' cannot be used in a compute shader: its field 'Name' has the unsupported type 'string' (strings are not available on the GPU)", diagnostic.GetMessage());
        AssertStub(run);
    }

    [Fact]
    public void MSG0009_NestedStructProblemsNameTheInnermostStruct()
    {
        GeneratorRun run = RunShader("""
                public readonly ReadWriteBuffer<Outer> items;

                public void Execute() { }
            """, trailer: """

            public struct Outer { public float A; public Inner B; }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
            public struct Inner { [System.Runtime.InteropServices.FieldOffset(0)] public float A; }
            """);

        Assert.StartsWith("Struct 'Inner' cannot be used in a compute shader: it uses LayoutKind.Explicit", run.AssertOnlyDiagnostic("SCG0009", "ReadWriteBuffer<Outer>").GetMessage());
    }

    [Fact]
    public void MSG0009_GenericStruct()
    {
        GeneratorRun run = RunShader("""
                public readonly Pair<float> pair;

                public void Execute() { }
            """, trailer: "\npublic struct Pair<T> where T : unmanaged { public T A; public T B; }");

        Assert.Contains("generic structs are not supported", run.AssertOnlyDiagnostic("SCG0009", "Pair<float>").GetMessage());
    }

    [Fact]
    public void MSG0010_BufferNestedInStruct()
    {
        GeneratorRun run = RunShader("""
                public readonly Bundle bundle;

                public void Execute() { }
            """, trailer: "\npublic struct Bundle { public float Scale; public ReadWriteBuffer<float> Data; }");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0010", "Bundle");

        Assert.Equal("Struct 'Bundle' has a buffer field 'Data'; buffers must be captured directly by the shader struct", diagnostic.GetMessage());
        AssertStub(run);
    }

    // ---- SCG0011: binding limits ------------------------------------------------------------------

    private static string ManyBuffers(int count, bool withConstant)
    {
        var builder = new StringBuilder();

        for (int i = 0; i < count; i++)
        {
            builder.AppendLine($"    public readonly ReadWriteBuffer<float> b{i};");
        }

        if (withConstant)
        {
            builder.AppendLine("    public readonly float scale;");
        }

        builder.AppendLine("    public void Execute() => b0[ThreadIds.X] = 1;");
        return TestSources.Shader(builder.ToString());
    }

    [Theory]
    [InlineData(32, false, 32)]
    [InlineData(31, true, 32)]
    [InlineData(40, true, 41)]
    public void MSG0011_TooManyBoundArguments(int buffers, bool withConstant, int bound)
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, ManyBuffers(buffers, withConstant));
        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0011", "TestShader");

        Assert.Equal($"Shader struct 'TestShader' binds {bound} arguments, but Metal supports at most 31 (buffers plus one constants block)", diagnostic.GetMessage());
        AssertStub(run);
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(30, true)]
    public void MSG0011_NotReportedAtTheLimit(int buffers, bool withConstant)
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, ManyBuffers(buffers, withConstant)).Verify("Tests.TestShader");

        Assert.Contains("[[buffer(30)]]", descriptor.MslSource);
        Assert.DoesNotContain("[[buffer(31)]]", descriptor.MslSource);
    }

    // ---- SCG0012: unsupported syntax --------------------------------------------------------------

    [Theory]
    [InlineData("Func<float, float> twice = x => x * 2;", "a lambda expression", "x => x * 2")]
    [InlineData("Func<float, float> twice = (float x) => { return x * 2; };", "a lambda expression", "(float x) => { return x * 2; }")]
    [InlineData("Action act = delegate { };", "an anonymous method", "delegate { }")]
    [InlineData("Func<float, float> root = MathF.Sqrt;", "a delegate creation", "MathF.Sqrt")]
    [InlineData("var query = from v in new[] { 1, 2 } select v * 2;", "a LINQ query expression", "from v in new[] { 1, 2 } select v * 2")]
    [InlineData("object o = new object();", "'new' of the class 'object'", "new object()")]
    [InlineData("var list = new System.Collections.Generic.List<float>();", "'new' of the class 'List<float>'", "new System.Collections.Generic.List<float>()")]
    [InlineData("try { buffer[0] = 1; } catch { }", "a try/catch statement", "try")]
    [InlineData("try { buffer[0] = 1; } finally { }", "a try/finally statement", "try")]
    [InlineData("string s = \"hello\";", "a string literal", "\"hello\"")]
    [InlineData("var s = $\"{factor}\";", "an interpolated string", "$\"{factor}\"")]
    [InlineData("var name = nameof(buffer);", "a nameof expression", "nameof(buffer)")]
    [InlineData("var type = typeof(float);", "a typeof expression", "typeof(float)")]
    [InlineData("foreach (var v in new float[2]) { }", "a foreach loop", "foreach")]
    [InlineData("throw new InvalidOperationException();", "a throw statement", "throw")]
    [InlineData("using (var stream = new System.IO.MemoryStream()) { }", "a using statement", "using")]
    [InlineData("var tuple = (1, 2f);", "a tuple expression", "(1, 2f)")]
    [InlineData("var anonymous = new { A = 1 };", "an anonymous object creation", "new { A = 1 }")]
    [InlineData("float v = ThreadIds.X switch { 0 => 1f, _ => 2f };", "a switch expression", "ThreadIds.X switch { 0 => 1f, _ => 2f }")]
    [InlineData("bool small = ThreadIds.X is < 4;", "pattern matching", "ThreadIds.X is < 4")]
    [InlineData("ref float first = ref buffer[0];", "the ref local 'first'", "ref float")]
    [InlineData("while (Fn.Modf(factor, out float integral) > 0) { break; }", "an inline variable declaration", "float integral")]
    [InlineData("var pair = new Pair { Left = 1 };", "an object initializer", "{ Left = 1 }")]
    [InlineData("var index = new Index(1);", "a constructor call on the struct 'Index', whose source is not part of this compilation", "new Index(1)")]
    [InlineData("var copy = this;", "'this' used as a value", "this")]
    [InlineData("var local = buffer;", "the buffer-typed local 'local'", "var")]
    [InlineData("switch (ThreadIds.X) { case > 3: break; }", "a pattern-based switch case", "case > 3:")]
    [InlineData("goto done; done: return;", "a goto statement", "goto")]
    public void MSG0012_UnsupportedSyntax_NamesTheConstruct(string statement, string construct, string location)
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            "using System.Linq;\n" + TestSources.Body(statement) + """

            public struct Pair
            {
                public float Left;
                public float Right;

                public Pair(float left, float right) { Left = left; Right = right; }
            }
            """);

        Diagnostic diagnostic = run.Diagnostics.First(static d => d.Id == "SCG0012");

        Assert.Equal(location, run.TextAt(diagnostic));
        Assert.StartsWith("Unsupported construct in a compute shader: " + construct, diagnostic.GetMessage());
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.All(run.Diagnostics, static d => Assert.Equal("SCG0012", d.Id));
        AssertStub(run);
    }

    [Fact]
    public void MSG0012_BufferElementPassedByReference()
    {
        // `thread float&` cannot bind to device memory, so only thread-local storage can be passed.
        GeneratorRun run = RunBody(
            "Fill(out buffer[0]);",
            "private static void Fill(out float value) => value = 1;");

        Assert.Contains(
            "the by-reference argument 'out buffer[0]' (only locals and parameters, or their fields, can be passed by reference to a helper)",
            run.AssertOnlyDiagnostic("SCG0012", "out buffer[0]").GetMessage());
    }

    // ---- SCG0013 / SCG0014: helper methods --------------------------------------------------------

    [Fact]
    public void MSG0013_InstanceMethodCalledOnAnotherInstance()
    {
        // Instance helpers are fine when called on the shader itself (see HelperTests); another
        // instance would need its captured state, which only exists for the dispatched shader.
        GeneratorRun run = RunBody(
            "buffer[ThreadIds.X] = default(TestShader).Scaled(2);",
            "private float Scaled(float value) => value * factor;");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0013", "default(TestShader).Scaled(2)");

        Assert.Equal("Method 'Scaled' is an instance method called on a value other than the shader itself; instance helper methods can only be called on 'this'", diagnostic.GetMessage());
        AssertStub(run);
    }

    [Fact]
    public void MSG0014_DirectRecursion()
    {
        GeneratorRun run = RunBody(
            "buffer[0] = Factorial(5);",
            "private static int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0014", "Factorial(n - 1)");

        Assert.Equal("Method 'Factorial' is recursive (Factorial -> Factorial); recursion is not supported in Metal Shading Language", diagnostic.GetMessage());
        AssertStub(run);
    }

    [Fact]
    public void MSG0014_MutualRecursion()
    {
        GeneratorRun run = RunBody(
            "buffer[0] = IsEven(ThreadIds.X) ? 1 : 0;",
            """
            private static bool IsEven(int n) => n == 0 || IsOdd(n - 1);

            private static bool IsOdd(int n) => n != 0 && IsEven(n - 1);
            """);

        Assert.Contains("(IsEven -> IsOdd -> IsEven)", run.AssertOnlyDiagnostic("SCG0014", "IsEven(n - 1)").GetMessage());
    }

    // ---- SCG0015: thread group size ---------------------------------------------------------------

    [Theory]
    [InlineData("[ThreadGroupSize(0)]", "ThreadGroupSize(0)", "[ThreadGroupSize(0, 1, 1)] is invalid: every dimension must be at least 1")]
    [InlineData("[ThreadGroupSize(8, -1)]", "ThreadGroupSize(8, -1)", "[ThreadGroupSize(8, -1, 1)] is invalid: every dimension must be at least 1")]
    [InlineData("[ThreadGroupSize(8, 8, 0)]", "ThreadGroupSize(8, 8, 0)", "[ThreadGroupSize(8, 8, 0)] is invalid: every dimension must be at least 1")]
    [InlineData("[ThreadGroupSize(1025)]", "ThreadGroupSize(1025)", "[ThreadGroupSize(1025, 1, 1)] is invalid: the total of 1025 threads exceeds Metal's limit of 1024 threads per threadgroup")]
    [InlineData("[ThreadGroupSize(32, 32, 2)]", "ThreadGroupSize(32, 32, 2)", "[ThreadGroupSize(32, 32, 2)] is invalid: the total of 2048 threads exceeds Metal's limit of 1024 threads per threadgroup")]
    [InlineData("[ThreadGroupSize(65536, 65536, 65536)]", "ThreadGroupSize(65536, 65536, 65536)", "[ThreadGroupSize(65536, 65536, 65536)] is invalid: the total of 281474976710656 threads exceeds Metal's limit of 1024 threads per threadgroup")]
    public void MSG0015_InvalidThreadGroupSize(string attribute, string location, string message)
    {
        GeneratorRun run = RunShader(SimpleMembers, attribute);

        Assert.Equal(message, run.AssertOnlyDiagnostic("SCG0015", location).GetMessage());
        AssertStub(run);
    }

    // ---- SCG0016 / SCG0017: unsupported members ---------------------------------------------------

    [Theory]
    [InlineData("Console.WriteLine(1);", "Console.WriteLine(1)", "it is not an [MslIntrinsic] method")]
    [InlineData("buffer[0] = MathF.Cbrt(factor);", "MathF.Cbrt(factor)", "it has no MSL equivalent")]
    [InlineData("buffer[0] = MathF.Round(factor, 2);", "MathF.Round(factor, 2)", "it has no MSL equivalent")]
    [InlineData("buffer[0] = factor.GetHashCode();", "factor.GetHashCode()", "it is not an [MslIntrinsic] method")]
    [InlineData("Money money = default; money++; buffer[0] = money.Amount;", "money++", "only operators declared by [MslType] vector types are supported here; write the operation as a method call")]
    public void MSG0016_UnsupportedMethodCall(string statement, string location, string reason)
    {
        // Methods, operators and conversions whose source is part of the compilation are translated
        // (see CodeReuseTests); what is left is code without source, and `++` on a user type.
        GeneratorRun run = RunBody(statement, trailer: """

            public struct Money
            {
                public float Amount;

                public static Money operator ++(Money value) => value;
            }
            """);

        Assert.Contains(reason, run.AssertOnlyDiagnostic("SCG0016", location).GetMessage());
        AssertStub(run);
    }

    [Theory]
    [InlineData("buffer[0] = Shared;", "Shared", "static fields that are not readonly are not available on the GPU")]
    [InlineData("buffer[0] = Stored;", "Stored", "auto-properties of the shader struct are not captured")]
    [InlineData("var device = buffer.Device;", "buffer.Device", "only the indexer and 'Length' of a buffer can be used in a shader")]
    [InlineData("buffer[0] = Other.Value;", "Other.Value", "static properties are not available on the GPU")]
    [InlineData("Index index = default; buffer[0] = index.Value;", "index.Value", "only swizzle properties of [MslType] vectors, and the properties of user structs whose source is part of this compilation, are supported")]
    public void MSG0017_UnsupportedMember(string statement, string location, string reason)
    {
        GeneratorRun run = RunBody(
            statement,
            """
            private static float Shared = 2;

            public float Stored { get; }
            """,
            """

            public static class Other { public static float Value { get; } = 1; }
            """);

        Assert.Contains(reason, run.AssertOnlyDiagnostic("SCG0017", location).GetMessage());
        AssertStub(run);
    }

    // ---- SCG0018 / SCG0019 / SCG0020 --------------------------------------------------------------

    [Theory]
    [InlineData("factor = 2;", "factor")]
    [InlineData("this.factor *= 2;", "this.factor")]
    [InlineData("factor++;", "factor")]
    [InlineData("tint.X = 1;", "tint.X")]
    [InlineData("settings.Scale = 1;", "settings.Scale")]
    public void MSG0018_CapturedStateIsReadOnly(string statement, string location)
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            $$"""
            using SharpCompute;
            using TestVectors;

            public struct Settings { public float Scale; }

            [GeneratedComputeShaderDescriptor]
            public partial struct Shader : IComputeShader
            {
                public ReadWriteBuffer<float> buffer;
                public float factor;
                public Vec3 tint;
                public Settings settings;

                public void Execute()
                {
                    {{statement}}
                }
            }
            """);

        Diagnostic diagnostic = run.AssertDiagnostic("SCG0018", location);

        Assert.StartsWith("Cannot modify '", diagnostic.GetMessage());
        Assert.Equal(new[] { "SCG0004", "SCG0018" }, run.Diagnostics.Select(static d => d.Id).OrderBy(static id => id));
    }

    [Fact]
    public void MSG0018_PrimaryConstructorParameter()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public partial struct Shader(ReadWriteBuffer<float> buffer, float factor) : IComputeShader
            {
                public void Execute()
                {
                    factor += 1;
                    buffer[0] = factor;
                }
            }
            """);

        run.AssertDiagnostic("SCG0018", "factor");
    }

    [Fact]
    public void MSG0019_LengthOfABufferPassedToAHelper()
    {
        GeneratorRun run = RunBody(
            "buffer[0] = Count(buffer);",
            "private static int Count(ReadWriteBuffer<float> values) => values.Length;");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0019", "values.Length");

        Assert.Equal("'Length' is only available on buffers captured directly by the shader struct, not on 'values'", diagnostic.GetMessage());
        AssertStub(run);
    }

    [Fact]
    public void MSG0020_MissingExecute()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct NoExecute : IComputeShader
            {
                public readonly ReadWriteBuffer<float> buffer;
            }
            """);

        run.AssertOnlyDiagnostic("SCG0020", "NoExecute");
    }

    // ---- SCG0021: evaluation order ----------------------------------------------------------------

    [Theory]
    [InlineData("int j = i++ + --i;", "i++ + --i", "i")]
    [InlineData("buffer[i] = i++;", "buffer[i] = i++", "i")]
    [InlineData("buffer[0] = buffer[i++] + buffer[i++];", "buffer[0] = buffer[i++] + buffer[i++]", "i")]
    [InlineData("i = i++;", "i = i++", "i")]
    [InlineData("i += i++;", "i += i++", "i")]
    [InlineData("buffer[0] = Sum(i++, i);", "buffer[0] = Sum(i++, i)", "i")]
    [InlineData("float f = (i = 2) + i;", "(i = 2) + i", "i")]
    [InlineData("float w = 0; float r = Fn.Modf(factor, out w) + w;", "Fn.Modf(factor, out w) + w", "w")]
    [InlineData("Vec3 v = new Vec3(i++, i, 0);", "new Vec3(i++, i, 0)", "i")]
    [InlineData("Vec3 v = default; v.X = v.Y++;", "v.X = v.Y++", "v")]
    [InlineData("for (int k = 0; k < 4; k = k++ + 1) { }", "k = k++ + 1", "k")]
    [InlineData("while (i++ < i) { }", "i++ < i", "i")]
    [InlineData("float r = Fn.Modf(factor, out float w) + w;", "Fn.Modf(factor, out float w) + w", "w")]
    [InlineData("float s = factor; float r = Split(ref s) + s;", "Split(ref s) + s", "s")]
    public void MSG0021_ExpressionsThatDependOnCSharpEvaluationOrder(string statement, string location, string variable)
    {
        GeneratorRun run = RunBody(
            "int i = ThreadIds.X;\n" + statement,
            "private static float Sum(float a, float b) => a + b; private static float Split(ref float x) { x *= 2; return x; }");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0021", location);

        Assert.StartsWith($"'{variable}' is modified and used again within one expression", diagnostic.GetMessage());
        AssertStub(run);
    }

    [Theory]
    [InlineData("i++; ++i; i--;")]
    [InlineData("i = i + 1; i += i; i = -i;")]
    [InlineData("int j = i++; int k = --i; buffer[j] = k;")]
    [InlineData("buffer[i++] = factor; buffer[i] = buffer[i] + i;")]
    [InlineData("buffer[i] += i; buffer[i]++;")]
    [InlineData("int a, b; a = b = i; buffer[a] = b;")]
    [InlineData("bool ok = i++ < 10 && i > 2 || i == 0; buffer[0] = ok ? i++ : i--;")]
    [InlineData("for (int j = 0, k = 9; j < k; j++, k--) { buffer[j] = k; }")]
    [InlineData("float w; float r = Fn.Modf(factor, out w); buffer[0] = r + w;")]
    [InlineData("Vec3 v = default; v.X = v.Y + v.Z; v.Y += v.X; v[i] = v[i] * 2; buffer[0] = v.X;")]
    [InlineData("buffer[0] = Sum(i, i) + Sum(i + 1, i * 2);")]
    [InlineData("float w = factor; float r = Fn.Modf(w, out w); buffer[0] = r + w;")]
    [InlineData("float s = factor; buffer[0] = Scale(ref s, s) + Scale(ref i2, i2);")]
    public void MSG0021_NotReportedWhenTheOrderIsDefined(string statements)
    {
        // The callee writes `ref`/`out` arguments after all arguments are evaluated, in C# and MSL alike.
        GeneratorHarness.Run(
            TestSources.Vectors,
            TestSources.Body(
                "int i = ThreadIds.X; float i2 = i;\n" + statements,
                "private static float Sum(float a, float b) => a + b; private static float Scale(ref float x, float by) { x *= by; return x; }")).Verify("Tests.TestShader");
    }

    // ---- Locations and suppression ----------------------------------------------------------------

    [Fact]
    public void Diagnostics_AreReportedAtRealSourceLocations()
    {
        GeneratorRun run = RunBody("double d = 1.0;");
        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0007", "1.0");

        // An in-source location (not just a file path) is what makes #pragma and .editorconfig work.
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Same(run.Input.SyntaxTrees.Single(static t => t.FilePath == "Source1.cs"), diagnostic.Location.SourceTree);
    }

    [Fact]
    public void Diagnostics_CanBeSuppressedWithPragma()
    {
        const string Declaration = """
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public partial struct Mutable : IComputeShader
            {
                public ReadWriteBuffer<float> buffer;

                public void Execute() => buffer[ThreadIds.X] = 1;
            }
            """;

        // RunGeneratorsAndUpdateCompilation applies the compilation's suppression rules, like a real build.
        var plain = GeneratorHarness.CreateCompilation(Declaration);
        GeneratorHarness.CreateDriver().RunGeneratorsAndUpdateCompilation(plain, out _, out var reported);

        Assert.False(Assert.Single(reported).IsSuppressed);

        var withPragma = GeneratorHarness.CreateCompilation("#pragma warning disable SCG0004\n" + Declaration);
        GeneratorHarness.CreateDriver().RunGeneratorsAndUpdateCompilation(withPragma, out Compilation output, out var suppressed);

        // The compiler keeps suppressed diagnostics around (for tooling) but does not report them.
        Assert.True(Assert.Single(suppressed).IsSuppressed);
        Assert.DoesNotContain(output.GetDiagnostics(), static d => d.Severity >= DiagnosticSeverity.Warning);
    }

    [Theory]
    [InlineData("bool same = a == b;", "a == b", "'=='")]
    [InlineData("bool different = a != b;", "a != b", "'!='")]
    [InlineData("if (a < b) { }", "a < b", "'<'")]
    public void MSG0012_VectorComparisonsThatReturnBool(string statement, string location, string op)
    {
        // A vector type could declare these in C#, but MSL would produce a bool vector instead.
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Body("Cmp2 a = default; Cmp2 b = default;\n" + statement) + """

            [MslType("float2", Packed = "packed_float2")]
            public struct Cmp2
            {
                public float X;
                public float Y;

                public static bool operator ==(Cmp2 a, Cmp2 b) => a.X == b.X && a.Y == b.Y;
                public static bool operator !=(Cmp2 a, Cmp2 b) => !(a == b);
                public static bool operator <(Cmp2 a, Cmp2 b) => a.X < b.X;
                public static bool operator >(Cmp2 a, Cmp2 b) => a.X > b.X;
                public override bool Equals(object? obj) => false;
                public override int GetHashCode() => 0;
            }
            """.Replace("using TestVectors;", string.Empty));

        Assert.Contains($"a {op} comparison of vectors that returns 'bool'", run.AssertOnlyDiagnostic("SCG0012", location).GetMessage());
    }

    // ---- Robustness -------------------------------------------------------------------------------

    [Theory]
    [InlineData("buffer[ThreadIds.X] = undefinedName;")]
    [InlineData("buffer[ThreadIds.X] = Missing(1);")]
    [InlineData("buffer[ThreadIds.X] = ;")]
    [InlineData("UnknownType value = default; buffer[0] = value.Field;")]
    [InlineData("buffer[ThreadIds.X] = factor +")]
    [InlineData("float x = \"text\" * 2;")]
    [InlineData("for (int i = 0; i < ; i++) { }")]
    [InlineData("buffer[0] = new Vec3(1, 2);")]
    [InlineData("if (factor) { }")]
    [InlineData("buffer[0] = (;")]
    [InlineData("buffer[0] = factor.;")]
    [InlineData("int i = \"a\"; double d = i; var q = from x in d select x;")]
    public void InvalidCSharp_NeverCrashesAndLeavesTheErrorsToTheCompiler(string statement)
    {
        GeneratorRun run = RunBody(statement);

        Assert.Null(run.Result.Exception);
        Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "SCG9999");

        // The C# compiler reports the real problem...
        Assert.Contains(run.Input.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);

        // ...and no MSL is produced from code that does not compile.
        Assert.DoesNotContain(run.Sources, static s => s.SourceText.ToString().Contains("metal_stdlib"));
    }

    [Fact]
    public void AllDescriptors_HaveUniqueIds_AndTheExpectedCategory()
    {
        DiagnosticDescriptor[] descriptors = typeof(DiagnosticDescriptors)
            .GetFields()
            .Select(static f => (DiagnosticDescriptor)f.GetValue(null)!)
            .ToArray();

        Assert.Equal(53, descriptors.Length);
        Assert.Equal(descriptors.Length, descriptors.Select(static d => d.Id).Distinct().Count());
        Assert.All(descriptors, static d =>
        {
            Assert.Matches(@"^SCG\d{4}$", d.Id);
            Assert.Equal("SharpCompute", d.Category);
            Assert.True(d.IsEnabledByDefault);
        });
        Assert.Equal(DiagnosticSeverity.Warning, DiagnosticDescriptors.ShaderShouldBeReadOnly.DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, DiagnosticDescriptors.GroupSharedWithoutThreadGroupSize.DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, DiagnosticDescriptors.SimdGroupCallSitesShareLine.DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, DiagnosticDescriptors.LocalArrayTooLarge.DefaultSeverity);
        Assert.Equal(DiagnosticSeverity.Warning, DiagnosticDescriptors.WaveMatrixInPartialSimdGroup.DefaultSeverity);
        Assert.All(descriptors.Where(static d => d.Id is not ("SCG0004" or "SCG0035" or "SCG0038" or "SCG0044" or "SCG0051")), static d => Assert.Equal(DiagnosticSeverity.Error, d.DefaultSeverity));
    }
}
