using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using SharpCompute.Samples.Audio;

namespace SharpCompute.Tests.Audio;

public sealed class AudioClipTests
{
    [MetalFact]
    public void SaveWav_WritesACanonicalPcmHeaderAndSamples()
    {
        float[] samples = [0f, 0.5f, -0.5f, 1f, -1f, 2f, -3f, 0.25f, float.NaN];
        using AudioClip clip = AudioClip.FromSamples(GraphicsDevice.GetDefault(), samples, 22050);
        string path = Path.Combine(Path.GetTempPath(), $"sharpcompute-audio-{Guid.NewGuid():N}.wav");

        try
        {
            clip.SaveWav(path);
            byte[] wav = File.ReadAllBytes(path);
            ReadOnlySpan<byte> b = wav;

            Assert.Equal(44 + (2 * samples.Length), wav.Length);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
            Assert.Equal(wav.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(b[4..]));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
            Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
            Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(b[16..]));
            Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(b[20..]));      // PCM
            Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(b[22..]));      // mono
            Assert.Equal(22050, BinaryPrimitives.ReadInt32LittleEndian(b[24..]));  // sample rate
            Assert.Equal(44100, BinaryPrimitives.ReadInt32LittleEndian(b[28..]));  // byte rate
            Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(b[32..]));      // block align
            Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(b[34..]));     // bits per sample
            Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
            Assert.Equal(2 * samples.Length, BinaryPrimitives.ReadInt32LittleEndian(b[40..]));

            short[] pcm = new short[samples.Length];

            for (int i = 0; i < pcm.Length; i++)
            {
                pcm[i] = BinaryPrimitives.ReadInt16LittleEndian(b[(44 + (2 * i))..]);
            }

            Assert.Equal([0, 16384, -16384, 32767, -32767, 32767, -32767, 8192, 0], pcm);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [MetalFact]
    public void SaveWav_OfARenderedClip_RoundTripsWithin16BitPrecision()
    {
        using ChipSynth synth = new(GraphicsDevice.GetDefault());
        using AudioClip clip = synth.Render(ChipPresets.Coin);
        using MemoryStream stream = new();
        clip.SaveWav(stream);
        byte[] wav = stream.ToArray();

        Assert.Equal(44 + (2 * clip.Length), wav.Length);
        Assert.Equal(48000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));

        for (int i = 0; i < clip.Length; i++)
        {
            short value = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44 + (2 * i)));
            Assert.Equal(clip.Samples[i], value / 32767f, 1f / 32767);
        }
    }

    [MetalFact]
    public void Clip_ReportsItsShape()
    {
        using AudioClip clip = AudioClip.FromSamples(GraphicsDevice.GetDefault(), new float[24000], 48000);
        Assert.Equal(24000, clip.Length);
        Assert.Equal(48000, clip.SampleRate);
        Assert.Equal(0.5, clip.Duration);
        Assert.False(clip.IsDisposed);
    }

    [MetalFact]
    public void FromSamples_RejectsBadInput()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Assert.Throws<ArgumentException>(() => AudioClip.FromSamples(device, [], 48000));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioClip.FromSamples(device, [0f], 100));
        Assert.Throws<ArgumentNullException>(() => AudioClip.FromSamples(null!, [0f], 48000));
    }

    [MetalFact]
    public void Dispose_MakesTheClipUnusable_AndIsIdempotent()
    {
        AudioClip clip = AudioClip.FromSamples(GraphicsDevice.GetDefault(), [0.1f, 0.2f], 48000);
        clip.Dispose();
        clip.Dispose();

        Assert.True(clip.IsDisposed);
        Assert.True(clip.Buffer.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => clip.Samples.Length);
        Assert.Throws<ObjectDisposedException>(() => clip.SaveWav(new MemoryStream()));
        Assert.False(clip.TryAcquire());
    }

    [MetalFact]
    public void Dispose_WhileLeased_DefersReleasingTheMemory()
    {
        AudioClip clip = AudioClip.FromSamples(GraphicsDevice.GetDefault(), [0.1f, 0.2f], 48000);
        Assert.True(clip.TryAcquire());
        Assert.True(clip.TryAcquire());

        clip.Dispose();
        Assert.True(clip.IsDisposed);
        Assert.False(clip.Buffer.IsDisposed);
        Assert.False(clip.TryAcquire());

        clip.ReleaseLease();
        Assert.False(clip.Buffer.IsDisposed);
        clip.ReleaseLease();
        Assert.True(clip.Buffer.IsDisposed);
    }

    [Fact]
    public void ToPcm16_ClampsAndRounds()
    {
        Assert.Equal(0, AudioClip.ToPcm16(0f));
        Assert.Equal(32767, AudioClip.ToPcm16(1f));
        Assert.Equal(-32767, AudioClip.ToPcm16(-1f));
        Assert.Equal(32767, AudioClip.ToPcm16(10f));
        Assert.Equal(-32767, AudioClip.ToPcm16(float.NegativeInfinity));
        Assert.Equal(0, AudioClip.ToPcm16(float.NaN));
        Assert.Equal(1, AudioClip.ToPcm16(0.6f / 32767));
    }
}
