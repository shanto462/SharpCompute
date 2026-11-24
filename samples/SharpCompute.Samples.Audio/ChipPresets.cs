// Original SharpCompute sample (MIT).
using System.Collections.Generic;

namespace SharpCompute.Samples.Audio;

/// <summary>A few ready-made sound effects and a short original loop, for demos and games.</summary>
public static class ChipPresets
{
    /// <summary>A rising pulse chirp whose duty sweeps from 25 % toward 45 %.</summary>
    public static ToneSpec Jump { get; } = new(Waveform.Pulse25, 280f, 640f, 0.14f, new Envelope(0.005f, 0.12f, 0.35f, 0.06f), Volume: 0.5f, DutySweep: 0.2f);

    /// <summary>A bright two-octave flick up.</summary>
    public static ToneSpec Coin { get; } = new(Waveform.Square, 988f, 1976f, 0.06f, new Envelope(0f, 0.05f, 0.7f, 0.22f), Volume: 0.3f);

    /// <summary>A falling saw zap.</summary>
    public static ToneSpec Laser { get; } = new(Waveform.Saw, 1600f, 180f, 0.22f, new Envelope(0f, 0.22f, 0.1f, 0.03f), Volume: 0.3f);

    /// <summary>Noise sliding down from a hiss to a rumble.</summary>
    public static ToneSpec Explosion { get; } = new(Waveform.Noise, 3000f, 60f, 0.55f, new Envelope(0f, 0.55f, 0f, 0.15f), Volume: 0.55f);

    /// <summary>A two-octave square sweep with vibrato.</summary>
    public static ToneSpec PowerUp { get; } = new(Waveform.Square, 220f, 880f, 0.5f, new Envelope(0.01f, 0.2f, 0.6f, 0.12f), Volume: 0.3f, VibratoHz: 14f, VibratoDepth: 0.04f);

    /// <summary>A short burst of noise for taking damage.</summary>
    public static ToneSpec Hit { get; } = new(Waveform.Noise, 6000f, 500f, 0.1f, new Envelope(0f, 0.1f, 0.2f, 0.05f), Volume: 0.45f);

    /// <summary>A tiny triangle blip for menus.</summary>
    public static ToneSpec Blip { get; } = new(Waveform.Triangle, 880f, 880f, 0.05f, new Envelope(0.002f, 0.03f, 0.5f, 0.03f), Volume: 0.5f);

    /// <summary>Every sound effect above, by name.</summary>
    public static IReadOnlyList<(string Name, ToneSpec Tone)> Effects { get; } =
    [
        ("jump", Jump), ("coin", Coin), ("laser", Laser), ("explosion", Explosion), ("powerup", PowerUp), ("hit", Hit), ("blip", Blip),
    ];

    /// <summary>
    /// An original four-bar loop in A minor (Am–F–C–G) at 150 BPM, 6.4 s: a 25 % pulse lead, a
    /// triangle bass, a 12.5 % pulse arpeggio and two noise drum channels.
    /// </summary>
    public static Song Loop { get; } = MakeLoop();

    private static Song MakeLoop()
    {
        // Chord roots and arpeggio tones per bar (MIDI): Am, F, C, G.
        int[] roots = [45, 41, 48, 43];
        int[][] arpeggios = [[69, 72, 76, 81], [65, 69, 72, 77], [67, 72, 76, 79], [67, 71, 74, 79]];

        List<Note> lead =
        [
            new(76, 0f, 1f), new(72, 1f, 0.5f), new(74, 1.5f, 0.5f), new(76, 2f, 1f), new(79, 3f, 0.5f), new(76, 3.5f, 0.5f),
            new(77, 4f, 1.5f), new(76, 5.5f, 0.5f), new(72, 6f, 1f), new(69, 7f, 1f),
            new(67, 8f, 0.5f), new(72, 8.5f, 0.5f), new(76, 9f, 1f), new(79, 10f, 0.5f), new(76, 10.5f, 0.5f), new(72, 11f, 1f),
            new(74, 12f, 1.5f), new(71, 13.5f, 0.5f), new(67, 14f, 1f), new(71, 15f, 0.5f), new(74, 15.5f, 0.5f),
        ];

        List<Note> bass = [];
        List<Note> arpeggio = [];
        List<Note> drums = [];
        List<Note> hats = [];

        for (int bar = 0; bar < 4; bar++)
        {
            for (int eighth = 0; eighth < 8; eighth++)
            {
                float beat = (bar * 4) + (eighth * 0.5f);
                bass.Add(new Note(roots[bar] + (eighth % 2 == 0 ? 0 : 12), beat, 0.4f));
                hats.Add(new Note(118, beat + 0.25f, 0.05f, eighth % 2 == 0 ? 0.5f : 0.8f));
            }

            for (int sixteenth = 0; sixteenth < 16; sixteenth++)
            {
                arpeggio.Add(new Note(arpeggios[bar][sixteenth % 4], (bar * 4) + (sixteenth * 0.25f), 0.2f, 0.7f + (0.3f * (sixteenth % 4 == 0 ? 1f : 0f))));
            }

            for (int beat = 0; beat < 4; beat++)
            {
                // Kick (a low noise rumble) on 1 and 3, snare (mid noise) on 2 and 4.
                drums.Add(beat % 2 == 0 ? new Note(40, (bar * 4) + beat, 0.12f) : new Note(88, (bar * 4) + beat, 0.1f, 0.8f));
            }
        }

        return new Song(150f, 16f,
        [
            new SongChannel(Waveform.Pulse25, new Envelope(0.01f, 0.12f, 0.6f, 0.08f), 0.3f, lead),
            new SongChannel(Waveform.Triangle, new Envelope(0.004f, 0.05f, 0.8f, 0.03f), 0.35f, bass),
            new SongChannel(Waveform.Pulse12, Envelope.Pluck, 0.12f, arpeggio),
            new SongChannel(Waveform.Noise, new Envelope(0f, 0.12f, 0f, 0.02f), 0.3f, drums),
            new SongChannel(Waveform.Noise, new Envelope(0f, 0.04f, 0f, 0.01f), 0.08f, hats),
        ]);
    }
}
