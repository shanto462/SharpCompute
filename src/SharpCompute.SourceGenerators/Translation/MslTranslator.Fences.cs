using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// <c>fence()</c> after texture writes. In Metal a thread that writes a pixel of a
/// <c>read_write</c> texture and then reads that texture gets the pixel's old value back, unless
/// <c>texture.fence()</c> is called between the two. C# has no such rule (and <c>ForCpu</c> reads
/// what it wrote), so the translator supplies the fence.
/// </summary>
/// <remarks>
/// <para>
/// The rule: a write is followed by a fence when the shader, meaning the kernel and every helper it
/// reaches, reads a pixel of the same texture anywhere. "Anywhere" rather than "later", because a
/// read that comes first in the source still follows the write on the next turn of a loop or in the
/// next call of a helper. A texture the shader only writes gets no fence, so its MSL is exactly what
/// it was.
/// </para>
/// <para>
/// A captured texture is known by its capture. A texture that reaches the code another way (a
/// helper's texture parameter, a local) could be any of them, so a read through one counts as a read
/// of every read-write texture, and a write through one is fenced when the shader reads any.
/// </para>
/// <para>
/// Whether a texture is read is only known once the whole shader has been translated, and a write is
/// often translated before the read that makes its fence necessary. So a write leaves a
/// <see cref="FenceMarker"/>-delimited placeholder in the text, and <see cref="ResolveFences"/>
/// turns every placeholder into a fence or into nothing when <see cref="TranslateKernel"/> is done.
/// </para>
/// </remarks>
internal sealed partial class MslTranslator
{
    /// <summary>Delimits a placeholder: a character that no MSL the translator writes contains.</summary>
    private const char FenceMarker = '\u0001';

    private readonly List<PendingFence> _pendingFences = new();
    private readonly HashSet<Capture> _texturesRead = new();
    private bool _unknownTextureRead;

    /// <summary>The captured texture <paramref name="instance"/> names directly, or null for a parameter, a local or anything else.</summary>
    private Capture? CapturedTexture(IOperation? instance)
    {
        ISymbol? symbol = WithoutImplicitConversions(instance) switch
        {
            IFieldReferenceOperation field => field.Field,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null,
        };

        return symbol is not null && _captures.TryGet(symbol, out Capture capture) && capture.IsTexture ? capture : null;
    }

    /// <summary>Records that a pixel of the texture <paramref name="instance"/> is read (<c>texture[x, y]</c> as a value).</summary>
    private void RecordTextureRead(IOperation? instance)
    {
        // A ReadOnlyTexture2D is never written by the shader, so reading one cannot follow a write.
        if (TypeResolver.IsTextureType(WithoutImplicitConversions(instance)?.Type, out bool isReadOnly) && isReadOnly)
        {
            return;
        }

        if (CapturedTexture(instance) is { } capture)
        {
            _texturesRead.Add(capture);
        }
        else
        {
            _unknownTextureRead = true;
        }
    }

    /// <summary>
    /// The placeholder to put directly after a write to the texture <paramref name="instance"/>, whose
    /// MSL expression is <paramref name="texture"/>.
    /// </summary>
    private string FencePlaceholder(IOperation? instance, string texture)
    {
        _pendingFences.Add(new PendingFence(CapturedTexture(instance), texture));
        return FenceMarker + (_pendingFences.Count - 1).ToString(CultureInfo.InvariantCulture) + FenceMarker;
    }

    /// <summary>
    /// Splits the placeholder off a statement that is a texture write, so that the fence can become a
    /// statement of its own on the next line. Returns null when <paramref name="statement"/> is anything else.
    /// </summary>
    private static string? TakeTrailingFence(ref string statement)
    {
        if (statement.Length < 3 || statement[statement.Length - 1] != FenceMarker)
        {
            return null;
        }

        int start = statement.LastIndexOf(FenceMarker, statement.Length - 2);
        string placeholder = statement.Substring(start);

        statement = statement.Substring(0, start);
        return placeholder;
    }

    /// <summary>
    /// Replaces the placeholders in a translated function body. One that is a line of its own becomes
    /// the statement <c>texture.fence();</c> or the line disappears; one inside a line (a write in
    /// the header of a <c>for</c>, where there are no statements) becomes <c>, texture.fence()</c> or nothing.
    /// </summary>
    private string ResolveFences(string body)
    {
        if (body.IndexOf(FenceMarker) < 0)
        {
            return body;
        }

        var result = new StringBuilder(body.Length + 64);

        foreach (string line in body.Split('\n'))
        {
            int open = line.IndexOf(FenceMarker);

            if (open < 0)
            {
                result.Append(line).Append('\n');
                continue;
            }

            int close = line.IndexOf(FenceMarker, open + 1);

            if (close == line.Length - 1 && line.AsSpan(0, open).Trim(' ').IsEmpty)
            {
                // The whole line is one placeholder: a statement, or no line at all.
                PendingFence fence = Pending(line, open, close);

                if (NeedsFence(fence))
                {
                    result.Append(line, 0, open).Append(fence.Expression).Append(".fence();\n");
                }

                continue;
            }

            int cursor = 0;

            for (; open >= 0; open = line.IndexOf(FenceMarker, cursor))
            {
                close = line.IndexOf(FenceMarker, open + 1);
                result.Append(line, cursor, open - cursor);

                PendingFence fence = Pending(line, open, close);

                if (NeedsFence(fence))
                {
                    result.Append(", ").Append(fence.Expression).Append(".fence()");
                }

                cursor = close + 1;
            }

            result.Append(line, cursor, line.Length - cursor).Append('\n');
        }

        // Split gave one more piece than the body has line ends.
        result.Length--;
        return result.ToString();
    }

    private PendingFence Pending(string line, int open, int close) =>
        _pendingFences[int.Parse(line.Substring(open + 1, close - open - 1), CultureInfo.InvariantCulture)];

    /// <summary>
    /// True when the shader reads the texture this write went to: the capture itself, any read-write
    /// texture when the write went through a parameter or a local, or a read through one of those.
    /// </summary>
    private bool NeedsFence(PendingFence fence) =>
        _unknownTextureRead || (fence.Texture is null ? _texturesRead.Count > 0 : _texturesRead.Contains(fence.Texture));

    /// <summary>A write that may need a fence: the texture it wrote (null when it is not a capture) and its MSL expression.</summary>
    private readonly struct PendingFence
    {
        public PendingFence(Capture? texture, string expression)
        {
            Texture = texture;
            Expression = expression;
        }

        public Capture? Texture { get; }

        public string Expression { get; }
    }
}
