using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpCompute.Build.Tests;

/// <summary>
/// <see cref="ShaderRecordReader"/>: the shaders of an assembly, read from its metadata. The assembly
/// read is this one, whose shaders are in Shaders.cs, so every record can be compared with what the
/// descriptor itself returns.
/// </summary>
public class ShaderRecordReaderTests
{
    internal static string ThisAssembly => typeof(ShaderRecordReaderTests).Assembly.Location;

    [Fact]
    public void Reads_EveryRecordedShader_WithTheSourceItsDescriptorReturns()
    {
        List<string> skipped = [];
        Dictionary<string, ShaderRecord> records = ShaderRecordReader.Read(ThisAssembly, skipped.Add).ToDictionary(static record => record.TypeName);

        Assert.Equal(
            [
                "SharpCompute.Build.Tests.Containing.Nested",
                "SharpCompute.Build.Tests.Fill",
                "SharpCompute.Build.Tests.FillFast",
                "SharpCompute.Build.Tests.Gradient",
                "SharpCompute.Build.Tests.ImplicitMembers",
                "SharpCompute.Build.Tests.MulAddExact",
                "SharpCompute.Build.Tests.MulAddFastExact",
                "SharpCompute.Build.Tests.Rejected",
            ],
            records.Keys.Order(StringComparer.Ordinal));

        AssertRecord<Fill>(records["SharpCompute.Build.Tests.Fill"], options: 0);
        AssertRecord<FillFast>(records["SharpCompute.Build.Tests.FillFast"], options: 1);
        AssertRecord<MulAddExact>(records["SharpCompute.Build.Tests.MulAddExact"], options: 2);
        AssertRecord<MulAddFastExact>(records["SharpCompute.Build.Tests.MulAddFastExact"], options: 3);
        AssertRecord<Gradient>(records["SharpCompute.Build.Tests.Gradient"], options: 0);
        AssertRecord<Rejected>(records["SharpCompute.Build.Tests.Rejected"], options: 0);

        // Found by the method that implements MslSource, however the C# spells the implementation.
        AssertRecord<ImplicitMembers>(records["SharpCompute.Build.Tests.ImplicitMembers"], options: 0);

        // A private nested shader, whose descriptor only its containing type can reach.
        ShaderRecord nested = records["SharpCompute.Build.Tests.Containing.Nested"];
        Assert.Equal(Containing.NestedEntryPoint, nested.EntryPoint);
        Assert.Equal(Containing.NestedSource, nested.MslSource);
    }

    [Fact]
    public void Skips_ARecordWhoseSourceIsNotALiteral_AndSaysSo()
    {
        List<string> skipped = [];
        IReadOnlyList<ShaderRecord> records = ShaderRecordReader.Read(ThisAssembly, skipped.Add);

        Assert.DoesNotContain(records, static record => record.EntryPoint == "ComputedSource_Kernel");

        string line = Assert.Single(skipped);
        Assert.Contains("'SharpCompute.Build.Tests.ComputedSource'", line);
        Assert.Contains("compiled at run time", line);
    }

    [Fact]
    public void AHandWrittenDescriptor_HasNoRecord()
    {
        Assert.DoesNotContain(ShaderRecordReader.Read(ThisAssembly), static record => record.EntryPoint == "HandWritten_Kernel" || record.TypeName.EndsWith("HandWritten", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRecord_GivesTheNameAndTheTextTheRuntimeUses()
    {
        ShaderRecord exact = ShaderRecordReader.Read(ThisAssembly).Single(static record => record.TypeName.EndsWith(".MulAddExact", StringComparison.Ordinal));

        Assert.Equal(PrecompiledShaderContract.FunctionName(exact.EntryPoint, exact.MslSource, exact.CompileOptions), exact.FunctionName);
        Assert.StartsWith(exact.EntryPoint + "__sc", exact.FunctionName);
        Assert.Equal("#pragma clang fp contract(off)\n" + exact.MslSource, exact.CompiledSource);

        ShaderRecord plain = ShaderRecordReader.Read(ThisAssembly).Single(static record => record.TypeName.EndsWith(".Fill", StringComparison.Ordinal));
        Assert.Equal(plain.MslSource, plain.CompiledSource);
    }

    [Fact]
    public void Reads_WithoutLoadingTheAssemblyOrItsDependencies()
    {
        // Alone in a folder, under another name: nothing it references can be resolved from there.
        using TemporaryDirectory directory = new();
        string copy = directory.File("Renamed.dll");
        File.Copy(ThisAssembly, copy);

        Assert.Equal(
            ShaderRecordReader.Read(ThisAssembly).Select(static record => (record.TypeName, record.FunctionName)),
            ShaderRecordReader.Read(copy).Select(static record => (record.TypeName, record.FunctionName)));
    }

    [Fact]
    public void AnAssemblyWithoutShaders_HasNoRecords()
    {
        Assert.Empty(ShaderRecordReader.Read(typeof(GraphicsDevice).Assembly.Location));
        Assert.Empty(ShaderRecordReader.Read(typeof(ShaderRecordReader).Assembly.Location));
    }

    [Fact]
    public void AFileThatIsNotAnAssembly_IsAnError()
    {
        using TemporaryDirectory directory = new();
        string text = directory.File("NotAnAssembly.dll");
        File.WriteAllText(text, "This is not a portable executable.");

        Assert.ThrowsAny<BadImageFormatException>(() => ShaderRecordReader.Read(text));
        Assert.Throws<FileNotFoundException>(() => ShaderRecordReader.Read(directory.File("Missing.dll")));
    }

    private static void AssertRecord<T>(ShaderRecord record, int options)
        where T : struct, IComputeShaderDescriptor<T>
    {
        Assert.Equal(Descriptor.EntryPoint<T>(), record.EntryPoint);
        Assert.Equal(Descriptor.Source<T>(), record.MslSource);
        Assert.Equal(Descriptor.Options<T>(), record.CompileOptions);
        Assert.Equal(options, record.CompileOptions);
        Assert.Contains("kernel void " + record.EntryPoint + "(", record.MslSource);
    }
}
