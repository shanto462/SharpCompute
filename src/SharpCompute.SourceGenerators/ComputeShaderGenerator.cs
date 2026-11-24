using System;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// Translates every <c>[GeneratedComputeShaderDescriptor]</c> struct to Metal Shading Language and
/// emits a partial implementing <c>IComputeShaderDescriptor&lt;TSelf&gt;</c> for it.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ComputeShaderGenerator : IIncrementalGenerator
{
    internal const string AttributeMetadataName = "SharpCompute.GeneratedComputeShaderDescriptorAttribute";

    /// <summary>The tracking name of the step that produces one <see cref="ShaderResult"/> per shader.</summary>
    internal const string ShaderResultStep = "ShaderResult";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // All semantic work happens in the transform, which returns an equatable, symbol-free result.
        // When an edit does not change that result, the output steps below are skipped entirely.
        IncrementalValuesProvider<ShaderResult> results = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeMetadataName,
                static (node, _) => node is StructDeclarationSyntax or RecordDeclarationSyntax,
                static (attributeContext, cancellationToken) => ShaderAnalyzer.Analyze(attributeContext, cancellationToken))
            .WithTrackingName(ShaderResultStep);

        // Sources depend on the model alone, so they stay cached across unrelated edits.
        IncrementalValuesProvider<ShaderModel> models = results
            .Select(static (result, _) => result.Model)
            .Where(static model => model is not null)!;

        context.RegisterSourceOutput(models, static (productionContext, model) =>
        {
            try
            {
                productionContext.AddSource(model.HintName, SourceText.From(DescriptorEmitter.Emit(model), Encoding.UTF8));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                productionContext.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.InternalError,
                    Location.None,
                    model.FullyQualifiedName,
                    e.GetType().Name + ": " + e.Message));
            }
        });

        // Diagnostics are re-anchored to the current syntax trees so that `#pragma warning disable`
        // works. That needs the compilation, but only shaders that have diagnostics pay for it.
        IncrementalValuesProvider<(EquatableArray<DiagnosticInfo> Diagnostics, Compilation Compilation)> diagnostics = results
            .Select(static (result, _) => result.Diagnostics)
            .Where(static diagnostics => diagnostics.Count > 0)
            .Combine(context.CompilationProvider);

        context.RegisterSourceOutput(diagnostics, static (productionContext, pair) =>
        {
            foreach (DiagnosticInfo diagnostic in pair.Diagnostics)
            {
                productionContext.ReportDiagnostic(diagnostic.ToDiagnostic(pair.Compilation));
            }
        });
    }
}
