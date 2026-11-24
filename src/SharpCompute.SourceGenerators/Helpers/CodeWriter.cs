using System.Text;

namespace SharpCompute.SourceGenerators;

/// <summary>A minimal indenting text writer that always emits <c>\n</c> line endings.</summary>
internal sealed class CodeWriter
{
    private readonly StringBuilder _builder = new();

    public CodeWriter(int indent = 0) => Indent = indent;

    public int Indent { get; set; }

    public void Line(string text = "")
    {
        if (text.Length > 0)
        {
            _builder.Append(' ', Indent * 4).Append(text);
        }

        _builder.Append('\n');
    }

    /// <summary>Appends pre-formatted lines, re-indenting each non-empty line to the current level.</summary>
    public void Lines(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            Line(line.TrimEnd('\r'));
        }
    }

    public override string ToString() => _builder.ToString();
}
