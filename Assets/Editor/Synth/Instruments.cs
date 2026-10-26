using System;

namespace HotelSynth
{
    // Instrument models used by the music and the playable instruments.
    public static class Inst
    {
        const int SR = Dsp.SR;
        const float TAU = Dsp.TAU;

        // ---------------------------------------------------------------- acoustic piano
        // Additive model: inharmonic partials (stiff strings), three slightly detuned strings per note (beating),
        // hammer-position comb, two-stage decay, hammer thump, soundboard EQ and damper release.
        static readonly System.Collections.Generic.Dictionary<string, float[]> pianoCache =
            new System.Collections.Generic.Dictionary<string, float[]>();

        public static float[] Piano(float midi, double dur, float vel, Rng r, float detuneCents = 1.2f, float bright = 0.6f,
                                    float stretch = 1f)
        {
            // identical notes (same pitch, length bucket and loudness bucket) are rendered once
            string key = $"{midi:0.00}|{Math.Round(dur * 4) / 4:0.00}|{Math.Round(vel * 5)}|{detuneCents:0.0}|{bright:0.0}|{stretch:0.0}";
            if (pianoCache.TryGetValue(key, out var cached)) return cached;
            float f0 = Dsp.MidiHz(midi);
            dur = Math.Round(dur * 4) / 4;
            vel = (float)Math.Round(vel * 5) / 5f;
            double len = dur + 1.8;
            var y = Dsp.Buf(len);
            float B = 0.00035f * (float)Math.Pow(f0 / 261.6f, 0.6) * stretch;
            float tau0 = 3.2f * (float)Math.Pow(261.6f / f0, 0.55);
            int nPart = Math.Min(24, (int)(9000 / f0));
            int rel = Dsp.N(dur);
            float strike = 1f / 7.3f;
            for (int s = 0; s < 3; s++)
            {
                float cents = (s - 1) * detuneCents + r.Sym() * detuneCents * 0.3f;
                float fs = f0 * (float)Math.Pow(2, cents / 1200.0);
                for (int n = 1; n <= nPart; n++)
                {
                    float fn = fs * n * (float)Math.Sqrt(1 + B * n * n);
                    if (fn > 16000) break;
                    float a = (float)(Math.Abs(Math.Sin(Math.PI * n * strike)) / Math.Pow(n, 1.1 - 0.5 * bright * vel));
                    float t1 = tau0 / (1 + 0.35f * (n - 1)), t2 = t1 * 4.5f;
                    double w = TAU * fn / SR;
                    double ph = r.Next() * TAU;
                    // recursive oscillator + multiplicative envelopes (fast)
                    double c2 = 2 * Math.Cos(w), s1 = Math.Sin(ph - w), s0 = Math.Sin(ph - 2 * w);
                    double e1 = 0.72, e2 = 0.28, d1 = Math.Exp(-1.0 / (t1 * SR)), d2 = Math.Exp(-1.0 / (t2 * SR));
                    double dr = Math.Exp(-(1 + n * 0.08) / (0.11 * SR)), er = 1;
                    for (int i = 0; i < y.Length; i++)
                    {
                        double sn = c2 * s1 - s0;
                        s0 = s1;
                        s1 = sn;
                        e1 *= d1;
                        e2 *= d2;
                        if (i > rel)
                        {
                            er *= dr;
                            if (er < 1e-4) break;
                        }
                        y[i] += (float)(a * (e1 + e2) * er * sn);
                    }
                }
            }
            // hammer knock
            var knock = Dsp.Burst(0.03, r, 0.006, Biquad.BandPass(Math.Min(3000, f0 * 6), 0.7));
            Dsp.Mix(y, knock, 0, 0.25f * vel);
            Dsp.Filter(y, Biquad.LowShelf(180, 3), Biquad.Peak(2500, 0.8, -2), Biquad.LowPass(4000 + 9000 * bright * vel));
            Dsp.Gain(y, vel * 0.18f);
            pianoCache[key] = y;
            return y;
        }

        // ---------------------------------------------------------------- electric piano (FM, Rhodes-like)
        public static float[] EPiano(float midi, double dur, float vel, Rng r)
        {
            float f = Dsp.MidiHz(midi);
            var y = Dsp.Buf(dur + 1.2);
            int rel = Dsp.N(dur);
            double pc = r.Next() * TAU, pm = 0, pt = 0;
            for (int i = 0; i < y.Length; i++)
            {
                float t = (float)i / SR;
                float idx = (1.6f * vel + 0.3f) * Dsp.Exp(-t / 0.6) + 0.25f;
                float tine = 0.5f * vel * Dsp.Exp(-t / 0.08);
                pm += TAU * f / SR;
                pt += TAU * f * 14.1f / SR;
                pc += TAU * f / SR;
                float env = Dsp.Exp(-t / (1.8 * Math.Pow(261.6 / f, 0.4)));
                if (i > rel) env *= Dsp.Exp(-(i - rel) / (0.08 * SR));
                y[i] = env * (float)Math.Sin(pc + idx * Math.Sin(pm) + tine * Math.Sin(pt));
            }
            Dsp.Filter(y, Biquad.LowPass(5000), Biquad.Peak(250, 0.7, 2));
            Dsp.Gain(y, 0.22f * vel);
            return y;
        }

        // ---------------------------------------------------------------- organ (drawbars, key click, tremolo)
        public static float[] Organ(float midi, double dur, float vel, Rng r, bool church = false)
        {
            float f = Dsp.MidiHz(midi);
            var y = Dsp.Buf(dur + 0.25);
            float[] ratio = church ? new[] { 0.5f, 1f, 2f, 3f, 4f, 6f, 8f } : new[] { 1f, 2f, 3f, 4f, 6f, 8f };
            float[] amp = church ? new[] { 0.6f, 1f, 0.55f, 0.25f, 0.3f, 0.12f, 0.1f } : new[] { 1f, 0.7f, 0.5f, 0.35f, 0.2f, 0.15f };
            int rel = Dsp.N(dur);
            var ph = new double[ratio.Length];
            for (int i = 0; i < y.Length; i++)
            {
                float t = (float)i / SR;
                float env = Math.Min(1f, t / 0.012f);
                if (i > rel) env *= Dsp.Exp(-(i - rel) / (0.06 * SR));
                float trem = 1f + 0.07f * Dsp.Sin(TAU * 6.3 * t);
                float s = 0;
                for (int k = 0; k < ratio.Length; k++)
                {
                    float fk = f * ratio[k];
                    if (fk > 12000) continue;
                    ph[k] += TAU * fk / SR * (1 + 0.0015 * Math.Sin(TAU * 6.3 * t));
                    s += amp[k] * (float)Math.Sin(ph[k]);
                }
                y[i] = s * env * trem;
            }
            var click = Dsp.Burst(0.01, r, 0.002, Biquad.HighPass(2000));
            Dsp.Mix(y, click, 0, 0.3f);
            Dsp.Gain(y, 0.08f * vel);
            return y;
        }

        // ---------------------------------------------------------------- bowed strings (ensemble or solo)
        public static float[] Strings(float midi, double dur, float vel, Rng r, int voices = 5, float attack = 0.25f, float vibrato = 0.006f,
                                      float bright = 0.5f)
        {
            float f = Dsp.MidiHz(midi);
            var y = Dsp.Buf(dur + 0.6);
            int rel = Dsp.N(dur);
            for (int v = 0; v < voices; v++)
            {
                float det = (float)Math.Pow(2, (r.Sym() * (voices > 1 ? 9 : 2)) / 1200.0);
                float vr = 5.2f + r.Sym() * 0.6f;
                double ph = r.Next(), vph = r.Next() * TAU;
                for (int i = 0; i < y.Length; i++)
                {
                    float t = (float)i / SR;
                    float vib = 1 + vibrato * Math.Min(1f, t / 0.4f) * (float)Math.Sin(vph);
                    vph += TAU * vr / SR;
                    ph += f * det * vib / SR;
                    ph -= Math.Floor(ph);
                    float saw = (float)(2 * ph - 1);
                    float env = Math.Min(1f, t / attack);
                    if (i > rel) env *= Dsp.Exp(-(i - rel) / (0.18 * SR));
                    y[i] += saw * env / voices;
                }
            }
            Dsp.Filter(y, Biquad.LowPass(1200 + 4000 * bright * vel, 0.6), Biquad.Peak(450, 1, 3), Biquad.Peak(2800, 1.5, 2), Biquad.HighPass(90));
            Dsp.Gain(y, 0.32f * vel);
            return y;
        }

        public static float[] Pizz(float midi, double dur, float vel, Rng r)
        {
            float f = Dsp.MidiHz(midi);
            var y = Dsp.KarplusStrong(f, dur + 0.5, 0.993f, r, 0.25f);
            Dsp.Filter(y, Biquad.LowPass(2500), Biquad.Peak(300, 1, 3));
            Dsp.Gain(y, 0.5f * vel);
            return y;
        }

        // ---------------------------------------------------------------- brass section stab / sustained note
        public static float[] Brass(float midi, double dur, float vel, Rng r, int voices = 3)
        {
            float f = Dsp.MidiHz(midi);
            var y = Dsp.Buf(dur + 0.3);
            int rel = Dsp.N(dur);
            var lp = new Biquad[voices];
            for (int v = 0; v < voices; v++)
            {
                double ph = r.Next();
                float det = (float)Math.Pow(2, r.Sym() * 7 / 1200.0);
                for (int i = 0; i < y.Length; i++)
                {
                    float t = (float)i / SR;
                    float vib = 1 + 0.008f * Math.Min(1f, Math.Max(0f, t - 0.25f) / 0.3f) * Dsp.Sin(TAU * 5.5 * t);
                    ph += f * det * vib / SR;
                    ph -= Math.Floor(ph);
                    float env = Math.Min(1f, t / 0.035f) * (0.85f + 0.15f * Dsp.Exp(-t / 0.1));
                    if (i > rel) env *= Dsp.Exp(-(i - rel) / (0.07 * SR));
                    y[i] += (float)(2 * ph - 1) * env / voices;
                }
            }
            // brightness follows the loudness (that is what makes it sound like brass)
            var b = Biquad.LowPass(900 + 3500 * vel, 1.1);
            var b2 = Biquad.LowPass(900 + 3500 * vel, 0.7);
            for (int i = 0; i < y.Length; i++) y[i] = b2.Process(b.Process(y[i]));
            Dsp.Filter(y, Biquad.Peak(1200, 1, 4));
            Dsp.Gain(y, 0.45f * vel);
            return y;
        }

        // ---------------------------------------------------------------- saxophone-like lead
        public static float[] Sax(float midi, double dur, float vel, Rng r)
        {
            float f = Dsp.MidiHz(midi);
            var y = Dsp.Buf(dur + 0.2);
            int rel = Dsp.N(dur);
            double ph = 0;
            for (int i = 0; i < y.Length; i++)
            {
                float t = (float)i / SR;
                float vib = 1 + 0.012f * Math.Min(1f, Math.Max(0f, t - 0.2f) / 0.3f) * Dsp.Sin(TAU * 5 * t);
                float bend = 1 - 0.03f * Dsp.Exp(-t / 0.04);
                ph += f * vib * bend / SR;
                ph -= Math.Floor(ph);
                float sq = ph < 0.45 ? 1 : -1;
                float saw = (float)(2 * ph - 1);
                float env = Math.Min(1f, t / 0.03f);
                if (i > rel) env *= Dsp.Exp(-(i - rel) / (0.05 * SR));
                y[i] = (0.6f * sq + 0.4f * saw + 0.08f * r.Sym()) * env;
            }
            Dsp.Filter(y, Biquad.LowPass(3200), Biquad.Peak(700, 2, 6), Biquad.Peak(1700, 3, 4), Biquad.HighPass(150));
            Dsp.Gain(y, 0.22f * vel);
            return y;
        }

        public static float[] Flute(float midi, double dur, float vel, Rng r)
        {
            float f = Dsp.MidiHz(midi);
            var y = Dsp.Buf(dur + 0.2);
            int rel = Dsp.N(dur);
            double ph = 0;
            var bp = Biquad.BandPass(f * 2, 4);
            for (int i = 0; i < y.Length; i++)
            {
                float t = (float)i / SR;
                ph += TAU * f * (1 + 0.006 * Math.Sin(TAU * 5 * t)) / SR;
                float env = Math.Min(1f, t / 0.06f);
                if (i > rel) env *= Dsp.Exp(-(i - rel) / (0.08 * SR));
                float breath = bp.Process(r.Sym()) * 0.4f;
                y[i] = ((float)Math.Sin(ph) + 0.15f * (float)Math.Sin(2 * ph) + breath) * env;
            }
            Dsp.Gain(y, 0.2f * vel);
            return y;
        }

        // ---------------------------------------------------------------- bass
        public static float[] UprightBass(float midi, double dur, float vel, Rng r)
        {
            float f = Dsp.MidiHz(midi);
            var y = Dsp.Buf(dur + 0.4);
            int rel = Dsp.N(dur);
            double ph = r.Next() * TAU;
            for (int i = 0; i < y.Length; i++)
            {
                float t = (float)i / SR;
                ph += TAU * f / SR;
                float env = Dsp.Exp(-t / 0.9) * Math.Min(1f, t / 0.004f);
                if (i > rel) env *= Dsp.Exp(-(i - rel) / (0.05 * SR));
                float s = (float)(Math.Sin(ph) + 0.45 * Math.Sin(2 * ph) * Dsp.Exp(-t / 0.15) + 0.2 * Math.Sin(3 * ph) * Dsp.Exp(-t / 0.08));
                y[i] = s * env;
            }
            var thump = Dsp.Burst(0.02, r, 0.004, Biquad.LowPass(900));
            Dsp.Mix(y, thump, 0, 0.3f);
            Dsp.Filter(y, Biquad.LowPass(1800));
            Dsp.Gain(y, 0.35f * vel);
            return y;
        }

        // ---------------------------------------------------------------- drums
        public static float[] Kick(Rng r, float vel = 1f)
        {
            var y = Dsp.Buf(0.45);
            double ph = 0;
            for (int i = 0; i < y.Length; i++)
            {
                float t = (float)i / SR;
                float f = 48 + 90 * Dsp.Exp(-t / 0.03);
                ph += TAU * f / SR;
                y[i] = (float)Math.Sin(ph) * Dsp.Exp(-t / 0.18);
            }
            Dsp.Mix(y, Dsp.Burst(0.01, r, 0.002, Biquad.LowPass(3000)), 0, 0.2f);
            Dsp.Gain(y, 0.6f * vel);
            return y;
        }

        public static float[] Snare(Rng r, float vel = 1f, bool brush = false)
        {
            if (brush)
            {
                var b = Dsp.Noise(0.25, r);
                for (int i = 0; i < b.Length; i++)
                {
                    float t = (float)i / SR;
                    b[i] *= Math.Min(1f, t / 0.015f) * Dsp.Exp(-t / 0.07);
                }
                Dsp.Filter(b, Biquad.BandPass(3500, 0.6), Biquad.HighPass(800));
                Dsp.Gain(b, 0.5f * vel);
                return b;
            }
            var y = Dsp.Burst(0.25, r, 0.06, Biquad.BandPass(2200, 0.7));
            Dsp.Modes(y, 0, new[] { 185f, 330f }, new[] { 0.05f, 0.03f }, new[] { 0.5f, 0.25f });
            Dsp.Gain(y, 0.5f * vel);
            return y;
        }

        public static float[] Swish(Rng r, double len, float vel = 1f)
        {
            var b = Dsp.Noise(len, r);
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)(i / (double)b.Length);
                b[i] *= Dsp.Sin(Math.PI * t);
            }
            Dsp.Filter(b, Biquad.BandPass(5000, 0.5));
            Dsp.Gain(b, 0.12f * vel);
            return b;
        }

        public static float[] HatClosed(Rng r, float vel = 1f)
        {
            var y = Dsp.Burst(0.06, r, 0.012, Biquad.HighPass(7000));
            Dsp.Gain(y, 0.25f * vel);
            return y;
        }

        public static float[] Ride(Rng r, float vel = 1f)
        {
            var y = Dsp.Buf(1.4);
            float[] fr = { 3205, 4120, 5287, 6120, 7333, 8460, 9100 };
            var dec = new float[fr.Length];
            var amp = new float[fr.Length];
            for (int k = 0; k < fr.Length; k++)
            {
                dec[k] = 0.5f + r.Next() * 0.4f;
                amp[k] = 0.15f + r.Next() * 0.1f;
            }
            Dsp.Modes(y, 0, fr, dec, amp, r, 0.01f);
            Dsp.Mix(y, Dsp.Burst(0.05, r, 0.01, Biquad.HighPass(5000)), 0, 0.4f);
            Dsp.Filter(y, Biquad.HighPass(2500));
            Dsp.Gain(y, 0.16f * vel);
            return y;
        }

        public static float[] Shaker(Rng r, float vel = 1f)
        {
            var y = Dsp.Noise(0.08, r);
            for (int i = 0; i < y.Length; i++)
            {
                float t = (float)i / SR;
                y[i] *= Math.Min(1f, t / 0.02f) * Dsp.Exp(-t / 0.025);
            }
            Dsp.Filter(y, Biquad.BandPass(6000, 1.2));
            Dsp.Gain(y, 0.35f * vel);
            return y;
        }

        public static float[] Rim(Rng r, float vel = 1f)
        {
            var y = Dsp.Buf(0.12);
            Dsp.Modes(y, 0, new[] { 1700f, 2400f, 820f }, new[] { 0.012f, 0.008f, 0.02f }, new[] { 0.5f, 0.3f, 0.3f });
            Dsp.Gain(y, 0.6f * vel);
            return y;
        }

        // ---------------------------------------------------------------- sung vowels ("ooh", "aah")
        public static readonly float[][] Vowels =
        {
            new[] { 730f, 1090f, 2440f }, // a
            new[] { 530f, 1840f, 2480f }, // e
            new[] { 270f, 2290f, 3010f }, // i
            new[] { 570f, 840f, 2410f },  // o
            new[] { 300f, 870f, 2240f },  // u
            new[] { 500f, 1500f, 2500f }, // schwa
        };

        public static float[] Vox(float midi, double dur, float vel, Rng r, int vowel = 4, float breath = 0.05f, float vib = 0.012f,
                                  float attack = 0.12f, float formantScale = 1f)
        {
            float f = Dsp.MidiHz(midi);
            var y = Dsp.Buf(dur + 0.4);
            int rel = Dsp.N(dur);
            double ph = r.Next();
            float vr = 5.0f + r.Sym() * 0.5f;
            var src = new float[y.Length];
            for (int i = 0; i < y.Length; i++)
            {
                float t = (float)i / SR;
                float v = 1 + vib * Math.Min(1f, t / 0.35f) * Dsp.Sin(TAU * vr * t) + 0.002f * r.Sym();
                ph += f * v / SR;
                ph -= Math.Floor(ph);
                // glottal pulse (Rosenberg-ish): rising half sine then quick close
                float g = ph < 0.6 ? Dsp.Sin(Math.PI * ph / 0.6) : 0f;
                float env = Math.Min(1f, t / attack);
                if (i > rel) env *= Dsp.Exp(-(i - rel) / (0.12 * SR));
                src[i] = (g - 0.38f + breath * r.Sym()) * env;
            }
            var fm = Vowels[vowel];
            float[] bw = { 80, 110, 160 };
            float[] ga = { 1f, 0.55f, 0.22f };
            for (int k = 0; k < 3; k++)
            {
                var bp = Biquad.BandPass(fm[k] * formantScale, fm[k] * formantScale / bw[k]);
                for (int i = 0; i < y.Length; i++) y[i] += bp.Process(src[i]) * ga[k];
            }
            Dsp.Gain(y, 0.6f * vel);
            return y;
        }

        // ---------------------------------------------------------------- tuned metal: music box tines, bells
        public static float[] Tine(float midi, float vel, Rng r)
        {
            float f = Dsp.MidiHz(midi);
            var y = Dsp.Buf(2.2);
            Dsp.Modes(y, 0, new[] { f, f * 2.76f, f * 5.40f, f * 8.93f }, new[] { 1.1f, 0.25f, 0.08f, 0.03f },
                      new[] { 1f, 0.35f, 0.18f, 0.08f }, r, 0.002f);
            var click = Dsp.Burst(0.006, r, 0.0015, Biquad.HighPass(3000));
            Dsp.Mix(y, click, 0, 0.35f);
            Dsp.Gain(y, 0.4f * vel);
            return y;
        }

        public static float[] Bell(float f, double len, float vel, Rng r, float decay = 1.6f)
        {
            var y = Dsp.Buf(len);
            Dsp.Modes(y, 0, new[] { f * 0.5f, f, f * 1.19f, f * 1.56f, f * 2.0f, f * 2.51f, f * 3.0f },
                      new[] { decay * 1.2f, decay, decay * 0.7f, decay * 0.5f, decay * 0.4f, decay * 0.25f, decay * 0.2f },
                      new[] { 0.35f, 1f, 0.5f, 0.35f, 0.25f, 0.12f, 0.08f }, r, 0.001f);
            Dsp.Gain(y, 0.3f * vel);
            return y;
        }
    }
}
