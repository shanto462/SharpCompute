using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// Which captured buffers and textures the shader can write. The runtime refuses a dispatch whose
/// bindings overlap when the shader writes one of them (on the GPU the reads through the other would
/// be stale), and lets them overlap when it only reads: a <c>ReadWriteBuffer&lt;T&gt;</c> or
/// <c>ReadWriteTexture2D&lt;T&gt;</c> that the shader never stores to is bound as "not written".
/// </summary>
/// <remarks>
/// <para>
/// The answer has to err on the side of "written": a capture wrongly taken as written only costs a
/// dispatch that binds it twice an exception, while one wrongly taken as read-only would let stale
/// reads through. So a capture counts as written unless every use of it is seen to be a read.
/// </para>
/// <para>
/// A buffer element is a C# variable (<c>buffer[i]</c> returns a reference), and C# has few ways to
/// store through one: assigning it or a member of it, <c>++</c> and <c>--</c>, passing it by
/// <c>ref</c> or <c>out</c> (the atomics do), and calling a method that modifies its struct on it.
/// <see cref="MayStoreTo"/> recognizes those from where the element stands in the code; anything else
/// reads the value. A pixel is only ever written by assigning <c>texture[x, y]</c>.
/// </para>
/// <para>
/// The one store that names no element is a wave matrix's: <c>matrix.Store(buffer, …)</c> writes 64
/// elements of the buffer it is given, from inside Metal's function (<see cref="RecordBufferStore"/>).
/// <c>WaveMatrix8x8.Load(buffer, …)</c> only reads its buffer, and marks nothing. A
/// <c>[GroupShared]</c> array either of them is given is the threadgroup's own memory, not a binding.
/// </para>
/// <para>
/// A captured resource is known by its capture, in the kernel and in the helpers that use it. One
/// that reaches the code another way (a helper's buffer or texture parameter, a local) could be any
/// capture of its kind, so a store through one marks them all, as for the fences
/// (<see cref="NeedsFence"/>). Like those, the answer is only final once the whole shader, with every
/// helper it reaches, has been translated.
/// </para>
/// </remarks>
internal sealed partial class MslTranslator
{
    private readonly HashSet<Capture> _resourcesWritten = new();
    private bool _unknownBufferWritten;
    private bool _unknownTextureWritten;

    /// <summary>
    /// True when <paramref name="capture"/> is a read-write buffer or texture that no code of the
    /// shader stores to. Valid after <see cref="TranslateKernel"/>.
    /// </summary>
    public bool IsNeverWritten(Capture capture)
    {
        if (capture.IsBuffer)
        {
            return !capture.Type.IsReadOnlyBuffer && !_unknownBufferWritten && !_resourcesWritten.Contains(capture);
        }

        return capture.IsTexture && !capture.Type.IsReadOnlyTexture && !_unknownTextureWritten && !_resourcesWritten.Contains(capture);
    }

    /// <summary>Records what a use of the buffer element <paramref name="element"/> (<c>buffer[i]</c>) does to its buffer.</summary>
    private void RecordBufferElement(IPropertyReferenceOperation element)
    {
        if (!TypeResolver.IsBufferType(element.Instance?.Type, out bool isReadOnly) || isReadOnly || !MayStoreTo(element))
        {
            return;
        }

        RecordBufferStore(element.Instance);
    }

    /// <summary>
    /// Records a store to the read-write buffer <paramref name="buffer"/> names: to an element of it,
    /// or the whole-matrix store of <c>WaveMatrix8x8.Store</c> and <c>WaveMatrixHalf8x8.Store</c>,
    /// whose destination it is. A capture is marked itself; a helper's buffer parameter could be any
    /// buffer capture, and marks them all.
    /// </summary>
    private void RecordBufferStore(IOperation? buffer)
    {
        if (CapturedResource(buffer) is { IsBuffer: true } capture)
        {
            _resourcesWritten.Add(capture);
        }
        else
        {
            _unknownBufferWritten = true;
        }
    }

    /// <summary>Records a write of a pixel of the texture <paramref name="instance"/>.</summary>
    private void RecordTextureWrite(IOperation? instance)
    {
        if (CapturedTexture(instance) is { } capture)
        {
            _resourcesWritten.Add(capture);
        }
        else
        {
            _unknownTextureWritten = true;
        }
    }

    /// <summary>The captured buffer or texture <paramref name="instance"/> names directly, or null for a parameter, a local or anything else.</summary>
    private Capture? CapturedResource(IOperation? instance)
    {
        ISymbol? symbol = WithoutImplicitConversions(instance) switch
        {
            IFieldReferenceOperation field => field.Field,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null,
        };

        return symbol is not null && _captures.TryGet(symbol, out Capture capture) && capture.Type.IsResource ? capture : null;
    }

    /// <summary>
    /// True unless <paramref name="element"/>, a variable, is only read where it stands: it (or a
    /// field, an inline-array element or a property of it) is the target of an assignment of any
    /// kind or of <c>++</c>/<c>--</c>, a <c>ref</c> or <c>out</c> argument, the receiver of a method
    /// or getter that modifies its struct, or what a reference is taken of.
    /// </summary>
    private bool MayStoreTo(IOperation element)
    {
        IOperation current = element;

        for (IOperation? parent = current.Parent; parent is not null; current = parent, parent = parent.Parent)
        {
            // `ref buffer[i]` as the value of a ref local, a ref return or a ref assignment: a reference
            // that outlives the expression. None of them translates, but none of them is a read either.
            if (current.Syntax.Parent is RefExpressionSyntax)
            {
                return true;
            }

            switch (parent)
            {
                // Still the same variable, or a part of it: what happens to that happens to the element.
                case IParenthesizedOperation:
                case ITupleOperation:
                case IConditionalOperation { IsRef: true }:
                case IFieldReferenceOperation field when ReferenceEquals(field.Instance, current):
                case IInlineArrayAccessOperation inlineElement when ReferenceEquals(inlineElement.Instance, current):
                    continue;

                // A getter that modifies its struct stores; any other one gives a value, which is
                // followed on only because its property can be the target of an assignment (a setter).
                case IPropertyReferenceOperation property when ReferenceEquals(property.Instance, current):
                    if (MutatesReceiver(property.Property.GetMethod))
                    {
                        return true;
                    }

                    continue;

                case IInvocationOperation invocation when ReferenceEquals(invocation.Instance, current):
                    return MutatesReceiver(invocation.TargetMethod);

                // Simple, compound, null-coalescing and deconstructing assignments alike.
                case IAssignmentOperation assignment:
                    return ReferenceEquals(assignment.Target, current);

                case IIncrementOrDecrementOperation:
                    return true;

                // `in` passes a reference that cannot be stored through.
                case IArgumentOperation argument:
                    return argument.Parameter is null or { RefKind: RefKind.Ref or RefKind.Out };

                case IAddressOfOperation:
                    return true;

                default:
                    return false;
            }
        }

        return false;
    }
}
