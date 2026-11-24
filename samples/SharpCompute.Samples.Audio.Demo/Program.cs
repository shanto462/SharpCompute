// Original SharpCompute sample (MIT).
//
// Renders the preset sound effects and the preset chiptune loop on the GPU, then plays them for
// about 10 seconds at half volume (or writes WAV files).
//
//   SharpCompute.Samples.Audio.Demo                  play: SFX one by one, then the loop with SFX on top
//   SharpCompute.Samples.Audio.Demo --wav <dir>      write <dir>/<name>.wav instead of playing
//   SharpCompute.Samples.Audio.Demo --seconds 6      play for 6 s instead of 10
//   SharpCompute.Samples.Audio.Demo --volume 0.3     master volume (at most 0.5)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using SharpCompute;
using SharpCompute.Samples.Audio;

string? wavDir = null;
double seconds = 10;
float volume = 0.5f;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--wav" when i + 1 < args.Length:
            wavDir = args[++i];
            break;
        case "--seconds" when i + 1 < args.Length:
            seconds = Math.Clamp(double.Parse(args[++i], CultureInfo.InvariantCulture), 1, 60);
            break;
        case "--volume" when i + 1 < args.Length:
            volume = Math.Clamp(float.Parse(args[++i], CultureInfo.InvariantCulture), 0f, 0.5f);
            break;
        default:
            Console.Error.WriteLine($"unknown option: {args[i]} (options: --wav <dir>, --seconds <s>, --volume <0..0.5>)");
            return 2;
    }
}

if (!GraphicsDevice.IsSupported)
{
    Console.Error.WriteLine("No Metal device is available.");
    return 1;
}

GraphicsDevice device = GraphicsDevice.GetDefault();
using ChipSynth synth = new(device);
List<(string Name, AudioClip Clip)> clips = [];

// Warm up (compiles the kernel) so the timings below are the renders alone.
synth.Render(ChipPresets.Blip).Dispose();

Console.WriteLine($"Rendering on {device.Name} at {synth.SampleRate} Hz, one GPU thread per sample:");

foreach ((string name, ToneSpec tone) in ChipPresets.Effects)
{
    clips.Add((name, Timed(name, () => synth.Render(tone))));
}

AudioClip loop = Timed("loop", () => synth.Render(ChipPresets.Loop));
clips.Add(("loop", loop));

try
{
    if (wavDir is null && !AudioPlayer.IsSupported)
    {
        wavDir = Path.Combine("demo-output", "audio");
        Console.WriteLine($"No audio output (AudioPlayer.IsSupported is false); writing WAV files to {wavDir} instead.");
    }

    if (wavDir is not null)
    {
        Directory.CreateDirectory(wavDir);

        foreach ((string name, AudioClip clip) in clips)
        {
            string path = Path.Combine(wavDir, name + ".wav");
            clip.SaveWav(path);
            Console.WriteLine($"  wrote {path}");
        }

        return 0;
    }

    Play(clips, loop, seconds, volume);
    return 0;
}
finally
{
    foreach ((_, AudioClip clip) in clips)
    {
        clip.Dispose();
    }
}

AudioClip Timed(string name, Func<AudioClip> render)
{
    long start = Stopwatch.GetTimestamp();
    AudioClip clip = render();
    TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
    Console.WriteLine($"  {name,-10} {clip.Duration,6:0.00} s  {clip.Length,8:N0} samples  {elapsed.TotalMilliseconds,6:0.00} ms");
    return clip;
}

static void Play(List<(string Name, AudioClip Clip)> clips, AudioClip loop, double seconds, float volume)
{
    using AudioPlayer player = new();
    player.MasterVolume = volume;
    Console.WriteLine($"Playing for {seconds:0} s at volume {volume:0.##} (Ctrl+C to stop)...");
    Stopwatch clock = Stopwatch.StartNew();

    // The sound effects one by one, panned across the stereo field, for the first 40 % of the time.
    double effectsEnd = seconds * 0.4;
    int effects = clips.Count - 1;

    for (int i = 0; i < effects && clock.Elapsed.TotalSeconds < effectsEnd; i++)
    {
        (string name, AudioClip clip) = clips[i];
        float pan = effects > 1 ? -0.6f + (1.2f * i / (effects - 1)) : 0f;
        Console.WriteLine($"  {clock.Elapsed.TotalSeconds,5:0.0} s  {name}");
        player.Play(clip, 1f, pan);
        Thread.Sleep(TimeSpan.FromSeconds(Math.Max(clip.Duration + 0.1, effectsEnd / effects)));
    }

    // Then the loop, with a jump or a coin on top now and then.
    Console.WriteLine($"  {clock.Elapsed.TotalSeconds,5:0.0} s  loop");
    player.PlayLoop(loop, 0.8f);
    int n = 0;

    while (clock.Elapsed.TotalSeconds < seconds - 0.6)
    {
        Thread.Sleep(600);
        (string name, AudioClip clip) = clips[n++ % 2 == 0 ? 0 : 1];
        player.Play(clip, 0.6f, n % 3 == 0 ? -0.5f : 0.5f);
    }

    player.StopLoop();
    Thread.Sleep(300);
    Console.WriteLine($"  {clock.Elapsed.TotalSeconds,5:0.0} s  done");
}
