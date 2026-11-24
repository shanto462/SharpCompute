using System;
using System.Linq;
using SharpCompute.Samples.Audio;

namespace SharpCompute.Tests.Audio;

/// <summary>
/// The player's mixing runs through <c>VoiceMixer</c> directly, called the way the audio thread
/// calls it, so nothing here ever reaches a speaker. Player tests use an inactive player, or open
/// a real queue only to check it starts and stops (silently: nothing is played on it).
/// </summary>
public sealed unsafe class AudioPlayerTests
{
    private static AudioClip Clip(params float[] samples) => AudioClip.FromSamples(GraphicsDevice.GetDefault(), samples, 48000);

    private static float[] Mix(VoiceMixer mixer, int frames)
    {
        float[] output = new float[frames * 2];

        fixed (float* p = output)
        {
            mixer.Mix(p, frames);
        }

        return output;
    }

    private static float[] Left(float[] stereo) => [.. Enumerable.Range(0, stereo.Length / 2).Select(i => stereo[2 * i])];

    private static float[] Right(float[] stereo) => [.. Enumerable.Range(0, stereo.Length / 2).Select(i => stereo[(2 * i) + 1])];

    [MetalFact]
    public void Mixer_NeverOutputsNaN_OrValuesBeyondFullScale()
    {
        using AudioClip clip = Clip(float.NaN, float.PositiveInfinity, float.NegativeInfinity, 3f, -3f, 0.25f);
        VoiceMixer mixer = new(48000, 4);
        mixer.Play(clip, 1f, 0f);

        float[] output = Left(Mix(mixer, 6));

        Assert.All(output, static v => Assert.True(float.IsFinite(v) && v is >= -1f and <= 1f, $"{v}"));
        Assert.Equal(0f, output[0]);
        Assert.Equal(1f, output[3]);
        Assert.Equal(-1f, output[4]);
    }

    [MetalFact]
    public void Mixer_PlaysAVoiceOnceWithVolume()
    {
        using AudioClip clip = Clip(0.1f, 0.2f, 0.3f);
        VoiceMixer mixer = new(48000, 4);
        mixer.Play(clip, 0.5f, 0f);

        float[] output = Mix(mixer, 5);
        Assert.Equal([0.05f, 0.1f, 0.15f, 0f, 0f], Left(output), (a, b) => Math.Abs(a - b) < 1e-7f);
        Assert.Equal(Left(output), Right(output));

        // Finished: the request is retired and the clip's lease returned.
        Assert.Equal(0, mixer.OutstandingCount);
        Assert.Equal(new float[8], Mix(mixer, 4));
    }

    [MetalTheory]
    [InlineData(0f, 0.25f, 0.25f)]
    [InlineData(1f, 0f, 0.25f)]
    [InlineData(-1f, 0.25f, 0f)]
    [InlineData(-0.5f, 0.25f, 0.125f)]
    [InlineData(0.5f, 0.125f, 0.25f)]
    [InlineData(5f, 0f, 0.25f)]
    [InlineData(float.NaN, 0.25f, 0.25f)]
    public void Mixer_Pans(float pan, float left, float right)
    {
        // The near side keeps the full volume; the far side fades out linearly.
        using AudioClip clip = Clip(0.5f);
        VoiceMixer mixer = new(48000, 4);
        mixer.Play(clip, 0.5f, pan);
        Assert.Equal([left, right], Mix(mixer, 1));
    }

    [MetalFact]
    public void Mixer_StealsTheOldestVoiceWhenFull()
    {
        using AudioClip a = Clip(0.1f, 0.1f, 0.1f, 0.1f);
        using AudioClip b = Clip(0.01f, 0.01f, 0.01f, 0.01f);
        using AudioClip c = Clip(0.001f, 0.001f, 0.001f, 0.001f);
        VoiceMixer mixer = new(48000, 2);

        // Both voices busy (A playing, B playing), then C steals A's voice.
        mixer.Play(a, 1f, 0f);
        mixer.Play(b, 1f, 0f);
        Assert.Equal(0.11f, Mix(mixer, 1)[0], 1e-6f);

        mixer.Play(c, 1f, 0f);
        Assert.Equal(0.011f, Mix(mixer, 1)[0], 1e-6f);

        // A request replaced before the audio thread took it never plays.
        VoiceMixer one = new(48000, 1);
        one.Play(a, 1f, 0f);
        one.Play(b, 1f, 0f);
        Assert.Equal(0.01f, Mix(one, 1)[0], 1e-6f);
        Assert.Equal(1, one.OutstandingCount);
    }

    [MetalFact]
    public void Mixer_ReusesFinishedVoicesBeforeStealing()
    {
        using AudioClip shortClip = Clip(0.1f);
        using AudioClip longClip = Clip(0.01f, 0.01f, 0.01f, 0.01f);
        VoiceMixer mixer = new(48000, 2);

        mixer.Play(shortClip, 1f, 0f);
        mixer.Play(longClip, 1f, 0f);
        Mix(mixer, 2); // the short clip finishes

        mixer.Play(shortClip, 1f, 0f); // takes the free voice, so the long one keeps playing
        Assert.Equal(0.11f, Mix(mixer, 1)[0], 1e-6f);
    }

    [MetalFact]
    public void Mixer_LoopsSeamlessly_AndStops()
    {
        using AudioClip loop = Clip(0.1f, 0.2f, 0.3f);
        using AudioClip other = Clip(0.5f);
        VoiceMixer mixer = new(48000, 4);

        mixer.PlayLoop(loop, 1f);
        Assert.Equal([0.1f, 0.2f, 0.3f, 0.1f, 0.2f, 0.3f, 0.1f], Left(Mix(mixer, 7)), (x, y) => Math.Abs(x - y) < 1e-7f);
        Assert.Equal([0.2f, 0.3f], Left(Mix(mixer, 2)), (x, y) => Math.Abs(x - y) < 1e-7f);

        // A new loop replaces the old one from its start.
        mixer.PlayLoop(other, 0.5f);
        Assert.Equal([0.25f, 0.25f], Left(Mix(mixer, 2)));

        mixer.StopLoop();
        Assert.Equal(new float[6], Mix(mixer, 3));
        Assert.Equal(0, mixer.OutstandingCount);
    }

    [MetalFact]
    public void Mixer_AppliesMasterVolume_AndClamps()
    {
        using AudioClip clip = Clip(0.9f, 0.9f);
        VoiceMixer mixer = new(48000, 4);
        mixer.Play(clip, 1f, 0f);
        mixer.Play(clip, 1f, 0f);
        Assert.Equal(1f, Mix(mixer, 1)[0]);

        mixer.MasterVolume = 0.25f;
        Assert.Equal(0.45f, Mix(mixer, 1)[0], 1e-6f);
    }

    [MetalFact]
    public void Mixer_ResamplesOtherRates()
    {
        using AudioClip clip = AudioClip.FromSamples(GraphicsDevice.GetDefault(), [0f, 0.4f, 0.8f], 24000);
        VoiceMixer mixer = new(48000, 1);
        mixer.Play(clip, 1f, 0f);

        Assert.Equal([0f, 0.2f, 0.4f, 0.6f, 0.8f, 0.4f, 0f], Left(Mix(mixer, 7)), (x, y) => Math.Abs(x - y) < 1e-6f);
    }

    [MetalFact]
    public void Mixer_KeepsADisposedClipAliveUntilItsVoiceEnds()
    {
        AudioClip clip = Clip(0.1f, 0.2f, 0.3f, 0.4f);
        VoiceMixer mixer = new(48000, 2);
        mixer.Play(clip, 1f, 0f);
        Assert.Equal(0.1f, Mix(mixer, 1)[0]);

        clip.Dispose();
        Assert.Throws<ObjectDisposedException>(() => mixer.Play(clip, 1f, 0f));
        Assert.False(clip.Buffer.IsDisposed);
        Assert.Equal([0.2f, 0.3f, 0.4f, 0f], Left(Mix(mixer, 4)));

        // The voice has finished; the lease comes back at the next game-side call.
        Assert.Equal(0, mixer.OutstandingCount);
        Assert.True(clip.Buffer.IsDisposed);
    }

    [MetalFact]
    public void Mixer_ReleaseAll_ReturnsEveryLease_AndIgnoresLaterCalls()
    {
        AudioClip clip = Clip(0.1f, 0.2f);
        VoiceMixer mixer = new(48000, 2);
        mixer.Play(clip, 1f, 0f);
        mixer.PlayLoop(clip, 1f);
        clip.Dispose();
        Assert.False(clip.Buffer.IsDisposed);

        mixer.ReleaseAll();
        Assert.True(clip.Buffer.IsDisposed);
        Assert.Equal(0, mixer.OutstandingCount);

        using AudioClip other = Clip(0.5f);
        mixer.Play(other, 1f, 0f);
        mixer.PlayLoop(other, 1f);
        mixer.StopLoop();
        Assert.Equal(0, mixer.OutstandingCount);
    }

    [MetalFact]
    public void InactivePlayer_IsASilentNoOp()
    {
        using AudioClip clip = Clip(0.5f, 0.5f);
        AudioPlayer player = new(48000, 8, openOutput: false);

        Assert.False(player.IsActive);
        Assert.Equal(48000, player.SampleRate);
        Assert.Equal(8, player.Voices);

        player.Play(clip);
        player.Play(clip, 0.3f, -1f);
        player.PlayLoop(clip, 0.5f);
        player.StopLoop();
        player.StopLoop();
        player.MasterVolume = 0.4f;
        Assert.Equal(0.4f, player.MasterVolume);
        player.MasterVolume = -1f;
        Assert.Equal(0f, player.MasterVolume);

        player.Dispose();
        player.Dispose();
        player.Play(clip);
        player.StopLoop();

        // No lease was ever taken.
        clip.Dispose();
        Assert.True(clip.Buffer.IsDisposed);
    }

    [MetalFact]
    public void Player_ValidatesArgumentsEvenWhenInactive()
    {
        AudioClip disposed = Clip(0.5f);
        disposed.Dispose();
        using AudioPlayer player = new(48000, 4, openOutput: false);

        Assert.Throws<ArgumentNullException>(() => player.Play(null!));
        Assert.Throws<ArgumentNullException>(() => player.PlayLoop(null!));
        Assert.Throws<ObjectDisposedException>(() => player.Play(disposed));
        Assert.Throws<ObjectDisposedException>(() => player.PlayLoop(disposed));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioPlayer(48000, 0, openOutput: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioPlayer(48000, 257, openOutput: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioPlayer(1000, 4, openOutput: false));
    }

    [Fact]
    public void IsSupported_AnswersWithoutThrowing()
    {
        bool supported = AudioPlayer.IsSupported;
        Assert.Equal(supported, AudioPlayer.IsSupported);
    }

    [AudioFact]
    public void RealPlayer_OpensAndClosesSilently()
    {
        // Nothing is played: the queue runs the mixer, which outputs silence, and is torn down.
        using AudioPlayer player = new();
        Assert.Equal(AudioPlayer.IsSupported, player.IsActive);
        player.MasterVolume = 0.5f;

        if (player.IsActive)
        {
            // The queue really runs: AudioToolbox calls back for more than the primed buffers.
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

            while (player.MixedFrames <= 3 * AudioPlayer.BufferFrames && clock.Elapsed < TimeSpan.FromSeconds(5))
            {
                System.Threading.Thread.Sleep(20);
            }

            Assert.True(player.MixedFrames > 3 * AudioPlayer.BufferFrames, $"only {player.MixedFrames} frames mixed");
        }

        player.StopLoop();
        player.Dispose();
        Assert.False(player.IsActive);
    }
}

/// <summary>
/// A test that starts a real audio output queue. Opt-in, so a default <c>dotnet test</c> never
/// wakes or switches the machine's audio output: runs only with <c>SHARPCOMPUTE_AUDIO_TESTS=1</c>
/// and never in CI.
/// </summary>
public sealed class AudioFactAttribute : FactAttribute
{
    public AudioFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SHARPCOMPUTE_AUDIO_TESTS") != "1" ||
            Environment.GetEnvironmentVariable("CI") is { Length: > 0 } ||
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is { Length: > 0 })
        {
            Skip = "Audio device tests are opt-in: set SHARPCOMPUTE_AUDIO_TESTS=1 (never run in CI).";
        }
    }
}
