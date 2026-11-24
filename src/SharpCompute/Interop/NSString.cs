using System;

namespace SharpCompute.Interop;

/// <summary>Conversions between <see cref="string"/> and <c>NSString</c>. Both are UTF-16, so neither direction transcodes.</summary>
internal static unsafe class NSString
{
    private static readonly nint s_class = ObjC.GetClass("NSString");

    /// <summary>
    /// Creates an <c>NSString</c> holding a copy of <paramref name="value"/>. Returns a +1 reference
    /// (<c>alloc</c>/<c>init</c>) that the caller must release.
    /// </summary>
    internal static nint Create(ReadOnlySpan<char> value)
    {
        nint instance = ObjC.SendIntPtr(s_class, Selectors.Alloc);

        fixed (char* chars = value)
        {
            // A fixed empty span yields null; NSString accepts that when length is 0.
            return ObjC.SendIntPtr(instance, Selectors.InitWithCharactersLength, chars, (nuint)value.Length);
        }
    }

    /// <summary>Copies an <c>NSString</c> into a managed string. Returns null for nil. Does not autorelease anything.</summary>
    internal static string? ToManaged(nint nsString)
    {
        if (nsString == 0)
        {
            return null;
        }

        nuint length = ObjC.SendNUInt(nsString, Selectors.Length);

        if (length == 0)
        {
            return string.Empty;
        }

        return string.Create(checked((int)length), nsString, static (span, source) =>
        {
            fixed (char* chars = span)
            {
                ObjC.SendVoid(source, Selectors.GetCharactersRange, chars, new NSRange(0, (nuint)span.Length));
            }
        });
    }

    /// <summary><c>error.localizedDescription</c> as a managed string, or a fallback when the error is nil.</summary>
    internal static string DescribeError(nint nsError)
    {
        if (nsError == 0)
        {
            return "Unknown error (no NSError was reported).";
        }

        // localizedDescription is autoreleased: callers run inside an AutoreleasePool.
        return ToManaged(ObjC.SendIntPtr(nsError, Selectors.LocalizedDescription)) ?? "Unknown error.";
    }
}
