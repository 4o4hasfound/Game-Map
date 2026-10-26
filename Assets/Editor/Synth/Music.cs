using System;
using System.Collections.Generic;

namespace HotelSynth
{
    // Composed and arranged procedurally. Every piece renders to an exact whole number of bars and note tails
    // wrap around to the start, so all pieces loop without a seam.
    public sealed class Song
    {
        public readonly float bpm;
        public readonly int beatsPerBar, bars;
        public float swing;
        public readonly float[] buf;
        public readonly Rng r;

        public Song(float bpm, int beatsPerBar, int bars, int seed)
        {
            this.bpm = bpm;
            this.beatsPerBar = beatsPerBar;
            this.bars = bars;
            r = new Rng(seed);
            buf = new float[Dsp.N(TotalBeats * 60.0 / bpm)];
        }

        public double TotalBeats => bars * beatsPerBar;
        public double Sec(double beats) => beats * 60.0 / bpm;

        double Swung(double beat)
        {
            double fl = Math.Floor(beat), fr = beat - fl;
            if (swing > 0 && Math.Abs(fr - 0.5) < 0.01) return fl + 0.5 + swing * 0.17;
            return beat;
        }

        public void Add(float[] note, double beat, float gain = 1f, float jitterMs = 6f)
        {
            if (note == null) return;
            double t = Sec(Swung(beat)) + r.Sym() * jitterMs * 0.001;
            Dsp.MixWrap(buf, note, (int)(t * Dsp.SR), gain);
        }
    }

    public static class Music
    {
        // chord = root (midi) + intervals
        public struct Chord
        {
            public int root;
            public int[] iv;

            public Chord(int root, params int[] iv)
            {
                this.root = root;
                this.iv = iv;
            }

            public bool Has(int midi)
            {
                int pc = ((midi - root) % 12 + 12) % 12;
                foreach (int i in iv) if (i % 12 == pc) return true;
                return false;
            }

            // chord tones inside [lo, hi]
            public List<int> Tones(int lo, int hi, bool ext = false)
            {
                var l = new List<int>();
                for (int m = lo; m <= hi; m++)
                {
                    int pc = ((m - root) % 12 + 12) % 12;
                    foreach (int i in iv)
                        if (i % 12 == pc) { l.Add(m); break; }
                    if (ext && pc == 2) l.Add(m);
                }
                return l;
            }
        }

        static readonly int[] Maj7 = { 0, 4, 7, 11 }, Min7 = { 0, 3, 7, 10 }, Dom7 = { 0, 4, 7, 10 }, Min6 = { 0, 3, 7, 9 },
            Sus7 = { 0, 5, 7, 10 }, Maj = { 0, 4, 7 }, Min = { 0, 3, 7 }, M7b5 = { 0, 3, 6, 10 }, Dom9 = { 0, 4, 7, 10, 14 };

        static Chord C(int root, int[] iv) => new Chord(root, iv);

        // close voicing of the chord around a centre pitch (for comping)
        static List<int> Voicing(Chord c, int centre, int notes = 4, bool rootless = true)
        {
            var tones = c.Tones(centre - 7, centre + 8, true);
            var l = new List<int>();
            foreach (int t in tones)
            {
                if (rootless && ((t - c.root) % 12 + 12) % 12 == 0 && tones.Count > notes) continue;
                l.Add(t);
                if (l.Count >= notes) break;
            }
            return l;
        }

        static int Nearest(List<int> options, int target)
        {
            int best = options[0];
            foreach (int o in options) if (Math.Abs(o - target) < Math.Abs(best - target)) best = o;
            return best;
        }

        static int StepTowards(Rng r, List<int> pool, int prev, int lo, int hi)
        {
            var near = pool.FindAll(m => Math.Abs(m - prev) <= 4 && m >= lo && m <= hi);
            if (near.Count == 0) near = pool.FindAll(m => m >= lo && m <= hi);
            if (near.Count == 0) return prev;
            return r.Pick(near);
        }

        static List<int> Scale(int tonic, int[] steps, int lo, int hi)
        {
            var l = new List<int>();
            for (int m = lo; m <= hi; m++)
            {
                int pc = ((m - tonic) % 12 + 12) % 12;
                if (Array.IndexOf(steps, pc) >= 0) l.Add(m);
            }
            return l;
        }

        static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 }, NatMinor = { 0, 2, 3, 5, 7, 8, 10 }, Blues = { 0, 3, 5, 6, 7, 10 };

        // ================================================================ lobby lounge piano (jazz ballad in Db)
        public static float[] LoungePiano(int seed, float detune = 1.4f, float bright = 0.55f, float stretch = 1f)
        {
            var s = new Song(68, 4, 16, seed);
            var prog = new[]
            {
                C(37, Maj7), C(34, Min7), C(39, Min7), C(32, Dom7), C(41, Min7), C(34, Dom7), C(39, Min7), C(32, Dom7),
                C(42, Maj7), C(42, Min6), C(41, Min7), C(34, Min7), C(39, Min7), C(32, Sus7), C(37, Maj7), C(32, Dom7)
            };
            var scale = Scale(1, Major, 63, 87);
            int mel = 72;
            for (int b = 0; b < prog.Length; b++)
            {
                var c = prog[b];
                double t = b * 4;
                var r = s.r;
                float vel = 0.55f + 0.1f * r.Next();
                // left hand: root and tenth-ish
                s.Add(Inst.Piano(c.root, 1.9, vel * 0.9f, r, detune, bright, stretch), t, 0.9f);
                s.Add(Inst.Piano(c.root + 7 + (r.Chance(0.5f) ? 12 : 0), 1.2, vel * 0.7f, r, detune, bright, stretch), t + 2, 0.7f);
                // rootless voicing, anticipated or on the "and" of one
                var v = Voicing(c, 62, 3);
                double ct = r.Chance(0.5f) ? t + 0.5 : t + 1;
                foreach (int m in v) s.Add(Inst.Piano(m, 1.4, vel * 0.6f, r, detune, bright, stretch), ct, 0.55f, 12);
                if (r.Chance(0.5f)) foreach (int m in v) s.Add(Inst.Piano(m, 0.8, vel * 0.45f, r, detune, bright, stretch), t + 2.5, 0.45f, 12);
                // melody: phrase of chord / scale tones, breathing every fourth bar
                if (b % 4 == 3 && r.Chance(0.6f))
                {
                    mel = StepTowards(r, c.Tones(70, 86, true), mel, 70, 86);
                    s.Add(Inst.Piano(mel, 1.5, 0.7f, r, detune, bright, stretch), t, 0.8f);
                    continue;
                }
                var rhythms = new[]
                {
                    new[] { 0.0, 1.0, 1.5, 2.0, 3.0 }, new[] { 0.0, 1.5, 2.0, 3.5 }, new[] { 0.5, 1.0, 2.0 }, new[] { 0.0, 0.5, 1.0, 1.5, 2.5, 3.0 }
                };
                var rh = rhythms[r.Int(0, rhythms.Length)];
                for (int k = 0; k < rh.Length; k++)
                {
                    double nt = rh[k];
                    double len = (k + 1 < rh.Length ? rh[k + 1] : 4.0) - nt;
                    bool strong = Math.Abs(nt - Math.Round(nt)) < 0.01;
                    var pool = strong ? c.Tones(68, 88, true) : scale;
                    mel = StepTowards(r, pool, mel, 68, 88);
                    s.Add(Inst.Piano(mel, len * 0.95 * 60 / s.bpm, 0.62f + 0.25f * r.Next(), r, detune, bright, stretch), t + nt, 0.85f);
                }
            }
            var y = Dsp.RoomWrap(s.buf, 1.8f, 0.22f, 0.5f);
            Dsp.NormalizeRms(y, 0.13f);
            return y;
        }

        // ================================================================ jukebox 1: doo-wop ballad (12/8 feel)
        public static float[] DooWop(int seed)
        {
            var s = new Song(64, 4, 16, seed);
            var r = s.r;
            var prog = new[] { C(48, Maj), C(45, Min), C(41, Maj), C(43, Dom7) };
            int mel = 67;
            for (int b = 0; b < s.bars; b++)
            {
                var c = prog[b % 4];
                double t = b * 4;
                for (int k = 0; k < 12; k++)
                {
                    double bt = t + k / 3.0;
                    var v = Voicing(c, 64, 3, false);
                    foreach (int m in v) s.Add(Inst.Piano(m, 0.25, k % 3 == 0 ? 0.55f : 0.35f, r, 2f, 0.4f), bt, 0.32f, 4);
                    s.Add(Inst.HatClosed(r, 0.5f), bt, 0.35f, 3);
                }
                s.Add(Inst.UprightBass(c.root - 12 + 12, 1.2, 0.8f, r), t, 1f);
                s.Add(Inst.UprightBass(c.root + 7 - 12 + 12, 0.9, 0.7f, r), t + 2, 0.9f);
                s.Add(Inst.UprightBass(c.root + 9 - 12 + 12, 0.3, 0.5f, r), t + 3 + 2 / 3.0, 0.7f);
                s.Add(Inst.Kick(r, 0.7f), t, 0.6f);
                s.Add(Inst.Kick(r, 0.6f), t + 2, 0.5f);
                s.Add(Inst.Snare(r, 0.7f, true), t + 1, 0.8f);
                s.Add(Inst.Snare(r, 0.8f, true), t + 3, 0.9f);
                // backing "ooh" choir
                foreach (int m in Voicing(c, 60, 3, false)) s.Add(Inst.Vox(m, 3.6 * 60 / s.bpm, 0.5f, r, 4, 0.05f, 0.01f, 0.25f), t, 0.35f, 20);
                // lead "aah" line: long notes
                for (int k = 0; k < 2; k++)
                {
                    mel = StepTowards(r, c.Tones(62, 76), mel, 62, 76);
                    s.Add(Inst.Vox(mel, 1.8 * 60 / s.bpm, 0.8f, r, k == 0 ? 0 : 3, 0.06f, 0.018f, 0.08f), t + k * 2, 0.55f, 15);
                }
            }
            var y = s.buf;
            y = Dsp.RoomWrap(y, 1.1f, 0.25f, 0.4f);
            Dsp.Speaker(y, 70, 7500, 1.4f, 3, 900);
            Dsp.NormalizeRms(y, 0.14f);
            return y;
        }

        // ================================================================ jukebox 2 / radio: big band swing blues in Bb
        public static float[] Swing(int seed, bool brassy = true)
        {
            var s = new Song(166, 4, 24, seed) { swing = 1f };
            var r = s.r;
            var blues = new[] { C(46, Dom7), C(51, Dom7), C(46, Dom7), C(46, Dom7), C(51, Dom7), C(51, Dom7), C(46, Dom7), C(43, Dom7),
                                C(48, Min7), C(41, Dom7), C(46, Dom7), C(41, Dom7) };
            int bass = 34, lead = 70;
            var bl = Scale(10, Blues, 62, 82);
            for (int b = 0; b < s.bars; b++)
            {
                var c = blues[b % 12];
                var next = blues[(b + 1) % 12];
                double t = b * 4;
                // walking bass
                for (int q = 0; q < 4; q++)
                {
                    int target;
                    if (q == 0) target = Nearest(c.Tones(28, 48), bass);
                    else if (q == 3) target = Nearest(next.Tones(28, 48), bass) + (r.Chance(0.5f) ? 1 : -1);
                    else target = StepTowards(r, c.Tones(28, 48), bass, 28, 48);
                    bass = target;
                    s.Add(Inst.UprightBass(bass, 0.32, 0.85f, r), t + q, 1f, 4);
                }
                // ride + hi-hat
                foreach (double bt in new[] { 0.0, 1.0, 1.5, 2.0, 3.0, 3.5 }) s.Add(Inst.Ride(r, bt % 1 == 0 ? 0.8f : 0.5f), t + bt, 0.5f, 3);
                s.Add(Inst.HatClosed(r, 0.7f), t + 1, 0.6f);
                s.Add(Inst.HatClosed(r, 0.7f), t + 3, 0.6f);
                if (b % 4 == 3) s.Add(Inst.Snare(r, 0.6f), t + 3.5, 0.5f);
                // piano comping: Charleston
                var v = Voicing(c, 62, 3);
                foreach (int m in v)
                {
                    s.Add(Inst.Piano(m, 0.2, 0.5f, r, 1.5f, 0.6f), t, 0.35f);
                    s.Add(Inst.Piano(m, 0.2, 0.45f, r, 1.5f, 0.6f), t + 1.5, 0.3f);
                }
                // brass stabs
                if (brassy && (b % 2 == 1))
                    foreach (int m in Voicing(c, 67, 4)) s.Add(Inst.Brass(m, 0.2, 0.8f, r), t + 3.5, 0.4f);
                if (brassy && b % 12 == 11)
                    foreach (int m in Voicing(c, 67, 4)) s.Add(Inst.Brass(m, 1.2, 0.95f, r), t, 0.5f);
                // sax solo phrases
                if (b % 2 == 0 || r.Chance(0.5f))
                {
                    double pos = r.Chance(0.5f) ? 0.5 : 0;
                    int notes = r.Int(3, 8);
                    for (int k = 0; k < notes && pos < 4; k++)
                    {
                        lead = StepTowards(r, bl, lead, 62, 82);
                        double len = r.Chance(0.7f) ? 0.5 : 1.0;
                        s.Add(Inst.Sax(lead, len * 0.9 * 60 / s.bpm, 0.7f + 0.2f * r.Next(), r), t + pos, 0.55f, 5);
                        pos += len;
                    }
                }
            }
            var y = Dsp.RoomWrap(s.buf, 1.3f, 0.25f, 0.45f);
            Dsp.NormalizeRms(y, 0.14f);
            return y;
        }

        // ================================================================ jukebox 3: slow ballad (strings + piano)
        public static float[] Ballad(int seed)
        {
            var s = new Song(62, 4, 16, seed);
            var r = s.r;
            var prog = new[] { C(51, Maj7), C(48, Min7), C(44, Maj7), C(46, Dom7) };
            int mel = 70;
            for (int b = 0; b < s.bars; b++)
            {
                var c = prog[b % 4];
                double t = b * 4;
                foreach (int m in Voicing(c, 60, 4, false)) s.Add(Inst.Strings(m, 4.1 * 60 / s.bpm, 0.55f, r, 5, 0.6f), t, 0.35f, 30);
                var arp = c.Tones(55, 79);
                for (int k = 0; k < 8; k++)
                    s.Add(Inst.Piano(arp[(k * 2 + b) % arp.Count], 0.6, 0.45f, r, 1.2f, 0.4f), t + k * 0.5, 0.45f, 8);
                s.Add(Inst.UprightBass(c.root - 12, 1.8, 0.7f, r), t, 0.9f);
                s.Add(Inst.UprightBass(c.root - 5, 1.6, 0.6f, r), t + 2, 0.8f);
                if (b % 2 == 0)
                {
                    mel = StepTowards(r, c.Tones(65, 82, true), mel, 65, 82);
                    s.Add(Inst.Strings(mel, 1.9 * 60 / s.bpm, 0.8f, r, 1, 0.12f, 0.012f, 0.6f), t, 0.6f);
                    mel = StepTowards(r, c.Tones(65, 82, true), mel, 65, 82);
                    s.Add(Inst.Strings(mel, 1.9 * 60 / s.bpm, 0.75f, r, 1, 0.12f, 0.012f, 0.6f), t + 2, 0.55f);
                }
            }
            var y = Dsp.RoomWrap(s.buf, 2.0f, 0.3f, 0.45f);
            Dsp.Speaker(y, 70, 7500, 1.2f, 2, 900);
            Dsp.NormalizeRms(y, 0.13f);
            return y;
        }

        // ================================================================ gramophone waltzes
        public static float[] Waltz(int seed, bool minor = false, float bpm = 150)
        {
            var s = new Song(bpm, 3, 32, seed);
            var r = s.r;
            Chord[] prog = minor
                ? new[] { C(50, Min), C(50, Min), C(45, Dom7), C(45, Dom7), C(45, Dom7), C(45, Dom7), C(50, Min), C(50, Min),
                          C(43, Min), C(43, Min), C(50, Min), C(50, Min), C(45, Dom7), C(45, Dom7), C(50, Min), C(50, Min) }
                : new[] { C(53, Maj), C(53, Maj), C(48, Dom7), C(48, Dom7), C(48, Dom7), C(48, Dom7), C(53, Maj), C(53, Maj),
                          C(46, Maj), C(46, Maj), C(53, Maj), C(53, Maj), C(48, Dom7), C(48, Dom7), C(53, Maj), C(53, Maj) };
            int mel = 77;
            for (int b = 0; b < s.bars; b++)
            {
                var c = prog[b % 16];
                double t = b * 3;
                s.Add(Inst.Pizz(c.root - 12, 0.6, 0.9f, r), t, 1f);
                foreach (int m in Voicing(c, 62, 3, false))
                {
                    s.Add(Inst.Strings(m, 0.35 * 60 / s.bpm * 2, 0.5f, r, 3, 0.03f), t + 1, 0.4f, 4);
                    s.Add(Inst.Strings(m, 0.35 * 60 / s.bpm * 2, 0.45f, r, 3, 0.03f), t + 2, 0.35f, 4);
                }
                // melody: 2-bar gestures, long note then turn
                if (b % 2 == 0)
                {
                    mel = StepTowards(r, c.Tones(72, 89), mel, 72, 89);
                    s.Add(Inst.Strings(mel, 2.6 * 60 / s.bpm, 0.8f, r, 2, 0.08f, 0.014f, 0.65f), t, 0.6f);
                    int m2 = StepTowards(r, Scale(minor ? 2 : 5, minor ? NatMinor : Major, 70, 91), mel, 70, 91);
                    s.Add(Inst.Strings(m2, 0.9 * 60 / s.bpm, 0.7f, r, 2, 0.05f, 0.014f, 0.65f), t + 3, 0.5f);
                    mel = m2;
                }
                else
                {
                    int m3 = StepTowards(r, c.Tones(70, 89), mel, 70, 89);
                    s.Add(Inst.Strings(m3, 1.8 * 60 / s.bpm, 0.75f, r, 2, 0.06f, 0.014f, 0.65f), t + 1, 0.5f);
                    mel = m3;
                }
            }
            var y = Dsp.RoomWrap(s.buf, 1.6f, 0.25f, 0.5f);
            Dsp.NormalizeRms(y, 0.14f);
            return y;
        }

        public static float[] LoveSong(int seed)
        {
            var s = new Song(70, 4, 16, seed);
            var r = s.r;
            var prog = new[] { C(51, Maj7), C(55, Min7), C(56, Maj7), C(46, Dom7), C(48, Min7), C(53, Min7), C(56, Maj7), C(46, Sus7) };
            int mel = 75;
            for (int b = 0; b < s.bars; b++)
            {
                var c = prog[b % 8];
                double t = b * 4;
                var tones = c.Tones(48, 72);
                for (int k = 0; k < 4; k++) s.Add(Inst.Piano(tones[(k * 2) % tones.Count], 0.9, 0.45f, r, 1.5f, 0.45f), t + k, 0.5f);
                s.Add(Inst.Piano(c.root - 12, 3.5, 0.55f, r, 1.5f, 0.45f), t, 0.6f);
                for (int k = 0; k < 2; k++)
                {
                    mel = StepTowards(r, c.Tones(67, 87, true), mel, 67, 87);
                    s.Add(Inst.Strings(mel, 1.9 * 60 / s.bpm, 0.8f, r, 1, 0.15f, 0.015f, 0.6f), t + k * 2, 0.6f);
                }
            }
            var y = Dsp.RoomWrap(s.buf, 1.4f, 0.25f, 0.45f);
            Dsp.NormalizeRms(y, 0.13f);
            return y;
        }

        public static float[] Gramophone(float[] x, int seed, float age)
        {
            var r = new Rng(seed);
            var y = Dsp.WowFlutter(x, 0.003f + 0.004f * age, 0.55f, 0.0015f * (1 + age), 7.5f, r);
            Dsp.Speaker(y, 260 + 120 * age, 3800 - 900 * age, 1.6f + age, 5, 1000);
            Dsp.VinylCrackle(y, r, 30 + 60 * age, 0.06f + 0.08f * age, 0.006f + 0.01f * age);
            Dsp.NormalizeRms(y, 0.13f);
            return y;
        }

        // ================================================================ radio programmes
        public static float[] Quartet(int seed)
        {
            var s = new Song(60, 4, 12, seed);
            var r = s.r;
            var prog = new[] { C(43, Min), C(48, Min), C(50, Dom7), C(43, Min), C(51, Maj), C(48, Min), C(50, Maj), C(50, Dom7),
                               C(43, Min), C(51, Maj), C(50, Dom7), C(43, Min) };
            int cello = 43, viola = 55, vln2 = 62, vln1 = 70;
            for (int b = 0; b < s.bars; b++)
            {
                var c = prog[b];
                double t = b * 4;
                for (int h = 0; h < 2; h++)
                {
                    cello = Nearest(c.Tones(36, 52), cello);
                    viola = Nearest(c.Tones(50, 64), viola + (r.Chance(0.5f) ? 2 : -2));
                    vln2 = Nearest(c.Tones(57, 72), vln2 + (r.Chance(0.5f) ? 2 : -2));
                    s.Add(Inst.Strings(cello, 1.95, 0.7f, r, 1, 0.12f, 0.01f, 0.4f), t + h * 2, 0.7f);
                    s.Add(Inst.Strings(viola, 1.95, 0.55f, r, 1, 0.12f, 0.01f, 0.45f), t + h * 2, 0.45f);
                    s.Add(Inst.Strings(vln2, 1.95, 0.55f, r, 1, 0.12f, 0.01f, 0.5f), t + h * 2, 0.4f);
                }
                for (int q = 0; q < 4; q++)
                {
                    vln1 = StepTowards(r, q % 2 == 0 ? c.Tones(67, 86) : Scale(7, NatMinor, 67, 86), vln1, 67, 86);
                    s.Add(Inst.Strings(vln1, 0.95, 0.7f, r, 1, 0.06f, 0.014f, 0.55f), t + q, 0.5f);
                }
            }
            var y = Dsp.RoomWrap(s.buf, 1.8f, 0.3f, 0.45f);
            Dsp.NormalizeRms(y, 0.13f);
            return y;
        }

        public static float[] Crooner(int seed)
        {
            var s = new Song(66, 4, 16, seed);
            var r = s.r;
            var prog = new[] { C(53, Maj7), C(50, Min7), C(55, Min7), C(48, Dom7) };
            int mel = 65;
            for (int b = 0; b < s.bars; b++)
            {
                var c = prog[b % 4];
                double t = b * 4;
                foreach (int m in Voicing(c, 60, 3)) s.Add(Inst.Piano(m, 1.6, 0.45f, r, 1.3f, 0.45f), t, 0.5f);
                foreach (int m in Voicing(c, 60, 3)) s.Add(Inst.Piano(m, 1.4, 0.4f, r, 1.3f, 0.45f), t + 2, 0.45f);
                s.Add(Inst.UprightBass(c.root - 12, 1.8, 0.7f, r), t, 0.9f);
                s.Add(Inst.UprightBass(c.root - 5, 1.6, 0.6f, r), t + 2, 0.8f);
                for (int q = 0; q < 4; q++) s.Add(Inst.Swish(r, 0.35, 0.8f), t + q, 0.7f);
                for (int k = 0; k < 2; k++)
                {
                    mel = StepTowards(r, c.Tones(57, 72, true), mel, 57, 72);
                    s.Add(Inst.Vox(mel, 1.8 * 60 / s.bpm, 0.85f, r, r.Int(0, 6), 0.05f, 0.02f, 0.07f, 0.95f), t + k * 2, 0.6f, 20);
                }
            }
            var y = Dsp.RoomWrap(s.buf, 1.4f, 0.25f, 0.45f);
            Dsp.NormalizeRms(y, 0.13f);
            return y;
        }

        public static float[] AmRadio(float[] x, int seed, float noise = 0.02f, bool am = true)
        {
            var r = new Rng(seed);
            var y = (float[])x.Clone();
            if (am) Dsp.Speaker(y, 180, 4200, 2.2f, 4, 1500);
            else Dsp.Speaker(y, 60, 11000, 1.3f);
            for (int i = 0; i < y.Length; i++) y[i] += r.Sym() * noise * (1 + 0.5f * Dsp.Sin(Dsp.TAU * 0.13 * i / Dsp.SR));
            // slow fading of the carrier
            for (int i = 0; i < y.Length; i++) y[i] *= 0.85f + 0.15f * Dsp.Sin(Dsp.TAU * 0.07 * i / Dsp.SR);
            Dsp.NormalizeRms(y, 0.13f);
            return y;
        }

        public static float[] TalkShow(int seed, double sec, bool female)
        {
            var r = new Rng(seed);
            var a = Speech.Babble(sec, female ? Speech.Female : Speech.Male, r);
            var b = Speech.Babble(sec, female ? Speech.Male : Speech.Female, new Rng(seed + 9));
            // two hosts take turns
            var y = new float[a.Length];
            double turn = 0;
            bool first = true;
            int i = 0;
            while (i < y.Length)
            {
                int len = Dsp.N(r.Range(3f, 7f));
                for (int k = 0; k < len && i + k < y.Length; k++) y[i + k] = first ? a[i + k] : b[i + k];
                i += len;
                first = !first;
                turn += 1;
            }
            Dsp.Fade(y, 0.0, 0.0);
            return y;
        }

        public static float[] News(int seed, double sec)
        {
            var r = new Rng(seed);
            var y = Speech.Babble(sec, Speech.Announcer, r);
            // jingle every ~18 s
            for (double t = 0.5; t < sec - 2; t += 18)
            {
                int o = Dsp.N(t);
                for (int i = 0; i < Dsp.N(1.5) && o + i < y.Length; i++) y[o + i] *= 0.1f;
                foreach (var (dt, m) in new[] { (0.0, 72), (0.2, 76), (0.4, 79), (0.6, 84) })
                    Dsp.Mix(y, Inst.Bell(Dsp.MidiHz(m), 1.0, 0.8f, r, 0.5f), o + Dsp.N(dt), 1.6f);
            }
            return y;
        }

        public static float[] Numbers(int seed, double sec)
        {
            var r = new Rng(seed);
            var y = Dsp.Buf(sec);
            int o = 0;
            while (o < y.Length - Dsp.N(4))
            {
                // attention chime
                foreach (var (dt, m) in new[] { (0.0, 76), (0.35, 72), (0.7, 79) })
                    Dsp.Mix(y, Inst.Bell(Dsp.MidiHz(m), 0.8, 0.6f, r, 0.35f), o + Dsp.N(dt), 1.4f);
                o += Dsp.N(1.6);
                for (int g = 0; g < 5 && o < y.Length - Dsp.N(1); g++)
                {
                    var digit = Speech.Babble(0.55, Speech.Robot, new Rng(r.Int(0, 10)));
                    Dsp.Mix(y, digit, o, 0.9f);
                    o += Dsp.N(0.75);
                }
                o += Dsp.N(r.Range(1.2f, 2.0f));
            }
            for (int i = 0; i < y.Length; i++) y[i] += 0.02f * Dsp.Sin(Dsp.TAU * 1000 * i / Dsp.SR) * (0.5f + 0.5f * Dsp.Sin(Dsp.TAU * 0.3 * i / Dsp.SR));
            return y;
        }

        // ================================================================ elevator muzak (bossa nova)
        public static float[] Muzak(int seed)
        {
            var s = new Song(126, 4, 16, seed);
            var r = s.r;
            var prog = new[] { C(53, Maj7), C(53, Maj7), C(55, Min7), C(48, Dom9), C(57, Min7), C(50, Dom7), C(55, Min7), C(48, Dom7) };
            int mel = 72;
            var scale = Scale(5, Major, 67, 86);
            for (int b = 0; b < s.bars; b++)
            {
                var c = prog[b % 8];
                double t = b * 4;
                foreach (double bt in new[] { 0.0, 1.5, 2.5, 3.5 })
                    foreach (int m in Voicing(c, 63, 4)) s.Add(Inst.EPiano(m, 0.45, 0.55f, r), t + bt, 0.35f, 4);
                s.Add(Inst.UprightBass(c.root - 12, 0.7, 0.7f, r), t, 0.9f);
                s.Add(Inst.UprightBass(c.root - 5, 0.4, 0.6f, r), t + 1.5, 0.8f);
                s.Add(Inst.UprightBass(c.root - 12, 0.7, 0.65f, r), t + 2, 0.85f);
                s.Add(Inst.UprightBass(c.root - 5, 0.4, 0.6f, r), t + 3.5, 0.8f);
                for (int k = 0; k < 8; k++) s.Add(Inst.Shaker(r, k % 2 == 0 ? 0.7f : 0.45f), t + k * 0.5, 0.5f, 3);
                foreach (double bt in (b % 2 == 0 ? new[] { 0.0, 1.5, 3.0 } : new[] { 1.0, 2.5 })) s.Add(Inst.Rim(r, 0.6f), t + bt, 0.35f);
                if (b % 2 == 0)
                {
                    double pos = 0;
                    while (pos < 8)
                    {
                        double len = r.Chance(0.6f) ? 1 : 2;
                        mel = StepTowards(r, pos % 2 == 0 ? c.Tones(67, 86, true) : scale, mel, 67, 86);
                        s.Add(Inst.Flute(mel, len * 0.9 * 60 / s.bpm, 0.7f, r), t + pos, 0.45f);
                        pos += len + (r.Chance(0.2f) ? 1 : 0);
                    }
                }
            }
            var y = Dsp.RoomWrap(s.buf, 0.8f, 0.2f, 0.5f);
            Dsp.Speaker(y, 220, 6500, 1.3f, 3, 1800);
            Dsp.NormalizeRms(y, 0.12f);
            return y;
        }

        // ================================================================ music boxes
        // (midi, beats) ; Brahms' Wiegenlied (public domain) in C
        static readonly (int, double)[] Lullaby =
        {
            (76, .5), (76, .5), (79, 2), (76, .5), (76, .5), (79, 2), (76, .5), (79, .5), (84, 1), (83, 1.5), (81, .5), (81, 1), (79, 2),
            (74, .5), (76, .5), (77, 1), (74, 1), (74, .5), (76, .5), (77, 2), (74, .5), (77, .5), (83, 1), (81, 1.5), (79, .5), (83, 1), (84, 2),
            (72, .5), (72, .5), (84, 2), (81, .5), (77, .5), (79, 2), (76, .5), (72, .5), (77, 1), (79, 1), (81, 1), (79, 2),
            (72, .5), (72, .5), (84, 2), (81, .5), (77, .5), (79, 2), (76, .5), (72, .5), (77, 1), (76, 1), (74, 1), (72, 3)
        };

        static readonly (int, double)[] Carousel =
        {
            (79, 1), (84, 1), (88, 1), (86, 2), (84, 1), (83, 1), (81, 1), (83, 1), (79, 3),
            (77, 1), (81, 1), (84, 1), (83, 2), (81, 1), (79, 1), (77, 1), (76, 1), (79, 3),
            (79, 1), (84, 1), (88, 1), (91, 2), (88, 1), (86, 1), (84, 1), (83, 1), (84, 3)
        };

        static readonly (int, double)[] BoxWaltz =
        {
            (81, 1), (80, 1), (81, 1), (76, 3), (77, 1), (76, 1), (74, 1), (72, 3), (74, 1), (76, 1), (77, 1), (79, 2), (77, 1),
            (76, 1), (74, 1), (71, 1), (69, 3), (81, 1), (83, 1), (84, 1), (88, 3), (86, 1), (84, 1), (83, 1), (81, 3)
        };

        public static float[] MusicBox(string tune, int seed)
        {
            var mel = tune == "carousel" ? Carousel : tune == "waltz_box" ? BoxWaltz : Lullaby;
            double beat = tune == "carousel" ? 0.32 : 0.42;
            double total = 0;
            foreach (var n in mel) total += n.Item2;
            var y = Dsp.Buf(total * beat + 0.01);
            var r = new Rng(seed);
            double t = 0;
            foreach (var (m, d) in mel)
            {
                Dsp.MixWrap(y, Inst.Tine(m + 12, 0.8f + 0.2f * r.Next(), r), Dsp.N(t * beat), 1f);
                // sparse bass tine on long notes
                if (d >= 2) Dsp.MixWrap(y, Inst.Tine(m - 12 + 12, 0.45f, r), Dsp.N(t * beat), 0.6f);
                t += d;
            }
            // the mechanism: comb ticks and cylinder whirr
            for (int i = 0; i < y.Length; i++)
            {
                if (i % Dsp.N(beat / 3) == 0) y[i] += 0.02f * r.Sym();
                y[i] += 0.002f * r.Sym();
            }
            Dsp.Filter(y, Biquad.HighPass(300), Biquad.Peak(2500, 1, 3));
            Dsp.NormalizeRms(y, 0.12f);
            if (tune == "lullaby_reverse") Array.Reverse(y);
            return y;
        }

        // ================================================================ playable instruments: one note per key
        public static float[] InstrumentNote(string instrument, int midi, int seed)
        {
            var r = new Rng(seed);
            switch (instrument)
            {
                case "detuned": return Trim(Inst.Piano(midi, 1.6, 0.75f, r, 9f, 0.55f, 2.5f));
                case "toy": return Trim(Inst.Tine(midi + 12, 0.9f, r));
                case "organ": return Trim(Inst.Organ(midi, 2.2, 0.9f, r, true));
                default: return Trim(Inst.Piano(midi, 2.4, 0.75f, r, 1.4f, 0.6f));
            }
        }

        static float[] Trim(float[] x)
        {
            var y = (float[])x.Clone();
            Dsp.Fade(y, 0.0, 0.05);
            Dsp.Normalize(y, 0.8f);
            return y;
        }
    }
}
