// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using SharpCompute.Samples.Audio;
using SharpCompute.Samples.Shooter.Game;

namespace SharpCompute.Samples.Shooter;

/// <summary>
/// Pulsewake's sound: effects for the arena's events and a driving loop, all synthesized on the GPU
/// once at startup (<see cref="ChipSynth"/>) and played through <see cref="AudioPlayer"/>. Without an
/// audio device (or with <c>--mute</c>) everything here is a silent no-op.
/// </summary>
public sealed class GameAudio : IDisposable
{
    /// <summary>The music's level under the effects: the mix is clamped, so it needs headroom.</summary>
    public const float MusicVolume = 0.42f;

    private const float MasterVolume = 0.7f;

    private readonly AudioPlayer? _player;
    private readonly Dictionary<GameEvent, AudioClip> _effects = [];
    private readonly AudioClip? _theme;
    private bool _gameOver;

    public GameAudio(GraphicsDevice device, bool mute)
    {
        ArgumentNullException.ThrowIfNull(device);

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
        _player = new AudioPlayer(voices: 24) { MasterVolume = MasterVolume };
        _player.PlayLoop(_theme, MusicVolume);
    }

    /// <summary>Sound effects for the arena's events: original tones, not presets.</summary>
    public static IReadOnlyList<(GameEvent Event, ToneSpec Tone)> Effects { get; } =
    [
        // A short, bright zap; quiet, because it repeats a dozen times a second.
        (GameEvent.Shot, new ToneSpec(Waveform.Pulse12, 1500f, 620f, 0.035f, new Envelope(0f, 0.03f, 0.2f, 0.02f), Volume: 0.12f, DutySweep: 0.2f)),
        // A tick of noise.
        (GameEvent.Hit, new ToneSpec(Waveform.Noise, 5200f, 2000f, 0.035f, new Envelope(0f, 0.03f, 0f, 0.02f), Volume: 0.2f)),
        // A wobbling drop as an orb comes apart.
        (GameEvent.Split, new ToneSpec(Waveform.Triangle, 700f, 330f, 0.14f, new Envelope(0f, 0.12f, 0.3f, 0.06f), Volume: 0.32f, VibratoHz: 22f, VibratoDepth: 0.05f)),
        // A crunching burst.
        (GameEvent.Kill, new ToneSpec(Waveform.Noise, 2600f, 110f, 0.3f, new Envelope(0f, 0.28f, 0f, 0.08f), Volume: 0.36f)),
        // A rising whoosh: a Lancer has committed.
        (GameEvent.Dash, new ToneSpec(Waveform.Saw, 160f, 950f, 0.2f, new Envelope(0.02f, 0.15f, 0.3f, 0.06f), Volume: 0.18f)),
        // A soft blip for a Weaver's shot.
        (GameEvent.Pulse, new ToneSpec(Waveform.Sine, 330f, 250f, 0.1f, new Envelope(0.005f, 0.08f, 0.2f, 0.05f), Volume: 0.28f)),
        // A hollow chime as a Pylon rings.
        (GameEvent.Ring, new ToneSpec(Waveform.Triangle, 220f, 440f, 0.3f, new Envelope(0.01f, 0.25f, 0.3f, 0.25f), Volume: 0.22f, VibratoHz: 7f, VibratoDepth: 0.02f)),
        // A long, deep blast.
        (GameEvent.Bomb, new ToneSpec(Waveform.Noise, 1400f, 40f, 1.1f, new Envelope(0f, 1.0f, 0f, 0.3f), Volume: 0.6f)),
        // A sinking, wavering fall.
        (GameEvent.Death, new ToneSpec(Waveform.Saw, 480f, 55f, 0.9f, new Envelope(0f, 0.8f, 0.2f, 0.2f), Volume: 0.3f, VibratoHz: 11f, VibratoDepth: 0.04f)),
        // A rising shimmer back into the arena.
        (GameEvent.Respawn, new ToneSpec(Waveform.Pulse25, 260f, 1040f, 0.35f, new Envelope(0.02f, 0.2f, 0.5f, 0.2f), Volume: 0.22f, VibratoHz: 16f, VibratoDepth: 0.015f, DutySweep: 0.2f)),
        // A two-octave rising sweep announcing a wave.
        (GameEvent.WaveStart, new ToneSpec(Waveform.Square, 196f, 784f, 0.4f, new Envelope(0.01f, 0.3f, 0.4f, 0.3f), Volume: 0.2f, VibratoHz: 9f, VibratoDepth: 0.01f)),
        // A slow fall to the bottom.
        (GameEvent.GameOver, new ToneSpec(Waveform.Square, 330f, 70f, 1.6f, new Envelope(0.02f, 1.4f, 0.3f, 0.5f), Volume: 0.25f, VibratoHz: 5f, VibratoDepth: 0.03f)),
    ];

    /// <summary>Silences everything (the M key) without stopping the music.</summary>
    public bool Muted
    {
        get => _player is { MasterVolume: 0f };
        set
        {
            if (_player is not null)
            {
                _player.MasterVolume = value ? 0f : MasterVolume;
            }
        }
    }

    /// <summary>Plays the sounds for this frame's events, panned by where they happen in the arena.</summary>
    public void Play(IReadOnlyList<GameEventAt> events, bool gameOver)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (_player is null)
        {
            return;
        }

        // One of each kind per frame: twenty hits at once would only be louder, not clearer.
        Span<bool> played = stackalloc bool[(int)GameEvent.GameOver + 1];

        foreach (GameEventAt e in events)
        {
            if (!played[(int)e.Event] && _effects.TryGetValue(e.Event, out AudioClip? clip))
            {
                played[(int)e.Event] = true;
                float pan = Math.Clamp(((e.Position.X / Tuning.Width) * 2f) - 1f, -0.7f, 0.7f);
                _player.Play(clip, 1f, pan);
            }
        }

        if (gameOver != _gameOver && _theme is not null)
        {
            _gameOver = gameOver;

            if (gameOver)
            {
                _player.StopLoop();
            }
            else
            {
                _player.PlayLoop(_theme, MusicVolume);
            }
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
    }

    /// <summary>
    /// "Pulsewake", an original 8-bar loop in E minor at 132 BPM: a kick on every beat, offbeat
    /// hats, a pumping pulse bass in octaves, a square arpeggio and a short pulse hook.
    /// </summary>
    public static Song Theme()
    {
        // Em, C, D, Bm, Em, C, Am, B (bass roots, MIDI).
        int[] roots = [40, 36, 38, 35, 40, 36, 45, 35];
        int[][] chords = [[64, 67, 71], [60, 64, 67], [62, 66, 69], [59, 62, 66], [64, 67, 71], [60, 64, 67], [57, 60, 64], [59, 63, 66]];

        List<Note> kick = [], hats = [], bass = [], arpeggio = [], hook = [];

        for (int bar = 0; bar < 8; bar++)
        {
            float start = bar * 4f;

            for (int beat = 0; beat < 4; beat++)
            {
                kick.Add(new Note(28, start + beat, 0.12f));
                hats.Add(new Note(118, start + beat + 0.5f, 0.04f, beat % 2 == 1 ? 1f : 0.7f));
            }

            for (int eighth = 0; eighth < 8; eighth++)
            {
                bass.Add(new Note(roots[bar] + (eighth % 2 == 1 ? 12 : 0), start + (eighth * 0.5f), 0.4f, eighth % 2 == 0 ? 1f : 0.75f));
            }

            int[] chord = chords[bar];

            for (int sixteenth = 0; sixteenth < 16; sixteenth++)
            {
                int pitch = chord[sixteenth % 3] + ((sixteenth / 4) % 2 == 1 ? 12 : 0);
                arpeggio.Add(new Note(pitch, start + (sixteenth * 0.25f), 0.2f, sixteenth % 4 == 0 ? 0.9f : 0.55f));
            }
        }

        // A short call in bars 1-2, answered in bars 5-6.
        (int Pitch, float Start, float Length)[] line =
        [
            (76, 0f, 0.5f), (79, 0.75f, 0.5f), (83, 1.5f, 1f), (81, 3f, 0.5f), (79, 3.5f, 0.5f), (76, 4f, 2f),
            (76, 16f, 0.5f), (79, 16.75f, 0.5f), (84, 17.5f, 1f), (83, 19f, 0.5f), (81, 19.5f, 0.5f), (79, 20f, 1.5f), (78, 21.5f, 0.5f),
            (75, 28f, 1f), (78, 29f, 1f), (83, 30f, 2f),
        ];

        foreach ((int pitch, float start, float length) in line)
        {
            hook.Add(new Note(pitch, start, length, 0.85f));
        }

        return new Song(132f, 32f,
        [
            new SongChannel(Waveform.Sine, new Envelope(0f, 0.11f, 0f, 0.02f), 0.5f, kick),
            new SongChannel(Waveform.Noise, new Envelope(0f, 0.03f, 0f, 0.01f), 0.07f, hats),
            new SongChannel(Waveform.Pulse25, new Envelope(0.002f, 0.12f, 0.4f, 0.04f), 0.16f, bass),
            new SongChannel(Waveform.Square, new Envelope(0f, 0.08f, 0.15f, 0.06f), 0.05f, arpeggio),
            new SongChannel(Waveform.Pulse25, new Envelope(0.01f, 0.25f, 0.6f, 0.15f), 0.11f, hook),
        ]);
    }
}
