using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SharpCompute.SourceGenerators;

/// <summary>An equatable source location that does not root a <see cref="SyntaxTree"/>.</summary>
internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public static LocationInfo? From(Location? location)
    {
        if (location?.SourceTree is not { } tree)
        {
            return null;
        }

        return new LocationInfo(tree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    /// <summary>
    /// Re-creates the location. When the file is still part of <paramref name="compilation"/>, the
    /// result is a real source location, so <c>#pragma warning disable</c> and per-file
    /// <c>.editorconfig</c> severities apply to the diagnostic; otherwise it is an external location.
    /// </summary>
    public Location ToLocation(Compilation compilation)
    {
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            if (tree.FilePath == FilePath && TextSpan.End <= tree.Length)
            {
                return Location.Create(tree, TextSpan);
            }
        }

        return Location.Create(FilePath, TextSpan, LineSpan);
    }
}

/// <summary>An equatable, symbol-free description of a diagnostic, materialized in the output step.</summary>
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> Arguments)
{
    public Diagnostic ToDiagnostic(Compilation compilation) =>
        Diagnostic.Create(Descriptor, Location?.ToLocation(compilation) ?? Microsoft.CodeAnalysis.Location.None, Arguments.Cast<object>().ToArray());
}

/// <summary>Collects diagnostics during analysis, dropping exact duplicates.</summary>
internal sealed class DiagnosticBag
{
    private readonly List<DiagnosticInfo> _items = new();
    private readonly HashSet<DiagnosticInfo> _seen = new();

    public int ErrorCount { get; private set; }

    public bool HasErrors => ErrorCount > 0;

    public void Report(DiagnosticDescriptor descriptor, Location? location, params string[] arguments) =>
        Add(new DiagnosticInfo(descriptor, LocationInfo.From(location), new EquatableArray<string>(arguments)));

    public void Add(DiagnosticInfo diagnostic)
    {
        if (!_seen.Add(diagnostic))
        {
            return;
        }

        _items.Add(diagnostic);

        if (diagnostic.Descriptor.DefaultSeverity == DiagnosticSeverity.Error)
        {
            ErrorCount++;
        }
    }

    public void AddRange(DiagnosticBag other)
    {
        foreach (DiagnosticInfo diagnostic in other._items)
        {
            Add(diagnostic);
        }
    }

    public EquatableArray<DiagnosticInfo> ToEquatableArray() => new(_items.ToArray());
}
