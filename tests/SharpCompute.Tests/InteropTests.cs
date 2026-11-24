using System;
using SharpCompute.Interop;

namespace SharpCompute.Tests;

public class InteropTests
{
    [MetalTheory]
    [InlineData("")]
    [InlineData("kernel void main0() {}")]
    [InlineData("naïve — ünïcödé ✓")]
    [InlineData("surrogate pairs: 😀🚀")]
    [InlineData("embedded\0null")]
    public void NSString_RoundTrips(string value)
    {
        using AutoreleasePool pool = AutoreleasePool.Push();
        nint nsString = NSString.Create(value);

        try
        {
            Assert.NotEqual(0, nsString);
            Assert.Equal((nuint)value.Length, ObjC.SendNUInt(nsString, Selectors.Length));
            Assert.Equal(value, NSString.ToManaged(nsString));
        }
        finally
        {
            ObjC.Release(nsString);
        }
    }

    [MetalFact]
    public void NSString_NilMapsToNull()
    {
        Assert.Null(NSString.ToManaged(0));
    }

    [MetalFact]
    public void NSString_LongStringsRoundTrip()
    {
        string value = string.Create(200_000, 0, static (span, _) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = (char)('a' + i % 26);
            }
        });

        nint nsString = NSString.Create(value);

        try
        {
            Assert.Equal(value, NSString.ToManaged(nsString));
        }
        finally
        {
            ObjC.Release(nsString);
        }
    }

    [MetalFact]
    public void Selectors_AreInternedByName()
    {
        Assert.NotEqual(0, Selectors.Release);
        Assert.Equal(Selectors.Release, ObjC.GetSelector("release"));
        Assert.NotEqual(Selectors.Retain, Selectors.Release);
    }

    [MetalFact]
    public void GetClass_ThrowsForUnknownClasses()
    {
        Assert.NotEqual(0, ObjC.GetClass("NSObject"));
        Assert.Throws<InvalidOperationException>(() => ObjC.GetClass("SharpComputeNoSuchClass"));
    }

    [MetalFact]
    public void AutoreleasePools_Nest()
    {
        using AutoreleasePool outer = AutoreleasePool.Push();

        using (AutoreleasePool inner = AutoreleasePool.Push())
        {
        }

        using AutoreleasePool sibling = AutoreleasePool.Push();
    }

    [MetalFact]
    public void ObjCHandle_ReleaseIsDeferredWhileLeased()
    {
        nint nsString = NSString.Create("leased");
        ObjCHandle handle = new(nsString);

        ObjCHandle.Lease lease = handle.Acquire(this);

        try
        {
            handle.Dispose();

            // Disposed, but the lease still pins the object: it is not released yet, so messaging it is safe.
            Assert.False(handle.IsClosed);
            Assert.Equal("leased", NSString.ToManaged(lease.Pointer));
        }
        finally
        {
            lease.Dispose();
        }

        // The deferred release ran when the last lease went away.
        Assert.True(handle.IsClosed);
        Assert.Throws<ObjectDisposedException>(() => handle.Acquire(this).Dispose());
    }
}
