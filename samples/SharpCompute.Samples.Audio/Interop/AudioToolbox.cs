// Original SharpCompute sample (MIT).
using System.Runtime.InteropServices;

namespace SharpCompute.Samples.Audio.Interop;

/// <summary>
/// The few AudioToolbox (AudioQueue) and CoreAudio functions the player needs, called directly
/// through <see cref="LibraryImportAttribute"/>: no bindings, no reflection, AOT-safe. Every
/// function returns an <c>OSStatus</c> (0 on success) unless noted.
/// </summary>
internal static unsafe partial class AudioToolbox
{
    private const string AudioToolboxLibrary = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const string CoreAudioLibrary = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";

    /// <summary><c>kAudioFormatLinearPCM</c> ('lpcm').</summary>
    internal const uint FormatLinearPcm = 0x6C70636D;

    /// <summary><c>kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked</c>: native-endian packed float32.</summary>
    internal const uint FormatFlagsPackedFloat = 1 | 8;

    /// <summary><c>kAudioObjectSystemObject</c>.</summary>
    internal const uint SystemObject = 1;

    /// <summary><c>kAudioHardwarePropertyDefaultOutputDevice</c> ('dOut').</summary>
    internal const uint PropertyDefaultOutputDevice = 0x644F7574;

    /// <summary><c>kAudioObjectPropertyScopeGlobal</c> ('glob').</summary>
    internal const uint ScopeGlobal = 0x676C6F62;

    /// <summary>
    /// Creates a playback queue. With a null run loop the callback runs on one of the queue's own
    /// threads. The callback receives <paramref name="userData"/>, the queue and the buffer to refill.
    /// </summary>
    [LibraryImport(AudioToolboxLibrary)]
    internal static partial int AudioQueueNewOutput(
        AudioStreamBasicDescription* format, delegate* unmanaged<nint, nint, AudioQueueBuffer*, void> callback, nint userData,
        nint runLoop, nint runLoopMode, uint flags, nint* queue);

    [LibraryImport(AudioToolboxLibrary)]
    internal static partial int AudioQueueAllocateBuffer(nint queue, uint byteSize, AudioQueueBuffer** buffer);

    [LibraryImport(AudioToolboxLibrary)]
    internal static partial int AudioQueueEnqueueBuffer(nint queue, AudioQueueBuffer* buffer, uint packetDescriptionCount, nint packetDescriptions);

    [LibraryImport(AudioToolboxLibrary)]
    internal static partial int AudioQueueStart(nint queue, nint startTime);

    /// <summary>Stops playback; with <paramref name="immediate"/> = 1, synchronously, without playing what is queued.</summary>
    [LibraryImport(AudioToolboxLibrary)]
    internal static partial int AudioQueueStop(nint queue, byte immediate);

    /// <summary>Disposes the queue and its buffers; with <paramref name="immediate"/> = 1, no callback runs after it returns.</summary>
    [LibraryImport(AudioToolboxLibrary)]
    internal static partial int AudioQueueDispose(nint queue, byte immediate);

    [LibraryImport(CoreAudioLibrary)]
    internal static partial int AudioObjectGetPropertyData(
        uint objectId, AudioObjectPropertyAddress* address, uint qualifierSize, void* qualifier, uint* dataSize, void* data);

    /// <summary>The default output device's id, or 0 (<c>kAudioObjectUnknown</c>) if there is none.</summary>
    internal static uint DefaultOutputDevice()
    {
        AudioObjectPropertyAddress address = new() { Selector = PropertyDefaultOutputDevice, Scope = ScopeGlobal, Element = 0 };
        uint device = 0;
        uint size = sizeof(uint);
        return AudioObjectGetPropertyData(SystemObject, &address, 0, null, &size, &device) == 0 ? device : 0;
    }
}

/// <summary><c>AudioStreamBasicDescription</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioStreamBasicDescription
{
    public double SampleRate;
    public uint FormatId;
    public uint FormatFlags;
    public uint BytesPerPacket;
    public uint FramesPerPacket;
    public uint BytesPerFrame;
    public uint ChannelsPerFrame;
    public uint BitsPerChannel;
    public uint Reserved;
}

/// <summary><c>AudioQueueBuffer</c> (the queue owns it; the callback fills <see cref="AudioData"/>).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AudioQueueBuffer
{
    public uint AudioDataBytesCapacity;
    public void* AudioData;
    public uint AudioDataByteSize;
    public void* UserData;
    public uint PacketDescriptionCapacity;
    public nint PacketDescriptions;
    public uint PacketDescriptionCount;
}

/// <summary><c>AudioObjectPropertyAddress</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioObjectPropertyAddress
{
    public uint Selector;
    public uint Scope;
    public uint Element;
}
