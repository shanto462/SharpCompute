using System;
using System.Runtime.InteropServices;

namespace SharpCompute.Interop;

/// <summary>
/// Owns one +1 reference to an Objective-C object and releases it exactly once. Being a
/// <see cref="SafeHandle"/> gives two things for free: a critical finalizer, so a leaked wrapper is
/// not a leaked GPU object, and reference counting, so <see cref="Lease"/> can keep the object
/// alive across a native call that races with <c>Dispose</c> on another thread.
/// </summary>
internal sealed class ObjCHandle : SafeHandle
{
    private readonly long _memoryPressure;

    /// <param name="owned">A non-nil +1 reference; ownership transfers to the handle.</param>
    /// <param name="memoryPressure">Native bytes kept alive by the object, reported to the GC.</param>
    internal ObjCHandle(nint owned, long memoryPressure = 0)
        : base(invalidHandleValue: 0, ownsHandle: true)
    {
        SetHandle(owned);

        if (memoryPressure > 0)
        {
            _memoryPressure = memoryPressure;
            GC.AddMemoryPressure(memoryPressure);
        }
    }

    public override bool IsInvalid => handle == 0;

    protected override bool ReleaseHandle()
    {
        // May run on the finalizer thread, which has no autorelease pool of its own.
        using AutoreleasePool pool = AutoreleasePool.Push();
        ObjC.Release(handle);

        if (_memoryPressure > 0)
        {
            GC.RemoveMemoryPressure(_memoryPressure);
        }

        return true;
    }

    /// <summary>
    /// Pins the object for the duration of a native call. Throws <see cref="ObjectDisposedException"/>
    /// naming the type of <paramref name="owner"/> when the handle is already closed.
    /// </summary>
    internal Lease Acquire(object owner)
    {
        bool acquired = false;

        try
        {
            DangerousAddRef(ref acquired);
        }
        catch (ObjectDisposedException)
        {
            throw new ObjectDisposedException(owner.GetType().FullName);
        }

        return new Lease(this);
    }

    internal readonly ref struct Lease
    {
        private readonly ObjCHandle _handle;

        internal Lease(ObjCHandle handle) => _handle = handle;

        /// <summary>The raw <c>id</c>, valid until this lease is disposed.</summary>
        internal nint Pointer => _handle.handle;

        public void Dispose() => _handle?.DangerousRelease();
    }
}
