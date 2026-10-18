using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>The game's pieces of music: the title, one per room, and the two endings.</summary>
    public enum MusicTrack
    {
        None,
        /// <summary>The theme, for the title screen (and after the results jingle).</summary>
        Title,
        /// <summary>Chapter 1: bright and busy, the safest room.</summary>
        AssemblyFloor,
        /// <summary>Chapter 2: colourful and exposed.</summary>
        PaintingRoom,
        /// <summary>Chapter 3: cramped and shadowy, with the Captain hunting.</summary>
        StorageArea,
        /// <summary>Chapter 4: the boss fight against Factory OS itself.</summary>
        ControlRoom,
        /// <summary>A short rising fanfare: the factory was shut down.</summary>
        WinJingle,
        /// <summary>A short falling "game over" phrase: Unit 047 was recalled.</summary>
        LoseJingle,
    }

    /// <summary>
    /// Writes the game's music, note by note, as plain samples. No audio files, so nothing to
    /// license or store in LFS, the same approach as the voice blips.
    /// </summary>
    /// <remarks>
    /// <para>Each room has its own piece, matched to its mood in Story.md: the Assembly Floor is
    /// a bright four-on-the-floor run; the Painting Room is a colourful funk groove with a
    /// Lydian lift; the Storage Vault creeps over a spy bass line; the Control Room, where the
    /// cores are the fight against the building itself, is a boss fight: a saw lead in the
    /// harmonic minor doubled an octave down, a galloping 16th-note bass, double kicks, power
    /// hits and crashes, with Factory OS's computer ticking underneath and its alarm siren.</para>
    /// <para>The melodies are written as scores, one 16th note per token (see
    /// <see cref="Parse"/>), so each tune reads like a tracker pattern. The bass, stabs,
    /// arpeggios and drums are generated from each bar's chord.</para>
    /// <para>The looping tracks are exactly eight bars of 4/4, and notes whose tails run past
    /// the end wrap round to the start, so the loop has no gap or click at the seam. The
    /// jingles do not loop and end in silence. The output is deterministic (the percussion
    /// noise has a fixed seed) and soft-clipped. Every piece is scaled to the same loudness
    /// (RMS), with the peak kept under 0.8, so a sparse room is not quieter than a busy one.</para>
    /// </remarks>
    public static class MusicComposer
    {
        public const int SampleRate = 44100;

        /// <summary>The loudness (RMS) every piece is scaled to, peaks permitting.</summary>
        public const float TargetRms = 0.15f;

        const int BeatsPerBar = 4;
        const int StepsPerBar = 16;
        const int Bars = 8;

        /// <summary>True for the tracks that loop.</summary>
        public static bool Loops(MusicTrack track) => Tempo(track) > 0f;

        /// <summary>Beats per minute of a looping track (0 for the jingles).</summary>
        public static float Tempo(MusicTrack track)
        {
            switch (track)
            {
                case MusicTrack.Title: return 120f;
                case MusicTrack.AssemblyFloor: return 140f;
                case MusicTrack.PaintingRoom: return 128f;
                case MusicTrack.StorageArea: return 124f;
                case MusicTrack.ControlRoom: return 160f;
                default: return 0f;
            }
        }

        /// <summary>Samples in one loop of a looping track: exactly eight bars.</summary>
        public static int LoopLength(MusicTrack track) =>
            Mathf.RoundToInt(Bars * BeatsPerBar * 60f / Tempo(track) * SampleRate);

        /// <summary>The track as mono samples at <see cref="SampleRate"/>; empty for <see cref="MusicTrack.None"/>.</summary>
        public static float[] Compose(MusicTrack track)
        {
            switch (track)
            {
                case MusicTrack.Title: return Finish(Title());
                case MusicTrack.AssemblyFloor: return Finish(AssemblyFloor());
                case MusicTrack.PaintingRoom: return Finish(PaintingRoom());
                case MusicTrack.StorageArea: return Finish(StorageArea());
                case MusicTrack.ControlRoom: return Finish(ControlRoom());
                case MusicTrack.WinJingle: return Finish(Win());
                case MusicTrack.LoseJingle: return Finish(Lose());
                default: return new float[0];
            }
        }

        // ---- The scores --------------------------------------------------------------------
        // One token per 16th note: a note ("E5", "F#4", "Bb3"), "-" to hold the note before,
        // "." to rest. "|" marks the bars and is skipped. Each loop's score is 8 bars of 16.

        // A minor: Am F C G | Am F G E.
        const string TitleTune =
            "A4 - - . C5 - E5 - D5 - C5 - B4 - C5 - | A4 - - - . . F4 - A4 - C5 - F5 - E5 - | " +
            "E5 - - - G5 - E5 - D5 - C5 - D5 - E5 - | D5 - - - - - B4 - G4 - - - . . . . | " +
            "A4 - - . C5 - E5 - A5 - G5 - E5 - C5 - | F5 - - - E5 - C5 - A4 - C5 - F5 - A5 - | " +
            "G5 - - - F5 - E5 - D5 - - - B4 - D5 - | E5 - - - - - - - G#4 - B4 - D5 - E5 -";

        // D major with a rock flat seventh: D C G D | D C G A.
        const string AssemblyTune =
            "D5 - F#5 - A5 - - F#5 . . A5 - B5 - A5 - | G5 - - E5 - - C5 - . . E5 - G5 - E5 - | " +
            "D5 - - B4 - - G4 - A4 - B4 - D5 - E5 - | F#5 - - - - - - - . . A5 - F#5 - E5 - | " +
            "D5 - F#5 - A5 - - D6 . . C6 - A5 - F#5 - | G5 - - E5 - - G5 - C6 - - - B5 - G5 - | " +
            "B5 - - A5 - - G5 - A5 - B5 - D6 - B5 - | A5 - - - - - . . A4 - C#5 - E5 - G5 -";

        // F Lydian (the B natural is the lift): F G Em Am | F G C C.
        const string PaintingTune =
            "C5 - A4 C5 . F5 - E5 . C5 - - A4 - G4 - | B4 - D5 - G5 - - . A5 - G5 - D5 - B4 - | " +
            "E5 - - G5 . B5 - A5 . G5 - E5 - D5 B4 - | C5 - - - - - A4 - . . E5 - D5 - C5 - | " +
            "F5 - A5 F5 . C6 - A5 . F5 - - C5 - D5 - | D5 - B4 D5 . G5 - B5 . A5 - G5 E5 - D5 - | " +
            "E5 - - G5 - - C6 - B5 - G5 - E5 - C5 - | D5 - - - E5 - - - G5 - - - A5 - G5 -";

        // E minor, creeping and chromatic: Em Em C B | Em Em C B.
        const string StorageTune =
            ". . . . E5 - - - G5 - F#5 - E5 - . . | . . B4 - . . B4 - C5 - B4 - Bb4 - B4 - | " +
            ". . . . E5 - - - G5 - A5 - G5 - E5 - | F#5 - - - - - D#5 - - - B4 - - - . . | " +
            ". . . . B5 - - - A5 - G5 - F#5 - G5 - | E5 - - - - - . . B4 - C5 - C#5 - D5 - | " +
            "E5 - - - G5 - - - C6 - - - B5 - G5 - | F#5 - - - D#5 - - - A5 - - - F#5 - D#5 -";

        // C harmonic minor (the B natural is the villain's), the boss fight: Cm Cm Ab G | Cm Ab Fm G,
        // ending on a run up into the loop.
        const string ControlTune =
            "C5 - - G4 C5 - Eb5 - D5 - C5 - B4 - G4 - | C5 - - G4 C5 - Eb5 - G5 - - - F5 - Eb5 - | " +
            "Eb5 - - C5 Eb5 - Ab5 - G5 - F5 - Eb5 - C5 - | D5 - - - B4 - - - G4 - B4 - D5 - F5 - | " +
            "G5 - - - Eb5 - G5 - C6 - - - B5 - G5 - | Ab5 - - - G5 - F5 - Eb5 - F5 - G5 - Ab5 - | " +
            "F5 - - - Ab5 - C6 - B5 - Ab5 - G5 - F5 - | G5 - - - - - - - D5 F5 G5 B5 D6 - B5 -";

        const string WinTune = "C5 E5 G5 C6 - . G5 C6 - - - - - - - - - - - -";
        const string WinThird = ". . . . . . . G5 - - - - - - - - - - - -";
        const string WinFifth = ". . . . . . . E5 - - - - - - - - - - - -";
        const string LoseTune = "E5 - D#5 - D5 - C#5 - - - . . C5 - - - - - - - - - - - - -";

        // ---- The pieces ------------------------------------------------------------------

        // Chord roots (MIDI) and whether each is minor.
        struct Chord
        {
            public readonly int Root;
            public readonly bool Minor;
            public Chord(int root, bool minor) { Root = root; Minor = minor; }
            public int Tone(int index)
            {
                int[] steps = Minor ? MinorTones : MajorTones;
                int octave = index / steps.Length;
                return Root + steps[index % steps.Length] + 12 * octave;
            }
        }

        static readonly int[] MajorTones = { 0, 4, 7 };
        static readonly int[] MinorTones = { 0, 3, 7 };

        static float StepSeconds(MusicTrack track) => 60f / Tempo(track) / 4f;

        static float[] Title()
        {
            float step = StepSeconds(MusicTrack.Title);
            var buffer = new Buffer(LoopLength(MusicTrack.Title), loop: true);
            Chord[] chords =
            {
                new Chord(57, true), new Chord(53, false), new Chord(60, false), new Chord(55, false),
                new Chord(57, true), new Chord(53, false), new Chord(55, false), new Chord(52, false),
            };
            Melody(buffer, TitleTune, step, Wave.Pulse, 0.2f, Bars * StepsPerBar);
            for (int bar = 0; bar < Bars; bar++)
            {
                Chord c = chords[bar];
                float start = bar * StepsPerBar * step;
                for (int e = 0; e < 8; e++)
                {
                    float t = start + e * 2 * step;
                    bool off = e % 2 == 1;
                    // Octave-bouncing bass, chord stabs on the off-beats.
                    SawBass(buffer, c.Root - 12 + (off ? 12 : 0), t, step * 1.6f, 0.22f);
                    if (off) Stab(buffer, c, 0, t, 0.05f);
                    Hat(buffer, t, off ? 0.08f : 0.045f, 0.02f);
                }
                for (int b = 0; b < BeatsPerBar; b++)
                {
                    float t = start + b * 4 * step;
                    if (b % 2 == 0) Kick(buffer, t, 0.55f);
                    else Snare(buffer, t, 0.3f);
                }
                Kick(buffer, start + 10 * step, 0.4f);   // the "and" of beat 3
            }
            return buffer.Data;
        }

        static float[] AssemblyFloor()
        {
            float step = StepSeconds(MusicTrack.AssemblyFloor);
            var buffer = new Buffer(LoopLength(MusicTrack.AssemblyFloor), loop: true);
            Chord[] chords =
            {
                new Chord(50, false), new Chord(48, false), new Chord(55, false), new Chord(50, false),
                new Chord(50, false), new Chord(48, false), new Chord(55, false), new Chord(57, false),
            };
            Melody(buffer, AssemblyTune, step, Wave.Pulse, 0.2f, Bars * StepsPerBar);
            for (int bar = 0; bar < Bars; bar++)
            {
                Chord c = chords[bar];
                float start = bar * StepsPerBar * step;
                for (int s = 0; s < StepsPerBar; s++)
                {
                    float t = start + s * step;
                    Arp(buffer, c.Tone(s % 4) + 12, t, 0.05f);
                    Hat(buffer, t, s % 4 == 2 ? 0.08f : 0.035f, 0.02f);
                    if (s % 2 == 0)
                        SawBass(buffer, c.Root - 12 + (s % 4 == 2 ? 12 : 0), t, step * 1.7f, 0.24f);
                }
                for (int b = 0; b < BeatsPerBar; b++)
                {
                    float t = start + b * 4 * step;
                    Kick(buffer, t, 0.55f);   // four on the floor
                    if (b % 2 == 1) Snare(buffer, t, 0.32f);
                }
                if (bar % 4 == 3)
                    Hat(buffer, start + 14 * step, 0.07f, 0.12f);   // open hat into the next phrase
                if (bar == Bars - 1)
                    for (int s = 12; s < 16; s++)
                        Snare(buffer, start + s * step, 0.14f + 0.05f * (s - 12));   // fill back to the top
            }
            return buffer.Data;
        }

        static float[] PaintingRoom()
        {
            float step = StepSeconds(MusicTrack.PaintingRoom);
            var buffer = new Buffer(LoopLength(MusicTrack.PaintingRoom), loop: true);
            Chord[] chords =
            {
                new Chord(53, false), new Chord(55, false), new Chord(52, true), new Chord(57, true),
                new Chord(53, false), new Chord(55, false), new Chord(48, false), new Chord(48, false),
            };
            Melody(buffer, PaintingTune, step, Wave.Pulse, 0.2f, Bars * StepsPerBar);
            // A syncopated funk bass: which 16ths sound, and which jump the octave.
            int[] bassSteps = { 0, 3, 6, 8, 10, 11, 14 };
            for (int bar = 0; bar < Bars; bar++)
            {
                Chord c = chords[bar];
                float start = bar * StepsPerBar * step;
                foreach (int s in bassSteps)
                    SawBass(buffer, c.Root - 12 + (s == 6 || s == 14 ? 12 : 0), start + s * step, step * 0.8f, 0.26f);
                for (int s = 0; s < StepsPerBar; s++)
                {
                    float t = start + s * step;
                    Hat(buffer, t, s % 2 == 0 ? 0.05f : 0.03f, 0.015f);
                    if (s % 4 == 2)
                        Arp(buffer, c.Tone(s / 4 + 3) + 12, t, 0.045f);   // high sparkle, the drips of paint
                }
                for (int b = 0; b < BeatsPerBar; b++)
                    Hat(buffer, start + (b * 4 + 2) * step, 0.06f, 0.06f);   // open hat on each off-beat
                Kick(buffer, start, 0.55f);
                Kick(buffer, start + 7 * step, 0.4f);
                Kick(buffer, start + 10 * step, 0.5f);
                Snare(buffer, start + 4 * step, 0.3f);
                Snare(buffer, start + 12 * step, 0.3f);
                Stab(buffer, c, 0, start + 6 * step, 0.045f);
                Stab(buffer, c, 0, start + 14 * step, 0.04f);
            }
            return buffer.Data;
        }

        static float[] StorageArea()
        {
            float step = StepSeconds(MusicTrack.StorageArea);
            var buffer = new Buffer(LoopLength(MusicTrack.StorageArea), loop: true);
            Chord[] chords =
            {
                new Chord(52, true), new Chord(52, true), new Chord(48, false), new Chord(47, false),
                new Chord(52, true), new Chord(52, true), new Chord(48, false), new Chord(47, false),
            };
            Melody(buffer, StorageTune, step, Wave.Square, 0.17f, Bars * StepsPerBar);
            // The spy line in eighths: root, root, minor third, root, fourth, flat fifth, fifth, third.
            int[] spy = { 0, 0, 3, 0, 5, 6, 7, 3 };
            for (int bar = 0; bar < Bars; bar++)
            {
                Chord c = chords[bar];
                float start = bar * StepsPerBar * step;
                for (int e = 0; e < 8; e++)
                    SawBass(buffer, c.Root - 12 + spy[e], start + e * 2 * step, step * 1.1f, 0.28f);   // staccato
                for (int s = 0; s < StepsPerBar; s++)
                    Hat(buffer, start + s * step, s % 4 == 2 ? 0.035f : 0.018f, 0.012f);   // a quiet shaker
                Kick(buffer, start, 0.5f);
                Kick(buffer, start + 6 * step, 0.35f);
                Kick(buffer, start + 8 * step, 0.45f);
                Rim(buffer, start + 4 * step, 0.12f);
                Snare(buffer, start + 12 * step, 0.22f);
                if (bar % 2 == 1)
                    Rim(buffer, start + 15 * step, 0.08f);
            }
            return buffer.Data;
        }

        static float[] ControlRoom()
        {
            float step = StepSeconds(MusicTrack.ControlRoom);
            var buffer = new Buffer(LoopLength(MusicTrack.ControlRoom), loop: true);
            Chord[] chords =
            {
                new Chord(48, true), new Chord(48, true), new Chord(44, false), new Chord(43, false),
                new Chord(48, true), new Chord(44, false), new Chord(41, true), new Chord(43, false),
            };
            Melody(buffer, ControlTune, step, Wave.Saw, 0.19f, Bars * StepsPerBar);
            Melody(buffer, ControlTune, step, Wave.Square, 0.08f, Bars * StepsPerBar, -12);   // doubled an octave down
            // Factory OS's computer, ticking under the fight: root, octave, fifth, octave, third, octave, fifth, high third.
            int[] sequence = { 0, 3, 2, 3, 1, 3, 2, 4 };
            int[] kicks = { 0, 2, 6, 8, 10, 14 };   // a double-kick gallop
            for (int bar = 0; bar < Bars; bar++)
            {
                Chord c = chords[bar];
                float start = bar * StepsPerBar * step;
                for (int s = 0; s < StepsPerBar; s++)
                {
                    float t = start + s * step;
                    // A galloping 16th-note bass with the octave on every third 16th.
                    SawBass(buffer, c.Root - 12 + (s % 4 == 2 ? 12 : 0), t, step * 0.85f, 0.26f);
                    Bleep(buffer, c.Tone(sequence[s % sequence.Length]) + 24, t, 0.03f);
                    Hat(buffer, t, s % 2 == 1 ? 0.06f : 0.03f, 0.015f);
                }
                foreach (int s in kicks)
                    Kick(buffer, start + s * step, s % 8 == 0 ? 0.6f : 0.42f);
                Snare(buffer, start + 4 * step, 0.36f);
                Snare(buffer, start + 12 * step, 0.36f);
                // Power hits on the one, the "and" of two and beat three.
                Stab(buffer, c, 12, start, 0.06f);
                Stab(buffer, c, 12, start + 6 * step, 0.05f);
                Stab(buffer, c, 12, start + 8 * step, 0.05f);
                if (bar % 4 == 0)
                    Crash(buffer, start, 0.1f);
                if (bar == 3)
                    Siren(buffer, start, StepsPerBar * step, 0.04f);   // the red alarm beacons
                if (bar == Bars - 1)
                    for (int s = 8; s < 16; s++)
                        Snare(buffer, start + s * step, 0.12f + 0.035f * (s - 8));   // a roll back to the top
            }
            return buffer.Data;
        }

        static float[] Win()
        {
            float step = 0.09f;
            var buffer = new Buffer(Mathf.RoundToInt(3.2f * SampleRate), loop: false);
            Melody(buffer, WinTune, step, Wave.Pulse, 0.24f, 0);
            Melody(buffer, WinThird, step, Wave.Pulse, 0.12f, 0);
            Melody(buffer, WinFifth, step, Wave.Pulse, 0.1f, 0);
            for (int s = 0; s < 4; s++)
                Snare(buffer, s * step, 0.12f + 0.06f * s);
            float hit = 7 * step;
            Kick(buffer, hit, 0.6f);
            Crash(buffer, hit, 0.18f);
            Stab(buffer, new Chord(60, false), 0, hit, 0.08f);
            SawBass(buffer, 36, hit, 1.1f, 0.24f);
            return buffer.Data;
        }

        static float[] Lose()
        {
            float step = 0.13f;
            var buffer = new Buffer(Mathf.RoundToInt(4.4f * SampleRate), loop: false);
            Melody(buffer, LoseTune, step, Wave.Pulse, 0.24f, 0);
            float low = 12 * step;
            Kick(buffer, low, 0.5f);
            Stab(buffer, new Chord(57, true), 0, low, 0.07f);
            SawBass(buffer, 33, low, 1.5f, 0.24f);
            return buffer.Data;
        }

        // ---- Scores ------------------------------------------------------------------------

        struct Note
        {
            public int Step, Midi, Steps;
        }

        // Reads a score into notes; `steps` is its length in 16ths.
        static List<Note> Parse(string score, out int steps)
        {
            var notes = new List<Note>();
            bool sounding = false;
            steps = 0;
            foreach (string token in score.Split(new[] { ' ', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token == "-")
                {
                    if (sounding)
                    {
                        Note last = notes[notes.Count - 1];
                        last.Steps++;
                        notes[notes.Count - 1] = last;
                    }
                }
                else if (token == ".")
                    sounding = false;
                else
                {
                    notes.Add(new Note { Step = steps, Midi = Midi(token), Steps = 1 });
                    sounding = true;
                }
                steps++;
            }
            return notes;
        }

        // "C4" is 60; a '#' raises and a 'b' lowers by a semitone.
        static int Midi(string name)
        {
            int semitone = "C D EF G A B".IndexOf(name[0]);
            if (semitone < 0)
                throw new FormatException("Not a note: " + name);
            int i = 1;
            if (name[i] == '#') { semitone++; i++; }
            else if (name[i] == 'b') { semitone--; i++; }
            return 12 * (int.Parse(name.Substring(i)) + 1) + semitone;
        }

        // Plays a score on the lead, moved by `transpose` semitones. A loop's score must fill
        // the loop exactly.
        static void Melody(Buffer b, string score, float step, Wave wave, float gain, int expectedSteps, int transpose = 0)
        {
            List<Note> notes = Parse(score, out int steps);
            if (expectedSteps > 0 && steps != expectedSteps)
                throw new InvalidOperationException($"A score is {steps} 16ths long, not {expectedSteps}.");
            foreach (Note n in notes)
                Lead(b, n.Midi + transpose, n.Step * step, n.Steps * step, gain, wave);
        }

        // ---- Instruments -------------------------------------------------------------------

        enum Wave { Pulse, Square, Saw }

        // A buffer that, when looping, wraps every write round to the start.
        sealed class Buffer
        {
            public readonly float[] Data;
            readonly bool _loop;
            public Buffer(int length, bool loop) { Data = new float[length]; _loop = loop; }
            public void Add(int index, float value)
            {
                if (_loop)
                    Data[((index % Data.Length) + Data.Length) % Data.Length] += value;
                else if (index >= 0 && index < Data.Length)
                    Data[index] += value;
            }
        }

        static float Frequency(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        static int At(float seconds) => Mathf.RoundToInt(seconds * SampleRate);

        // A pulse with its offset removed, so it averages zero like the saw.
        static float PulseWave(float phase, float duty) => (phase < duty ? 1f : -1f) + (1f - 2f * duty);

        static float TriangleWave(float phase) => phase < 0.5f ? 4f * phase - 1f : 3f - 4f * phase;

        static float Shape(Wave wave, float phase)
        {
            switch (wave)
            {
                case Wave.Saw: return 2f * phase - 1f;
                case Wave.Square: return 0.7f * PulseWave(phase, 0.5f);
                default: return 0.8f * PulseWave(phase, 0.25f);
            }
        }

        // The lead: lightly low-passed, with a little vibrato on long notes and a short release
        // so repeated notes stay separate.
        static void Lead(Buffer b, int midi, float start, float length, float gain, Wave wave)
        {
            float f = Frequency(midi);
            int from = At(start), hold = At(length * 0.9f), count = hold + At(0.04f);
            float phase = 0f, smooth = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float vibrato = t > 0.2f ? 1f + 0.005f * Mathf.Sin(2f * Mathf.PI * 5.5f * t) : 1f;
                phase += f * vibrato / SampleRate;
                phase -= Mathf.Floor(phase);
                smooth += (Shape(wave, phase) - smooth) * 0.35f;
                float env = Mathf.Min(1f, t / 0.005f) * (0.7f + 0.3f * Mathf.Exp(-t / 0.08f));
                if (i > hold)
                    env *= 1f - (i - hold) / (float)(count - hold);
                b.Add(from + i, smooth * env * gain);
            }
        }

        // A thin 12.5% pulse blip: the fast chiptune arpeggio.
        static void Arp(Buffer b, int midi, float start, float gain)
        {
            float f = Frequency(midi);
            int from = At(start), count = At(0.1f);
            float smooth = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float phase = f * t - Mathf.Floor(f * t);
                smooth += (PulseWave(phase, 0.125f) - smooth) * 0.4f;
                float env = Mathf.Min(1f, t / 0.002f) * Mathf.Exp(-t / 0.035f);
                b.Add(from + i, smooth * env * gain);
            }
        }

        // A clean sine blip with a faint square edge: a computer's sequence, colder than the arp.
        static void Bleep(Buffer b, int midi, float start, float gain)
        {
            float f = Frequency(midi);
            int from = At(start), count = At(0.09f);
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float phase = f * t - Mathf.Floor(f * t);
                float s = Mathf.Sin(2f * Mathf.PI * phase) + 0.15f * PulseWave(phase, 0.5f);
                float env = Mathf.Min(1f, t / 0.002f) * Mathf.Exp(-t / 0.03f);
                b.Add(from + i, s * env * gain);
            }
        }

        // A low-passed saw with a sine underneath for weight.
        static void SawBass(Buffer b, int midi, float start, float length, float gain)
        {
            float f = Frequency(midi);
            int from = At(start), count = At(length);
            int release = Mathf.Max(1, Mathf.Min(count, At(0.015f)));
            float phase = 0f, smooth = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                phase += f / SampleRate;
                phase -= Mathf.Floor(phase);
                smooth += (2f * phase - 1f - smooth) * 0.12f;
                float sub = Mathf.Sin(2f * Mathf.PI * phase);
                float env = Mathf.Min(1f, t / 0.003f) * (0.75f + 0.25f * Mathf.Exp(-t / 0.06f))
                            * Mathf.Min(1f, (count - i) / (float)release);
                b.Add(from + i, (0.8f * smooth + 0.6f * sub) * env * gain);
            }
        }

        // A short chord of detuned saws: the "brass" hit.
        static void Stab(Buffer b, Chord chord, int shift, float start, float gain)
        {
            int from = At(start), count = At(0.28f);
            for (int k = 0; k < 3; k++)
            {
                float f = Frequency(chord.Tone(k) + shift);
                for (int d = 0; d < 2; d++)
                {
                    float fd = f * (d == 0 ? 1f : 1.005f);
                    float smooth = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        float t = (float)i / SampleRate;
                        float phase = fd * t - Mathf.Floor(fd * t);
                        smooth += (2f * phase - 1f - smooth) * 0.25f;
                        float env = Mathf.Min(1f, t / 0.003f) * Mathf.Exp(-t / 0.09f);
                        b.Add(from + i, smooth * env * gain);
                    }
                }
            }
        }

        // A two-tone alarm that rises and falls over the bar, swelling in and out.
        static void Siren(Buffer b, float start, float length, float gain)
        {
            int from = At(start), count = At(length);
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float x = i / (float)count;
                float f = 620f + 260f * Mathf.Sin(Mathf.PI * x);
                phase += f / SampleRate;
                phase -= Mathf.Floor(phase);
                float s = Mathf.Sin(2f * Mathf.PI * phase) + 0.2f * TriangleWave(phase);
                b.Add(from + i, s * Mathf.Sin(Mathf.PI * x) * gain);
            }
        }

        // A sine dropping from 160 Hz to 48 Hz, driven slightly into saturation for punch.
        static void Kick(Buffer b, float start, float gain)
        {
            int from = At(start), count = At(0.3f);
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float f = 48f + 112f * Mathf.Exp(-t / 0.025f);
                phase += f / SampleRate;
                float s = (float)Math.Tanh(1.6f * Mathf.Sin(2f * Mathf.PI * phase));
                b.Add(from + i, s * Mathf.Exp(-t / 0.11f) * gain);
            }
        }

        // Brightened noise with a short body tone.
        static void Snare(Buffer b, float start, float gain)
        {
            int from = At(start), count = At(0.22f);
            uint seed = (uint)(from * 2654435761u + 1u);
            float previous = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float noise = Noise(ref seed);
                float s = (noise - 0.5f * previous) * Mathf.Exp(-t / 0.07f)
                          + 0.4f * Mathf.Sin(2f * Mathf.PI * 200f * t) * Mathf.Exp(-t / 0.04f);
                previous = noise;
                b.Add(from + i, s * gain);
            }
        }

        // A dry wooden click: a high tone and a burst of noise, both gone in a few milliseconds.
        static void Rim(Buffer b, float start, float gain)
        {
            int from = At(start), count = At(0.05f);
            uint seed = (uint)(from * 2654435761u + 5u);
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float s = Mathf.Sin(2f * Mathf.PI * 1700f * t) * Mathf.Exp(-t / 0.01f)
                          + 0.5f * Noise(ref seed) * Mathf.Exp(-t / 0.004f);
                b.Add(from + i, s * gain);
            }
        }

        // High-passed noise; a short decay is a closed hat, a long one an open hat.
        static void Hat(Buffer b, float start, float gain, float decay)
        {
            int from = At(start), count = At(decay * 4f);
            uint seed = (uint)(from * 2246822519u + 7u);
            float previous = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float noise = Noise(ref seed);
                b.Add(from + i, (noise - previous) * Mathf.Exp(-t / decay) * gain);
                previous = noise;
            }
        }

        // A long high-passed noise wash for the fanfare's hit.
        static void Crash(Buffer b, float start, float gain)
        {
            int from = At(start), count = At(2.3f);
            uint seed = (uint)(from * 3266489917u + 3u);
            float previous = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float noise = Noise(ref seed);
                b.Add(from + i, (noise - previous) * Mathf.Exp(-t / 0.4f) * gain);
                previous = noise;
            }
        }

        // xorshift32: deterministic noise from -1 to 1.
        static float Noise(ref uint state)
        {
            if (state == 0u) state = 1u;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state / (float)uint.MaxValue * 2f - 1f;
        }

        // Soft-clip, then scale to the target loudness, as far as the 0.8 peak allows.
        static float[] Finish(float[] data)
        {
            float peak = 0f;
            double sum = 0.0;
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (float)Math.Tanh(data[i]);
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
                sum += data[i] * data[i];
            }
            float rms = (float)Math.Sqrt(sum / Math.Max(1, data.Length));
            if (peak > 1e-6f && rms > 1e-6f)
            {
                float scale = Mathf.Min(0.8f / peak, TargetRms / rms);
                for (int i = 0; i < data.Length; i++)
                    data[i] *= scale;
            }
            return data;
        }
    }
}
