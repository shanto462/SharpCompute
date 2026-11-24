// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using SharpCompute.Samples.Audio;
using SharpCompute.Samples.Platformer.Game;

namespace SharpCompute.Samples.Platformer;

/// <summary>
/// Glowfall's sound: a few effects and a slow cave theme, all synthesized on the GPU once at
/// startup (<see cref="ChipSynth"/>) and played through <see cref="AudioPlayer"/>. Without an
/// audio device (or with <c>--mute</c>) everything here is a silent no-op.
/// </summary>
public sealed class GameAudio : IDisposable
{
    private readonly AudioPlayer? _player;
    private readonly Dictionary<GameEvent, AudioClip> _effects = [];
    private readonly AudioClip? _theme;
    private readonly AudioClip? _finale;
    private bool _finaleStarted;

    public GameAudio(GraphicsDevice device, bool mute)
    {
        if (mute || !AudioPlayer.IsSupported)
        {
            return;
        }

        using ChipSynth synth = new(device);

        foreach ((GameEvent e, ToneSpec tone) in Effects)
        {
            _effects[e] = synth.Render(tone);
        }

        _theme = synth.Render(Theme());
        _finale = synth.Render(Finale());
        _player = new AudioPlayer { MasterVolume = 0.5f };
        _player.PlayLoop(_theme, 0.55f);
    }

    /// <summary>Sound effects for the game's events: original tones, not presets.</summary>
    public static IReadOnlyList<(GameEvent Event, ToneSpec Tone)> Effects { get; } =
    [
        // A soft upward hop.
        (GameEvent.Jump, new ToneSpec(Waveform.Pulse25, 330f, 520f, 0.09f, new Envelope(0.003f, 0.07f, 0.3f, 0.04f), Volume: 0.28f, DutySweep: 0.15f)),
        // A muffled thump.
        (GameEvent.Land, new ToneSpec(Waveform.Noise, 900f, 200f, 0.05f, new Envelope(0f, 0.05f, 0f, 0.03f), Volume: 0.18f)),
        // A glassy twinkle: a perfect fifth up.
        (GameEvent.Mote, new ToneSpec(Waveform.Triangle, 1318.5f, 1975.5f, 0.08f, new Envelope(0f, 0.06f, 0.5f, 0.18f), Volume: 0.35f, VibratoHz: 18f, VibratoDepth: 0.01f)),
        // A warm swell as a lantern catches.
        (GameEvent.Lantern, new ToneSpec(Waveform.Square, 196f, 392f, 0.55f, new Envelope(0.05f, 0.3f, 0.5f, 0.45f), Volume: 0.25f, VibratoHz: 6f, VibratoDepth: 0.015f, DutySweep: -0.2f)),
        // A crunch.
        (GameEvent.Stomp, new ToneSpec(Waveform.Noise, 2400f, 600f, 0.09f, new Envelope(0f, 0.08f, 0.1f, 0.04f), Volume: 0.32f)),
        // A sad fall.
        (GameEvent.Hurt, new ToneSpec(Waveform.Saw, 520f, 110f, 0.45f, new Envelope(0f, 0.4f, 0.2f, 0.12f), Volume: 0.22f, VibratoHz: 9f, VibratoDepth: 0.03f)),
        // A rising shimmer when the gate opens.
        (GameEvent.GateOpen, new ToneSpec(Waveform.Pulse12, 440f, 1760f, 0.9f, new Envelope(0.02f, 0.4f, 0.6f, 0.5f), Volume: 0.22f, VibratoHz: 11f, VibratoDepth: 0.02f)),
        // Through the gate.
        (GameEvent.LevelComplete, new ToneSpec(Waveform.Triangle, 523.25f, 1046.5f, 0.6f, new Envelope(0.01f, 0.3f, 0.6f, 0.6f), Volume: 0.4f)),
    ];

    /// <summary>Silences everything (the M key) without stopping the music.</summary>
    public bool Muted
    {
        get => _player is { MasterVolume: 0f };
        set
        {
            if (_player is not null)
            {
                _player.MasterVolume = value ? 0f : 0.5f;
            }
        }
    }

    /// <summary>Plays the sounds for this frame's events; <paramref name="cameraX"/> pans them by where they happen on screen.</summary>
    public void Play(IReadOnlyList<GameEventAt> events, float cameraX, bool finished)
    {
        if (_player is null)
        {
            return;
        }

        int played = 0;

        foreach (GameEventAt e in events)
        {
            // Several motes in one frame would just be louder, not clearer.
            if (played++ < 4 && _effects.TryGetValue(e.Event, out AudioClip? clip))
            {
                float pan = Math.Clamp(((e.Position.X - cameraX) / GameSession.ViewWidth * 2f) - 1f, -0.8f, 0.8f);
                _player.Play(clip, 1f, pan);
            }
        }

        if (finished && !_finaleStarted && _finale is not null)
        {
            _finaleStarted = true;
            _player.PlayLoop(_finale, 0.6f);
        }
        else if (!finished && _finaleStarted && _theme is not null)
        {
            _finaleStarted = false;
            _player.PlayLoop(_theme, 0.55f);
        }
    }

    public void Dispose()
    {
        // The player first, so no voice is reading a clip when the clips go.
        _player?.Dispose();

        foreach (AudioClip clip in _effects.Values)
        {
            clip.Dispose();
        }

        _theme?.Dispose();
        _finale?.Dispose();
    }

    /// <summary>
    /// "Lantern Deep", an original 8-bar theme in D minor at 92 BPM: a triangle bass on roots and
    /// fifths, a soft triangle arpeggio, a sparse pulse melody and a quiet noise "drip".
    /// </summary>
    public static Song Theme()
    {
        // Dm, Bb, F, C, Dm, Gm, Bb, A (MIDI roots, two octaves down).
        int[] roots = [38, 34, 41, 36, 38, 43, 34, 33];
        int[][] chords = [[62, 65, 69], [58, 62, 65], [65, 69, 72], [60, 64, 67], [62, 65, 69], [55, 58, 62], [58, 62, 65], [57, 61, 64]];

        List<Note> bass = [], arpeggio = [], melody = [], drips = [];

        for (int bar = 0; bar < 8; bar++)
        {
            float start = bar * 4f;
            bass.Add(new Note(roots[bar], start, 1.6f));
            bass.Add(new Note(roots[bar] + 7, start + 2f, 1.6f, 0.8f));

            for (int i = 0; i < 8; i++)
            {
                int[] chord = chords[bar];
                int pitch = chord[i % 3] + (i >= 6 ? 12 : 0);
                arpeggio.Add(new Note(pitch, start + (i * 0.5f), 0.45f, i % 4 == 0 ? 0.9f : 0.6f));
            }

            if (bar % 2 == 1)
            {
                drips.Add(new Note(100, start + 3.25f, 0.05f, 0.6f));
            }
        }

        // A slow, sparse line over the top.
        (int Pitch, float Start, float Length)[] line =
        [
            (74, 0f, 3f), (72, 3f, 1f), (70, 4f, 2f), (69, 6f, 2f),
            (72, 8f, 3f), (69, 11f, 1f), (67, 12f, 4f),
            (74, 16f, 2f), (77, 18f, 2f), (74, 20f, 3f), (70, 23f, 1f),
            (69, 24f, 3f), (67, 27f, 1f), (69, 28f, 4f),
        ];

        foreach ((int pitch, float start, float length) in line)
        {
            melody.Add(new Note(pitch, start, length, 0.8f));
        }

        return new Song(92f, 32f,
        [
            new SongChannel(Waveform.Triangle, new Envelope(0.02f, 0.4f, 0.6f, 0.3f), 0.32f, bass),
            new SongChannel(Waveform.Triangle, new Envelope(0.005f, 0.25f, 0.15f, 0.25f), 0.16f, arpeggio),
            new SongChannel(Waveform.Pulse25, new Envelope(0.15f, 0.6f, 0.5f, 0.6f), 0.1f, melody),
            new SongChannel(Waveform.Noise, new Envelope(0f, 0.04f, 0f, 0.02f), 0.05f, drips),
        ]);
    }

    /// <summary>A short, brighter loop in D major for the end screen.</summary>
    public static Song Finale()
    {
        int[][] chords = [[62, 66, 69], [67, 71, 74], [69, 73, 76], [62, 66, 69]];
        List<Note> arpeggio = [], bass = [];

        for (int bar = 0; bar < 4; bar++)
        {
            bass.Add(new Note(chords[bar][0] - 24, bar * 4f, 3.5f));

            for (int i = 0; i < 12; i++)
            {
                arpeggio.Add(new Note(chords[bar][i % 3] + (i / 3 % 2 == 1 ? 12 : 0), (bar * 4f) + (i / 3f), 0.3f, 0.7f));
            }
        }

        return new Song(110f, 16f,
        [
            new SongChannel(Waveform.Triangle, new Envelope(0.02f, 0.3f, 0.7f, 0.3f), 0.3f, bass),
            new SongChannel(Waveform.Pulse25, Envelope.Pluck, 0.14f, arpeggio),
        ]);
    }
}
