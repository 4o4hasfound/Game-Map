using System;
using System.Collections.Generic;

namespace HotelSynth
{
    public sealed class Baked
    {
        public string name;
        public float[] left, right;     // right == null -> mono
        public bool loop;
    }

    // Every sound in the game, by name. Loops are rendered seamless; one-shots come in several variants
    // (name_0, name_1, ...) so repeated events never sound identical.
    public static class Sounds
    {
        const int SR = Dsp.SR;
        const float TAU = Dsp.TAU;

        static Baked M(string n, float[] x, bool loop = false) => new Baked { name = n, left = x, loop = loop };

        static IEnumerable<Baked> Var(string n, int count, int seed, Func<Rng, float[]> f, float rms = 0f, float peak = 0.9f)
        {
            for (int k = 0; k < count; k++)
            {
                var x = f(new Rng(seed * 131 + k * 7919));
                if (rms > 0) Dsp.NormalizeRms(x, rms); else Dsp.Normalize(x, peak);
                yield return M($"{n}_{k}", x);
            }
        }

        static float[] Env(float[] x, Func<float, float> e)
        {
            for (int i = 0; i < x.Length; i++) x[i] *= e((float)i / SR);
            return x;
        }

        static float[] LoopOf(double sec, double xf, Func<double, float[]> render, float rms)
        {
            var x = Dsp.Loop(render(sec + xf), xf);
            Dsp.NormalizeRms(x, rms);
            return x;
        }

        // ---------------------------------------------------------------- building blocks
        static void Thud(float[] y, int o, Rng r, float f, float dec, float amp)
        {
            Dsp.Modes(y, o, new[] { f, f * 1.7f, f * 2.6f }, new[] { dec, dec * 0.6f, dec * 0.35f }, new[] { amp, amp * 0.5f, amp * 0.25f }, r, 0.05f);
        }

        static void Click(float[] y, int o, Rng r, float f, float amp, float dec = 0.004f)
        {
            Dsp.Mix(y, Dsp.Burst(dec * 6, r, dec, Biquad.HighPass(f)), o, amp);
        }

        static void Grit(float[] y, int o, Rng r, int count, double span, float amp, float f = 2500)
        {
            var g = Dsp.Buf(span + 0.02);
            for (int k = 0; k < count; k++)
            {
                int p = (int)(r.Next() * Dsp.N(span));
                g[p] += r.Sym() * amp * (0.3f + r.Next());
            }
            Dsp.Filter(g, Biquad.BandPass(f, 0.9));
            Dsp.Mix(y, g, o);
        }

        // stick-slip friction: creaking wood, squeaking hinges, groaning metal
        static float[] StickSlip(Rng r, double sec, float rateLo, float rateHi, float[] res, float[] q, float body = 0f)
        {
            var src = Dsp.Buf(sec);
            double ph = 0;
            float rate = r.Range(rateLo, rateHi), target = rate;
            for (int i = 0; i < src.Length; i++)
            {
                if (i % 2000 == 0) target = r.Range(rateLo, rateHi);
                rate += (target - rate) * 0.0008f;
                ph += rate / SR;
                if (ph >= 1)
                {
                    ph -= 1;
                    src[i] = r.Range(0.5f, 1f) * (r.Chance(0.06f) ? 0 : 1);
                }
            }
            var env = Dsp.Sin(0);
            var y = Dsp.Buf(sec);
            for (int k = 0; k < res.Length; k++)
            {
                var bp = Biquad.BandPass(res[k] * r.Range(0.9f, 1.1f), q[k]);
                for (int i = 0; i < y.Length; i++) y[i] += bp.Process(src[i]) / res.Length;
            }
            if (body > 0)
            {
                var lp = Biquad.LowPass(250);
                for (int i = 0; i < y.Length; i++) y[i] += lp.Process(src[i]) * body;
            }
            Env(y, t => Math.Min(1f, t / 0.05f) * Math.Min(1f, (float)(sec - t) / 0.15f) * (0.6f + 0.4f * Dsp.Sin(TAU * 0.7 * t + 1)));
            return y;
        }

        static float[] Hum(double sec, Rng r, float f0, int harmonics, float tilt, float buzz)
        {
            var y = Dsp.Buf(sec);
            var ph = new double[harmonics + 1];
            for (int h = 1; h <= harmonics; h++) ph[h] = r.Next() * TAU;
            for (int i = 0; i < y.Length; i++)
            {
                double t = (double)i / SR;
                float s = 0;
                for (int h = 1; h <= harmonics; h++)
                {
                    float a = (float)(1 / Math.Pow(h, tilt));
                    s += a * (float)Math.Sin(TAU * f0 * h * t + ph[h]);
                }
                y[i] = s;
            }
            if (buzz > 0)
                for (int i = 0; i < y.Length; i++) y[i] = Dsp.SoftClip(y[i] * (1 + buzz * 3), 1 + buzz * 2);
            return y;
        }

        static void Bubbles(float[] y, Rng r, float perSecond, float fLo, float fHi, float amp, float decLo = 0.004f, float decHi = 0.02f)
        {
            int n = (int)(perSecond * y.Length / SR);
            for (int k = 0; k < n; k++)
            {
                int o = (int)(r.Next() * y.Length);
                float f = (float)Math.Exp(r.Range((float)Math.Log(fLo), (float)Math.Log(fHi)));
                Dsp.Bubble(y, o, f, amp * r.Range(0.2f, 1f), r.Range(decLo, decHi), r.Range(0.3f, 2.5f));
            }
        }

        // ---------------------------------------------------------------- footsteps
        // A step is three overlapping events: heel strike (a short, low body impact), the roll of the foot (a soft
        // scuff of friction noise) and the toe-off. Each impact is filtered noise through a low-Q resonance, not a
        // pure tone; the surface decides the filter shapes and what rattles or rings afterwards.
        static float[] Shaped(Rng r, double len, double attack, double decay, float f, float q, bool lowpass = false)
        {
            var x = Dsp.Noise(len, r);
            for (int i = 0; i < x.Length; i++)
            {
                double t = (double)i / SR;
                x[i] *= (float)(Math.Min(1.0, t / attack) * Math.Exp(-t / decay));
            }
            if (lowpass) Dsp.Filter(x, Biquad.LowPass(f, q), Biquad.LowPass(f, q));
            else Dsp.Filter(x, Biquad.BandPass(f, q));
            return x;
        }

        static float[] Step(string surf, Rng r, bool land)
        {
            var y = Dsp.Buf(land ? 0.7 : 0.5);
            float g = land ? 1.7f : 1f;
            int roll = Dsp.N(r.Range(0.035f, 0.06f));
            int toe = Dsp.N(r.Range(0.09f, 0.14f));
            float heelF = r.Range(0.85f, 1.15f);
            switch (surf)
            {
                case "carpet":
                    Dsp.Mix(y, Shaped(r, 0.12, 0.004, 0.022, 140 * heelF, 0.8f, true), 0, 1.0f * g);
                    Dsp.Mix(y, Shaped(r, 0.12, 0.012, 0.03, r.Range(900, 1500), 0.6f), roll, 0.22f);
                    Dsp.Mix(y, Shaped(r, 0.08, 0.004, 0.018, 220, 0.8f, true), toe, 0.45f);
                    break;
                case "wood":
                    Dsp.Mix(y, Shaped(r, 0.15, 0.002, 0.03, 180 * heelF, 0.9f, true), 0, 0.9f * g);
                    Dsp.Mix(y, Shaped(r, 0.12, 0.001, 0.025, r.Range(380, 520), 2.2f), 0, 0.5f * g);
                    Dsp.Mix(y, Shaped(r, 0.03, 0.0005, 0.004, 2600, 1.2f), 0, 0.18f);
                    Dsp.Mix(y, Shaped(r, 0.1, 0.012, 0.025, r.Range(1200, 2000), 0.7f), roll, 0.12f);
                    Dsp.Mix(y, Shaped(r, 0.1, 0.002, 0.02, r.Range(420, 600), 2f), toe, 0.35f);
                    if (r.Chance(0.2f)) Dsp.Mix(y, StickSlip(r, 0.22, 90, 220, new[] { 380f, 820f }, new[] { 5f, 7f }), Dsp.N(0.06), 0.12f);
                    break;
                case "tile":
                    Dsp.Mix(y, Shaped(r, 0.1, 0.002, 0.02, 160 * heelF, 0.9f, true), 0, 0.6f * g);
                    Dsp.Mix(y, Shaped(r, 0.05, 0.0004, 0.006, r.Range(2600, 3400), 1.4f), 0, 0.55f * g);
                    Dsp.Mix(y, Shaped(r, 0.1, 0.01, 0.02, r.Range(3000, 5000), 0.8f), roll, 0.08f);
                    Dsp.Mix(y, Shaped(r, 0.04, 0.0004, 0.005, r.Range(3000, 4200), 1.4f), toe, 0.35f);
                    break;
                case "concrete":
                    Dsp.Mix(y, Shaped(r, 0.1, 0.002, 0.018, 150 * heelF, 0.9f, true), 0, 0.7f * g);
                    Dsp.Mix(y, Shaped(r, 0.06, 0.0008, 0.008, r.Range(1500, 2300), 1.0f), 0, 0.35f * g);
                    Grit(y, Dsp.N(0.01), r, 25, 0.06, 0.25f, 4200);
                    Dsp.Mix(y, Shaped(r, 0.12, 0.01, 0.03, r.Range(2500, 4000), 0.7f), roll, 0.12f);
                    Dsp.Mix(y, Shaped(r, 0.05, 0.001, 0.008, r.Range(1800, 2600), 1.0f), toe, 0.3f);
                    break;
                case "metal":
                {
                    float f = r.Range(0.9f, 1.15f);
                    Dsp.Mix(y, Shaped(r, 0.12, 0.002, 0.02, 130 * heelF, 0.9f, true), 0, 0.6f * g);
                    for (int k = 0; k < 4; k++)
                        Dsp.Mix(y, Shaped(r, 0.4, 0.0005, r.Range(0.05f, 0.14f), r.Range(400, 1600) * f, r.Range(12, 25)), 0, 0.12f * g);
                    for (int k = 0; k < 3; k++)
                        Dsp.Mix(y, Shaped(r, 0.02, 0.0003, 0.003, 3000, 1.2f), Dsp.N(0.015 + 0.02 * k + r.Next() * 0.01), 0.12f * (1 - k * 0.25f));
                    Dsp.Mix(y, Shaped(r, 0.25, 0.0005, 0.06, r.Range(600, 1100) * f, 18), toe, 0.1f);
                    break;
                }
                case "water":
                {
                    Dsp.Mix(y, Shaped(r, 0.25, 0.006, 0.05, r.Range(1100, 1800), 0.5f), 0, 0.55f * g);
                    Dsp.Mix(y, Shaped(r, 0.2, 0.01, 0.05, 250, 0.7f, true), 0, 0.45f * g);
                    int drops = r.Int(3, 8);
                    for (int k = 0; k < drops; k++)
                        Dsp.Mix(y, Shaped(r, 0.03, 0.0004, 0.006, r.Range(2500, 5000), 1.5f), Dsp.N(0.05 + r.Next() * 0.25), r.Range(0.04f, 0.12f));
                    Dsp.Mix(y, Shaped(r, 0.2, 0.02, 0.05, r.Range(800, 1400), 0.6f), toe, 0.25f);
                    break;
                }
                case "rubble":
                    Dsp.Mix(y, Shaped(r, 0.1, 0.002, 0.02, 150 * heelF, 0.9f, true), 0, 0.6f * g);
                    Grit(y, 0, r, 90, 0.14, 0.6f, 2600);
                    Grit(y, Dsp.N(0.03), r, 40, 0.1, 0.4f, 1400);
                    break;
                case "rubber":
                    Dsp.Mix(y, Shaped(r, 0.1, 0.004, 0.02, 120 * heelF, 0.9f, true), 0, 0.8f * g);
                    if (r.Chance(0.25f)) Dsp.Mix(y, StickSlip(r, 0.1, 500, 800, new[] { 1700f }, new[] { 8f }), roll, 0.1f);
                    break;
                default:
                    Dsp.Mix(y, Shaped(r, 0.12, 0.002, 0.025, 140 * heelF, 0.9f, true), 0, 0.6f * g);
                    Dsp.Mix(y, Shaped(r, 0.2, 0.0005, 0.05, r.Range(500, 800), 10), 0, 0.15f * g);
                    Dsp.Mix(y, Shaped(r, 0.03, 0.0004, 0.004, 3200, 1.3f), 0, 0.15f);
                    break;
            }
            Dsp.Filter(y, Biquad.HighPass(45));
            Dsp.Fade(y, 0.0, 0.05);
            return y;
        }

        // ---------------------------------------------------------------- doors
        static float[] DoorCreak(Rng r, bool metal)
        {
            // Dry hinge friction: irregular short rubs, heavily damped, without a sung pitch or latch on every creak.
            double len = r.Range(0.35f, 0.65f);
            var y = Dsp.Buf(len + 0.08);
            int count = r.Int(3, 7);
            for (int k = 0; k < count; k++)
            {
                double at = (len - 0.12) * k / count + r.Range(0f, 0.025f);
                float f = r.Range(metal ? 1100 : 450, metal ? 2100 : 1000);
                Dsp.Mix(y, Shaped(r, 0.11, 0.004, r.Range(0.012f, 0.027f), f, 1.2f), Dsp.N(at), r.Range(0.2f, 0.65f));
                Dsp.Mix(y, Shaped(r, 0.08, 0.002, 0.014, 190, 0.7f, true), Dsp.N(at), 0.22f);
            }
            Dsp.Filter(y, Biquad.HighPass(90), Biquad.LowPass(metal ? 3400 : 2000));
            Dsp.Fade(y, 0.004, 0.04);
            return y;
        }

        static float[] DoorClose(Rng r, bool metal)
        {
            // A heavy leaf hitting its seals/frame: short broadband contact, broad low body, then a separate latch.
            // All components are dry, low-Q noise resonances. There is no descending oscillator or baked room echo.
            var y = Dsp.Buf(metal ? 0.75 : 0.55);
            float body = r.Range(metal ? 105 : 85, metal ? 145 : 120);
            Dsp.Mix(y, Shaped(r, 0.35, 0.0015, metal ? 0.065 : 0.047, body, 0.75f, true), 0, 3.8f);
            Dsp.Mix(y, Shaped(r, 0.24, 0.0005, 0.027, r.Range(220, 340), 1.0f), 0, 1.05f);
            Dsp.Mix(y, Shaped(r, 0.12, 0.0003, 0.009, r.Range(850, 1300), 0.7f), 0, 0.32f);
            int latch = Dsp.N(r.Range(0.022f, 0.038f));
            Dsp.Mix(y, Shaped(r, 0.055, 0.0002, 0.006, r.Range(2200, 3200), 1.4f), latch, 0.22f);
            if (metal)
            {
                Dsp.Mix(y, Shaped(r, 0.4, 0.001, 0.075, r.Range(480, 690), 2.7f), 0, 0.26f);
                for (int k = 0; k < 3; k++)
                    Dsp.Mix(y, Shaped(r, 0.055, 0.0003, 0.008, r.Range(1700, 2800), 1.8f),
                            Dsp.N(0.045 + k * 0.026 + r.Range(0f, 0.008f)), 0.06f / (k + 1));
            }
            Dsp.Filter(y, Biquad.HighPass(32), Biquad.LowPass(metal ? 5200 : 4200));
            Dsp.Fade(y, 0.0003, 0.045);
            return y;
        }

        static float[] DoorLocked(Rng r)
        {
            var y = Dsp.Buf(0.9);
            int n = r.Int(3, 6);
            for (int k = 0; k < n; k++)
            {
                int o = Dsp.N(0.05 + k * r.Range(0.1f, 0.16f));
                Dsp.Modes(y, o, new[] { 1900f, 2800f, 4300f }, new[] { 0.012f, 0.01f, 0.006f }, new[] { 0.4f, 0.3f, 0.2f }, r, 0.05f);
                Thud(y, o, r, 120, 0.03f, 0.35f);
            }
            return y;
        }

        // ---------------------------------------------------------------- lights and power
        static float[] Buzz(double sec, Rng r, float level)
        {
            // Restrained ballast vibration: mains fundamental and a little mechanical hiss, no crackling arcing bed.
            var y = Hum(sec, r, 120f, 3, 2.3f, 0f);
            var hiss = Dsp.Noise(sec, r);
            Dsp.Filter(hiss, Biquad.BandPass(1000, 0.6f));
            Dsp.Mix(y, hiss, 0, 0.025f);
            Dsp.Filter(y, Biquad.HighPass(80), Biquad.LowPass(650));
            return y;
        }

        static float[] LightTick(Rng r)
        {
            // A starter contact clicking inside its housing, a few milliseconds long; no laser zap or ringing bell.
            var y = Dsp.Buf(0.09);
            Dsp.Mix(y, Shaped(r, 0.045, 0.0003, 0.003, r.Range(1500, 2600), 0.7f), 0, 0.55f);
            Dsp.Mix(y, Shaped(r, 0.035, 0.0003, 0.004, 500, 0.8f), Dsp.N(0.001), 0.3f);
            Dsp.Filter(y, Biquad.HighPass(240), Biquad.LowPass(4800));
            Dsp.Fade(y, 0.0002, 0.015);
            return y;
        }

        // ---------------------------------------------------------------- weather
        static float[] Wind(double sec, Rng r, float lo, float hi, float whistle)
        {
            var src = Dsp.PinkNoise(sec, r);
            var y = Dsp.Buf(sec);
            var bp = Biquad.BandPass(400, 0.8);
            var wh = Biquad.BandPass(700, 18);
            float cf = (lo + hi) / 2, tgt = cf, g = 0.5f, gt = 0.5f, wf = 700, wt = 700;
            for (int i = 0; i < y.Length; i++)
            {
                if (i % 4410 == 0)
                {
                    tgt = r.Range(lo, hi);
                    gt = r.Range(0.25f, 1f);
                    wt = r.Range(lo * 1.5f, hi * 2f);
                }
                cf += (tgt - cf) * 0.00005f;
                g += (gt - g) * 0.00004f;
                wf += (wt - wf) * 0.00003f;
                if (i % 64 == 0)
                {
                    bp = Biquad.WithState(Biquad.BandPass(cf, 0.7), bp);
                    wh = Biquad.WithState(Biquad.BandPass(wf, 22), wh);
                }
                float s = src[i];
                y[i] = bp.Process(s) * g + wh.Process(s) * whistle * g * g;
            }
            return y;
        }

        static float[] Rain(double sec, Rng r, float lp, float drops)
        {
            var y = Dsp.PinkNoise(sec, r, 0.25f);
            Dsp.Filter(y, Biquad.HighPass(600), Biquad.LowPass(lp));
            int n = (int)(drops * sec);
            for (int k = 0; k < n; k++)
            {
                int o = (int)(r.Next() * y.Length);
                float a = r.Range(0.02f, 0.25f) * (r.Chance(0.02f) ? 3f : 1f);
                for (int j = 0; j < 30 && o + j < y.Length; j++) y[o + j] += a * r.Sym() * Dsp.Exp(-j / 4.0);
            }
            var b = Dsp.Buf(sec);
            Bubbles(b, r, 6, 1500, 4000, 0.2f, 0.002f, 0.008f);
            Dsp.Mix(y, b, 0, 1f);
            return y;
        }

        // ---------------------------------------------------------------- the catalog
        public static IEnumerable<Baked> All()
        {
            // footsteps --------------------------------------------------------------------
            string[] surfaces = { "carpet", "wood", "tile", "concrete", "metal", "water", "rubble", "rubber", "escalator" };
            int si = 0;
            foreach (var s in surfaces)
            {
                si++;
                foreach (var b in Var("fs_" + s, 8, 100 + si, r => Step(s, r, false), 0f, 0.85f)) yield return b;
                foreach (var b in Var("land_" + s, 3, 200 + si, r => Step(s, r, true), 0f, 0.95f)) yield return b;
            }
            foreach (var b in Var("cloth", 6, 300, r =>
            {
                var x = Dsp.Noise(r.Range(0.15f, 0.35f), r);
                Env(x, t => Dsp.Sin(Math.PI * Math.Min(1, t / 0.3)) * (0.5f + 0.5f * r.Next()));
                Dsp.Filter(x, Biquad.BandPass(r.Range(2500, 5000), 0.6));
                return x;
            }, 0f, 0.4f)) yield return b;
            yield return M("monster_breath", LoopOf(5.2, 0.4, sec =>
            {
                // slow, wet, too-deep breathing: noise through a low vocal-tract resonance, irregular cycle
                var r = new Rng(91);
                var x = Dsp.Noise(sec, r);
                var f1 = Biquad.BandPass(260, 2.5);
                var f2 = Biquad.BandPass(720, 3);
                var lp = Biquad.LowPass(1400);
                double ph = 0;
                for (int i = 0; i < x.Length; i++)
                {
                    double t = (double)i / SR;
                    ph += (1.0 / 2.6 + 0.05 * Math.Sin(t * 0.7)) / SR;
                    double cyc = ph - Math.Floor(ph);
                    float env = cyc < 0.45 ? Dsp.Sin(Math.PI * cyc / 0.45) : 0.7f * Dsp.Sin(Math.PI * (cyc - 0.45) / 0.55);
                    float rasp = 0.6f + 0.4f * Dsp.Sin(Dsp.TAU * 38 * t) * env;
                    float s0 = x[i];
                    x[i] = lp.Process(f1.Process(s0) + 0.5f * f2.Process(s0)) * env * env * rasp;
                }
                return x;
            }, 0.08f), true);
            yield return M("breath_heavy", LoopOf(3.6, 0.3, sec =>
            {
                var r = new Rng(77);
                var x = Dsp.Noise(sec, r);
                var f = Biquad.BandPass(1100, 1.2);
                var f2 = Biquad.BandPass(2400, 2);
                for (int i = 0; i < x.Length; i++)
                {
                    double t = (double)i / SR;
                    double cyc = (t % 1.8) / 1.8;
                    float env = cyc < 0.42 ? Dsp.Sin(Math.PI * cyc / 0.42) * 0.7f : Dsp.Sin(Math.PI * (cyc - 0.42) / 0.58);
                    float s = x[i];
                    x[i] = (f.Process(s) + 0.4f * f2.Process(s)) * env * env;
                }
                return x;
            }, 0.06f), true);

            // doors -------------------------------------------------------------------------
            foreach (var b in Var("door_creak", 7, 400, r => DoorCreak(r, false), 0f, 0.32f)) yield return b;
            foreach (var b in Var("door_close", 4, 410, r => DoorClose(r, false))) yield return b;
            foreach (var b in Var("door_creak_metal", 5, 420, r => DoorCreak(r, true), 0f, 0.32f)) yield return b;
            foreach (var b in Var("door_close_metal", 3, 430, r => DoorClose(r, true))) yield return b;
            foreach (var b in Var("door_locked", 3, 440, DoorLocked)) yield return b;
            // latch bolt sliding out of the strike plate as the handle turns
            foreach (var b in Var("door_unlatch", 3, 450, r =>
            {
                var y = Dsp.Buf(0.35);
                Dsp.Mix(y, Shaped(r, 0.03, 0.0005, 0.004, r.Range(2400, 3400), 2.5f), 0, 0.5f);
                Dsp.Mix(y, Shaped(r, 0.08, 0.01, 0.02, r.Range(1200, 1800), 1.2f), Dsp.N(0.02), 0.25f);
                Dsp.Mix(y, Shaped(r, 0.04, 0.0005, 0.006, r.Range(1800, 2600), 2.5f), Dsp.N(r.Range(0.07f, 0.12f)), 0.4f);
                return y;
            })) yield return b;
            foreach (var b in Var("door_unlatch_metal", 2, 460, r =>
            {
                var y = Dsp.Buf(0.5);
                Dsp.Mix(y, Shaped(r, 0.2, 0.0005, 0.05, r.Range(1500, 2200), 14f), 0, 0.3f);
                Dsp.Mix(y, Shaped(r, 0.05, 0.0005, 0.006, 3000, 2f), 0, 0.5f);
                Dsp.Mix(y, Shaped(r, 0.25, 0.0005, 0.08, r.Range(900, 1300), 16f), Dsp.N(0.1), 0.3f);
                return y;
            })) yield return b;

            // lights / power -----------------------------------------------------------------
            yield return M("light_buzz", LoopOf(4, 0.4, sec => Buzz(sec, new Rng(500), 1f), 0.025f), true);
            foreach (var b in Var("light_tick", 8, 510, LightTick, 0f, 0.18f)) yield return b;
            yield return M("light_pop", Make(r =>
            {
                var y = Dsp.Buf(1.2);
                Click(y, 0, r, 1200, 1f, 0.003f);
                for (int k = 0; k < 12; k++)
                    Dsp.Modes(y, Dsp.N(0.02 + r.Next() * 0.4), new[] { r.Range(3000, 7000) }, new[] { 0.03f }, new[] { 0.15f }, r);
                return y;
            }, 520));
            yield return M("breaker_clunk", Make(r =>
            {
                var y = Dsp.Buf(1.0);
                Dsp.Modes(y, 0, new[] { 210f, 470f, 920f, 1610f, 2400f }, new[] { 0.18f, 0.12f, 0.08f, 0.05f, 0.03f },
                          new[] { 0.6f, 0.5f, 0.35f, 0.25f, 0.15f }, r, 0.03f);
                Thud(y, 0, r, 60, 0.07f, 0.9f);
                Click(y, Dsp.N(0.01), r, 2000, 0.5f);
                return y;
            }, 530));
            yield return M("power_down", Make(r =>
            {
                var y = Dsp.Buf(2.6);
                double ph = 0;
                for (int i = 0; i < y.Length; i++)
                {
                    float t = (float)i / SR;
                    float f = 120 * Dsp.Exp(-t / 0.9) + 18;
                    ph += TAU * f / SR;
                    float s = (float)(Math.Sin(ph) + 0.5 * Math.Sin(2 * ph) + 0.3 * Math.Sin(3 * ph));
                    y[i] = Dsp.SoftClip(s, 2) * Dsp.Exp(-t / 1.1) * Math.Min(1, t / 0.01f);
                }
                Dsp.Filter(y, Biquad.LowPass(2000));
                for (int k = 0; k < 4; k++) Click(y, Dsp.N(0.05 + k * 0.4 + r.Next() * 0.2), r, 2500, 0.3f);
                return y;
            }, 540));
            yield return M("power_up", Make(r =>
            {
                var y = Dsp.Buf(2.2);
                double ph = 0;
                for (int i = 0; i < y.Length; i++)
                {
                    float t = (float)i / SR;
                    float f = 120 - 90 * Dsp.Exp(-t / 0.5);
                    ph += TAU * f / SR;
                    float s = (float)(Math.Sin(ph) + 0.5 * Math.Sin(2 * ph) + 0.3 * Math.Sin(3 * ph));
                    y[i] = Dsp.SoftClip(s, 2) * Math.Min(1, t / 0.6f) * Math.Min(1, (2.2f - t) / 0.5f);
                }
                Dsp.Filter(y, Biquad.LowPass(2500));
                for (int k = 0; k < 7; k++) Dsp.Mix(y, LightTick(r), Dsp.N(0.3 + r.Next() * 1.4), 0.4f);
                return y;
            }, 550));

            // elevator ------------------------------------------------------------------------
            yield return M("elev_motor", LoopOf(6, 0.5, sec =>
            {
                var r = new Rng(600);
                var y = Hum(sec, r, 60, 12, 1.1f, 0.2f);
                Dsp.Gain(y, 0.5f);
                var w = Hum(sec, r, 410, 4, 1.5f, 0f);
                Dsp.Mix(y, w, 0, 0.12f);
                var rum = Dsp.BrownNoise(sec, r);
                Dsp.Filter(rum, Biquad.LowPass(180));
                Dsp.Mix(y, rum, 0, 0.6f);
                var rat = Dsp.Noise(sec, r);
                Env(rat, t => 0.5f + 0.5f * Dsp.Sin(TAU * 11 * t) * Dsp.Sin(TAU * 0.3 * t));
                Dsp.Filter(rat, Biquad.BandPass(1300, 2));
                Dsp.Mix(y, rat, 0, 0.05f);
                return y;
            }, 0.12f), true);
            yield return M("elev_start", Make(r =>
            {
                var y = Dsp.Buf(2.0);
                Dsp.Modes(y, 0, new[] { 2100f, 3300f }, new[] { 0.02f, 0.015f }, new[] { 0.5f, 0.3f }, r);
                Thud(y, Dsp.N(0.15), r, 55, 0.12f, 1f);
                Dsp.Modes(y, Dsp.N(0.15), new[] { 300f, 520f, 880f }, new[] { 0.2f, 0.15f, 0.1f }, new[] { 0.3f, 0.2f, 0.1f }, r);
                var whine = Dsp.Buf(1.6);
                double ph = 0;
                for (int i = 0; i < whine.Length; i++)
                {
                    float t = (float)i / SR;
                    ph += TAU * (160 + 250 * Math.Min(1, t / 1.2f)) / SR;
                    whine[i] = (float)Math.Sin(ph) * Math.Min(1, t / 0.5f) * Math.Min(1, (1.6f - t) / 0.4f) * 0.15f;
                }
                Dsp.Mix(y, whine, Dsp.N(0.3));
                return y;
            }, 610));
            yield return M("elev_stop", Make(r =>
            {
                var y = Dsp.Buf(2.2);
                var whine = Dsp.Buf(1.0);
                double ph = 0;
                for (int i = 0; i < whine.Length; i++)
                {
                    float t = (float)i / SR;
                    ph += TAU * (410 - 250 * Math.Min(1, t)) / SR;
                    whine[i] = (float)Math.Sin(ph) * (1 - t) * 0.15f;
                }
                Dsp.Mix(y, whine, 0);
                Thud(y, Dsp.N(0.95), r, 50, 0.15f, 1f);
                Dsp.Modes(y, Dsp.N(0.95), new[] { 240f, 410f, 690f, 1100f }, new[] { 0.3f, 0.2f, 0.15f, 0.1f }, new[] { 0.35f, 0.25f, 0.2f, 0.1f }, r);
                Dsp.Mix(y, StickSlip(r, 0.6, 40, 90, new[] { 300f, 700f }, new[] { 6f, 8f }), Dsp.N(1.2), 0.25f);
                return y;
            }, 620));
            yield return M("elev_door", Make(r =>
            {
                var y = Dsp.Buf(2.0);
                var roll = Dsp.BrownNoise(1.6, r);
                Dsp.Filter(roll, Biquad.LowPass(500));
                Env(roll, t => Math.Min(1, t / 0.2f) * Math.Min(1, (1.6f - t) / 0.3f));
                Dsp.Mix(y, roll, 0, 0.8f);
                var mot = Hum(1.6, r, 95, 6, 1.2f, 0.3f);
                Env(mot, t => Math.Min(1, t / 0.15f) * Math.Min(1, (1.6f - t) / 0.2f));
                Dsp.Mix(y, mot, 0, 0.15f);
                Grit(y, 0, r, 60, 1.5, 0.12f, 2000);
                Thud(y, Dsp.N(1.55), r, 90, 0.08f, 0.6f);
                Dsp.Modes(y, Dsp.N(1.55), new[] { 600f, 1100f }, new[] { 0.05f, 0.04f }, new[] { 0.2f, 0.1f }, r);
                return y;
            }, 630));
            yield return M("elev_chime", Make(r =>
            {
                var y = Dsp.Buf(2.4);
                Dsp.Mix(y, Inst.Bell(1318.5f, 1.8, 1f, r, 0.9f), 0);
                Dsp.Mix(y, Inst.Bell(1046.5f, 1.9, 1f, r, 1.0f), Dsp.N(0.42));
                Dsp.Filter(y, Biquad.LowPass(6000));
                return y;
            }, 640));
            yield return M("elev_button", Make(r =>
            {
                var y = Dsp.Buf(0.25);
                Dsp.Modes(y, 0, new[] { 2400f, 3900f, 1200f }, new[] { 0.01f, 0.007f, 0.015f }, new[] { 0.5f, 0.3f, 0.3f }, r);
                Thud(y, 0, r, 200, 0.02f, 0.3f);
                return y;
            }, 650));
            foreach (var b in Var("elev_creak", 3, 660, r => StickSlip(r, r.Range(0.8f, 1.6f), 25, 70, new[] { 180f, 420f, 900f }, new[] { 5f, 7f, 9f }, 0.5f)))
                yield return b;
            yield return M("elev_shaft_wind", LoopOf(8, 0.6, sec => Wind(sec, new Rng(670), 150, 450, 0.3f), 0.08f), true);

            // ambience loops -------------------------------------------------------------------
            {
                var r = new Rng(700);
                double sec = 12;
                Func<Rng, float[]> bed = rr =>
                {
                    var b = Dsp.BrownNoise(sec + 1, rr);
                    Dsp.Filter(b, Biquad.LowPass(220));
                    var p = Dsp.PinkNoise(sec + 1, rr, 0.15f);
                    Dsp.Filter(p, Biquad.LowPass(900), Biquad.HighPass(120));
                    Dsp.Mix(b, p, 0);
                    Dsp.Mix(b, Hum(sec + 1, rr, 60, 4, 1.2f, 0f), 0, 0.03f);
                    return Dsp.Loop(b, 1.0);
                };
                var L = bed(r);
                var R = bed(new Rng(701));
                Dsp.NormalizeRms(L, 0.05f);
                Dsp.NormalizeRms(R, 0.05f);
                yield return new Baked { name = "amb_room_tone", left = L, right = R, loop = true };
            }
            yield return M("amb_room_tone_low", LoopOf(10, 1, sec =>
            {
                var b = Dsp.BrownNoise(sec, new Rng(702));
                Dsp.Filter(b, Biquad.LowPass(120));
                return b;
            }, 0.08f), true);
            yield return M("amb_fridge", LoopOf(8, 0.6, sec =>
            {
                var r = new Rng(710);
                var y = Hum(sec, r, 60, 10, 1.0f, 0.5f);
                Env(y, t => 0.9f + 0.1f * Dsp.Sin(TAU * 3.1 * t));
                var fan = Dsp.PinkNoise(sec, r);
                Dsp.Filter(fan, Biquad.LowPass(900));
                Dsp.Mix(y, fan, 0, 0.6f);
                return y;
            }, 0.08f), true);
            yield return M("amb_vending", LoopOf(8, 0.6, sec =>
            {
                var r = new Rng(720);
                var y = Hum(sec, r, 120, 14, 0.9f, 0.3f);
                Dsp.Gain(y, 0.6f);
                var fan = Dsp.PinkNoise(sec, r);
                Dsp.Filter(fan, Biquad.BandPass(700, 0.7));
                Dsp.Mix(y, fan, 0, 0.8f);
                Dsp.Mix(y, Hum(sec, r, 7800, 1, 1, 0), 0, 0.01f);
                return y;
            }, 0.08f), true);
            yield return M("amb_ice", LoopOf(9, 0.6, sec =>
            {
                var r = new Rng(730);
                var y = Hum(sec, r, 60, 8, 1.1f, 0.2f);
                Dsp.Gain(y, 0.5f);
                var w = Dsp.Buf(sec);
                Bubbles(w, r, 30, 800, 2600, 0.12f);
                Dsp.Mix(y, w, 0);
                return y;
            }, 0.07f), true);
            yield return M("amb_boiler", LoopOf(10, 0.8, sec =>
            {
                var r = new Rng(740);
                var roar = Dsp.BrownNoise(sec, r);
                Dsp.Filter(roar, Biquad.LowPass(280));
                var fl = Dsp.Noise(sec, r);
                Dsp.Filter(fl, Biquad.BandPass(160, 0.8));
                Env(fl, t => 0.6f + 0.4f * Dsp.Sin(TAU * 8.7 * t) * Dsp.Sin(TAU * 0.4 * t + 1));
                Dsp.Mix(roar, fl, 0, 0.6f);
                var hiss = Dsp.Noise(sec, r);
                Dsp.Filter(hiss, Biquad.HighPass(4000));
                Dsp.Mix(roar, hiss, 0, 0.03f);
                for (int k = 0; k < 6; k++) Dsp.Modes(roar, (int)(r.Next() * roar.Length * 0.9), new[] { r.Range(400, 900), r.Range(1200, 2000) },
                                                      new[] { 0.1f, 0.06f }, new[] { 0.2f, 0.1f }, r);
                return roar;
            }, 0.12f), true);
            yield return M("amb_pump", LoopOf(8, 0.6, sec =>
            {
                var r = new Rng(750);
                var y = Hum(sec, r, 360, 6, 1.2f, 0.2f);
                Dsp.Gain(y, 0.4f);
                for (double t = 0; t < sec; t += 0.4) Thud(y, Dsp.N(t), r, 70, 0.08f, 0.6f);
                var w = Dsp.Buf(sec);
                Bubbles(w, r, 50, 300, 1200, 0.15f);
                Dsp.Mix(y, w, 0);
                return y;
            }, 0.1f), true);
            yield return M("amb_water_flow", LoopOf(12, 0.8, sec =>
            {
                var r = new Rng(760);
                var y = Dsp.PinkNoise(sec, r, 0.3f);
                Dsp.Filter(y, Biquad.LowPass(1500), Biquad.HighPass(150));
                Bubbles(y, r, 220, 350, 2400, 0.12f);
                return y;
            }, 0.09f), true);
            yield return M("amb_water_trickle", LoopOf(8, 0.6, sec =>
            {
                var r = new Rng(770);
                var y = Dsp.PinkNoise(sec, r, 0.08f);
                Dsp.Filter(y, Biquad.HighPass(1500));
                Bubbles(y, r, 45, 1100, 3600, 0.2f, 0.003f, 0.012f);
                return y;
            }, 0.06f), true);
            yield return M("amb_faucet", LoopOf(6, 0.5, sec =>
            {
                var r = new Rng(780);
                var y = Dsp.Noise(sec, r, 0.4f);
                Dsp.Filter(y, Biquad.BandPass(1800, 0.6));
                Bubbles(y, r, 400, 500, 3000, 0.1f);
                return y;
            }, 0.1f), true);
            yield return M("amb_faucet_overflow", LoopOf(8, 0.6, sec =>
            {
                var r = new Rng(785);
                var y = Dsp.Noise(sec, r, 0.3f);
                Dsp.Filter(y, Biquad.BandPass(1200, 0.5));
                Bubbles(y, r, 250, 250, 2000, 0.14f);
                var sl = Dsp.BrownNoise(sec, r);
                Dsp.Filter(sl, Biquad.LowPass(300));
                Env(sl, t => 0.5f + 0.5f * Dsp.Sin(TAU * 0.6 * t));
                Dsp.Mix(y, sl, 0, 0.4f);
                return y;
            }, 0.1f), true);
            yield return M("amb_steam", LoopOf(8, 0.6, sec =>
            {
                var r = new Rng(790);
                var y = Dsp.Noise(sec, r);
                Dsp.Filter(y, Biquad.HighPass(2500), Biquad.LowPass(9000));
                Env(y, t => 0.6f + 0.4f * Dsp.Sin(TAU * 0.25 * t) * Dsp.Sin(TAU * 1.7 * t));
                return y;
            }, 0.07f), true);
            yield return M("amb_generator", LoopOf(6, 0.5, sec =>
            {
                var r = new Rng(800);
                var y = Hum(sec, r, 29, 18, 0.8f, 0.8f);
                var clat = Dsp.Noise(sec, r);
                Dsp.Filter(clat, Biquad.BandPass(2000, 1));
                Env(clat, t => 0.5f + 0.5f * Dsp.Sin(TAU * 29 * t));
                Dsp.Mix(y, clat, 0, 0.15f);
                return y;
            }, 0.12f), true);
            yield return M("amb_transformer", LoopOf(6, 0.5, sec => Hum(sec, new Rng(810), 120, 8, 1.4f, 0.15f), 0.08f), true);
            yield return M("amb_wind", LoopOf(16, 1.2, sec => Wind(sec, new Rng(820), 250, 900, 0.5f), 0.08f), true);
            yield return M("amb_wind_shaft", LoopOf(16, 1.2, sec => Wind(sec, new Rng(830), 120, 420, 0.8f), 0.08f), true);
            yield return M("amb_chute_wind", LoopOf(10, 1, sec => Wind(sec, new Rng(835), 100, 300, 0.4f), 0.06f), true);
            yield return M("amb_crawl_air", LoopOf(8, 0.8, sec =>
            {
                var y = Wind(sec, new Rng(838), 400, 1200, 0.1f);
                Grit(y, 0, new Rng(839), 30, sec - 0.1, 0.1f, 3000);
                return y;
            }, 0.04f), true);
            yield return M("amb_rain", LoopOf(12, 1, sec => Rain(sec, new Rng(840), 9000, 1800), 0.09f), true);
            yield return M("amb_rain_roof", LoopOf(12, 1, sec =>
            {
                var y = Rain(sec, new Rng(850), 2500, 900);
                var rum = Dsp.BrownNoise(sec, new Rng(851));
                Dsp.Filter(rum, Biquad.LowPass(150));
                Dsp.Mix(y, rum, 0, 0.4f);
                return y;
            }, 0.09f), true);
            yield return M("amb_clock_tick", Make(r =>
            {
                var y = Dsp.Buf(2.0);
                Dsp.Modes(y, 0, new[] { 3200f, 4800f, 1900f }, new[] { 0.015f, 0.01f, 0.02f }, new[] { 0.6f, 0.3f, 0.2f }, r);
                Dsp.Modes(y, Dsp.N(1.0), new[] { 2500f, 3600f, 1500f }, new[] { 0.015f, 0.01f, 0.025f }, new[] { 0.6f, 0.3f, 0.25f }, r);
                Grit(y, Dsp.N(0.02), r, 6, 0.05, 0.05f, 5000);
                Grit(y, Dsp.N(1.02), r, 6, 0.05, 0.05f, 5000);
                return y;
            }, 860), true);
            yield return M("amb_fan", LoopOf(3.54, 0.3, sec =>
            {
                var r = new Rng(870);
                var y = Dsp.PinkNoise(sec, r);
                Dsp.Filter(y, Biquad.BandPass(380, 0.8));
                Env(y, t => 0.5f + 0.5f * Dsp.Sin(TAU * 3.39 * t));
                Dsp.Mix(y, Hum(sec, r, 120, 3, 1.5f, 0f), 0, 0.05f);
                for (double t = 0.2; t < sec; t += 0.885) Click(y, Dsp.N(t), r, 1500, 0.15f);
                return y;
            }, 0.06f), true);
            yield return M("amb_elevator_machine", LoopOf(8, 0.6, sec =>
            {
                var r = new Rng(880);
                var y = Hum(sec, r, 60, 10, 1.0f, 0.3f);
                Dsp.Mix(y, Hum(sec, r, 600, 3, 1.5f, 0f), 0, 0.08f);
                for (int k = 0; k < 3; k++) Click(y, (int)(r.Next() * y.Length * 0.9), r, 2000, 0.4f);
                return y;
            }, 0.1f), true);
            yield return M("amb_escalator", LoopOf(6, 0.4, sec =>
            {
                var r = new Rng(890);
                var y = Hum(sec, r, 50, 8, 1.0f, 0.2f);
                Dsp.Gain(y, 0.5f);
                for (double t = 0; t < sec; t += 0.42)
                {
                    Dsp.Modes(y, Dsp.N(t), new[] { 190f, 330f, 610f }, new[] { 0.05f, 0.04f, 0.02f }, new[] { 0.35f, 0.2f, 0.12f }, r, 0.05f);
                }
                var ch = Dsp.Noise(sec, r);
                Dsp.Filter(ch, Biquad.BandPass(2400, 1.5));
                Env(ch, t => 0.5f + 0.5f * Dsp.Sin(TAU * 17 * t));
                Dsp.Mix(y, ch, 0, 0.04f);
                return y;
            }, 0.1f), true);
            yield return M("amb_tv_static", LoopOf(6, 0.4, sec =>
            {
                var r = new Rng(900);
                var y = Dsp.Noise(sec, r);
                Dsp.Filter(y, Biquad.HighPass(250), Biquad.LowPass(8000));
                Dsp.Mix(y, Hum(sec, r, 15734, 1, 1, 0), 0, 0.03f);
                Dsp.Mix(y, Hum(sec, r, 60, 6, 1, 0.5f), 0, 0.05f);
                return y;
            }, 0.1f), true);
            yield return M("amb_projector", LoopOf(4, 0.3, sec =>
            {
                var r = new Rng(910);
                var y = Hum(sec, r, 180, 6, 1.2f, 0.3f);
                Dsp.Gain(y, 0.3f);
                for (double t = 0; t < sec; t += 1 / 24.0) Click(y, Dsp.N(t), r, 1500, 0.1f, 0.002f);
                var fan = Dsp.PinkNoise(sec, r);
                Dsp.Filter(fan, Biquad.BandPass(900, 0.8));
                Dsp.Mix(y, fan, 0, 0.3f);
                return y;
            }, 0.08f), true);
            yield return M("amb_washer", LoopOf(10, 0.8, sec =>
            {
                var r = new Rng(920);
                var y = Dsp.BrownNoise(sec, r);
                Dsp.Filter(y, Biquad.LowPass(420));
                Env(y, t => 0.4f + 0.6f * Math.Max(0, Dsp.Sin(TAU * 0.7 * t)));
                Bubbles(y, r, 60, 200, 900, 0.1f);
                Dsp.Mix(y, Hum(sec, r, 60, 5, 1.2f, 0.1f), 0, 0.1f);
                return y;
            }, 0.09f), true);
            yield return M("amb_dryer", LoopOf(8, 0.6, sec =>
            {
                var r = new Rng(930);
                var y = Hum(sec, r, 60, 6, 1.1f, 0.2f);
                Dsp.Gain(y, 0.3f);
                for (double t = 0.1; t < sec; t += r.Range(0.7f, 0.95f)) Thud(y, Dsp.N(t), r, r.Range(60, 110), 0.08f, 0.6f);
                for (int k = 0; k < 4; k++) Dsp.Modes(y, (int)(r.Next() * y.Length * 0.9), new[] { 2800f, 4100f }, new[] { 0.02f, 0.015f },
                                                      new[] { 0.2f, 0.1f }, r);
                return y;
            }, 0.1f), true);
            yield return M("amb_plastic_flap", LoopOf(8, 0.6, sec =>
            {
                var r = new Rng(940);
                var y = Dsp.Buf(sec);
                for (double t = 0; t < sec; t += r.Range(0.15f, 1.4f))
                {
                    var b = Dsp.Noise(r.Range(0.05f, 0.25f), r);
                    Env(b, tt => Dsp.Sin(Math.PI * Math.Min(1, tt / 0.25)) * (r.Chance(0.5f) ? 1 : 0.6f));
                    Dsp.Filter(b, Biquad.HighPass(1500), Biquad.Peak(4000, 1, 6));
                    Dsp.Mix(y, b, Dsp.N(t), r.Range(0.3f, 1f));
                }
                return y;
            }, 0.05f), true);

            // random one-shots ------------------------------------------------------------------
            foreach (var b in Var("amb_drip", 14, 1000, r =>
            {
                // Tiny impact on a damp hard surface, followed by scattered micro-splashes. No pitched bubble sweep.
                var y = Dsp.Buf(0.18);
                Dsp.Mix(y, Shaped(r, 0.026, 0.00025, 0.0035, r.Range(2400, 4800), 0.6f), 0, 0.65f);
                Dsp.Mix(y, Shaped(r, 0.035, 0.0005, 0.006, r.Range(650, 1150), 0.7f), Dsp.N(0.0015), 0.16f);
                int specks = r.Int(2, 6);
                for (int k = 0; k < specks; k++)
                    Dsp.Mix(y, Shaped(r, 0.014, 0.0002, 0.002, r.Range(3300, 6500), 0.65f),
                            Dsp.N(r.Range(0.006f, 0.038f)), r.Range(0.025f, 0.085f));
                Dsp.Filter(y, Biquad.HighPass(400), Biquad.LowPass(7800));
                Dsp.Fade(y, 0.00015, 0.025);
                return y;
            }, 0f, 0.4f)) yield return b;

            foreach (var b in Var("amb_pipe_knock", 6, 1010, r =>
            {
                var y = Dsp.Buf(1.6);
                float f = r.Range(160, 460);
                int knocks = r.Chance(0.4f) ? r.Int(2, 4) : 1;
                for (int k = 0; k < knocks; k++)
                {
                    int o = Dsp.N(k * r.Range(0.12f, 0.3f));
                    Dsp.Modes(y, o, new[] { f, f * 2.76f, f * 5.4f, f * 8.9f }, new[] { 0.7f, 0.35f, 0.15f, 0.07f },
                              new[] { 0.6f, 0.4f, 0.2f, 0.1f }, r, 0.02f);
                    Thud(y, o, r, 80, 0.06f, 0.5f);
                }
                return y;
            })) yield return b;
            foreach (var b in Var("amb_creak", 6, 1020, r => DoorCreak(r, false)))
                yield return b;
            foreach (var b in Var("amb_settle", 3, 1030, r =>
            {
                var y = StickSlip(r, r.Range(1.5f, 3f), 12, 45, new[] { 90f, 210f, 450f }, new[] { 4f, 5f, 6f }, 0.8f);
                Grit(y, Dsp.N(0.5), r, 30, 0.8, 0.2f, 1800);
                return y;
            })) yield return b;
            foreach (var b in Var("amb_door_slam_far", 3, 1040, r => DoorClose(r, r.Chance(0.3f)))) yield return b;
            foreach (var b in Var("amb_debris", 4, 1050, r =>
            {
                var y = Dsp.Buf(1.5);
                double t = 0;
                int n = r.Int(8, 25);
                for (int k = 0; k < n; k++)
                {
                    t += r.Range(0.01f, 0.09f) * (1 + k * 0.05f);
                    Dsp.Modes(y, Dsp.N(t), new[] { r.Range(1500, 5000), r.Range(800, 2500) }, new[] { 0.01f, 0.015f }, new[] { 0.3f, 0.2f }, r);
                }
                var dust = Dsp.Noise(1.2, r);
                Env(dust, tt => Dsp.Exp(-tt / 0.4) * 0.3f);
                Dsp.Filter(dust, Biquad.BandPass(3000, 0.7));
                Dsp.Mix(y, dust, 0);
                return y;
            })) yield return b;
            foreach (var b in Var("amb_glass", 3, 1060, r =>
            {
                var y = Dsp.Buf(1.4);
                for (int k = 0; k < r.Int(3, 9); k++)
                    Dsp.Modes(y, Dsp.N(r.Next() * 0.6), new[] { r.Range(3000, 7500), r.Range(5000, 9000) }, new[] { 0.06f, 0.03f },
                              new[] { 0.3f, 0.15f }, r);
                return y;
            })) yield return b;
            foreach (var b in Var("amb_metal_groan", 3, 1070, r =>
                StickSlip(r, r.Range(1.5f, 3f), 18, 60, new[] { 140f, 330f, 760f, 1500f }, new[] { 8f, 10f, 12f, 14f }, 0.3f))) yield return b;

            // devices ---------------------------------------------------------------------------
            yield return M("radio_static", LoopOf(6, 0.4, sec =>
            {
                var r = new Rng(1100);
                var y = Dsp.Noise(sec, r);
                Dsp.Filter(y, Biquad.HighPass(200), Biquad.LowPass(5000));
                for (int k = 0; k < 40; k++) Click(y, (int)(r.Next() * y.Length * 0.95), r, 800, r.Range(0.2f, 0.8f), 0.003f);
                double ph = 0;
                for (int i = 0; i < y.Length; i++)
                {
                    double t = (double)i / SR;
                    ph += TAU * (1800 + 900 * Math.Sin(TAU * 0.11 * t)) / SR;
                    y[i] += 0.05f * (float)Math.Sin(ph);
                }
                return y;
            }, 0.1f), true);
            yield return M("radio_click", Make(r =>
            {
                var y = Dsp.Buf(0.3);
                Dsp.Modes(y, 0, new[] { 1700f, 2900f }, new[] { 0.012f, 0.008f }, new[] { 0.5f, 0.3f }, r);
                Thud(y, 0, r, 160, 0.03f, 0.3f);
                return y;
            }, 1110));
            yield return M("radio_station_0", Music.AmRadio(Music.Swing(1201, true), 1201, 0.015f), true);
            yield return M("radio_station_1", Music.AmRadio(Music.TalkShow(1202, 48, false), 1202, 0.02f), true);
            yield return M("radio_station_2", Music.AmRadio(Music.Quartet(1203), 1203, 0.01f, false), true);
            yield return M("radio_station_3", Music.AmRadio(Music.Crooner(1204), 1204, 0.015f), true);
            yield return M("radio_station_4", Music.AmRadio(Music.News(1205, 40), 1205, 0.02f), true);
            yield return M("radio_station_5", Music.AmRadio(Music.Numbers(1206, 36), 1206, 0.03f), true);

            {
                var r = new Rng(1300);
                var show = Music.TalkShow(1301, 40, true);
                for (double t = 3; t < 38; t += r.Range(5, 9))
                {
                    var laugh = Dsp.Noise(r.Range(1.2f, 2.2f), r);
                    var bp = Biquad.BandPass(900, 0.8);
                    for (int i = 0; i < laugh.Length; i++)
                    {
                        float tt = (float)i / SR;
                        laugh[i] = bp.Process(laugh[i]) * Dsp.Sin(Math.PI * Math.Min(1, tt / (laugh.Length / (float)SR))) *
                                   (0.6f + 0.4f * Dsp.Sin(TAU * r.Range(5, 7) * tt));
                    }
                    Dsp.Mix(show, laugh, Dsp.N(t), 0.5f);
                }
                yield return M("tv_show", Music.AmRadio(show, 1302, 0.01f, false), true);
                yield return M("tv_news", Music.AmRadio(Music.News(1303, 40), 1303, 0.01f, false), true);
            }
            yield return M("tv_computer", LoopOf(8, 0.5, sec =>
            {
                var r = new Rng(1310);
                var y = Hum(sec, r, 15734, 1, 1, 0);
                Dsp.Gain(y, 0.2f);
                var hum = Hum(sec, r, 60, 6, 1.2f, 0.2f);
                Dsp.Mix(y, hum, 0, 0.3f);
                for (double t = 0.3; t < sec; t += r.Range(0.4f, 1.6f))
                {
                    var bp = Dsp.Buf(0.08);
                    float f = r.Pick(new[] { 880f, 1320f, 660f });
                    for (int i = 0; i < bp.Length; i++) bp[i] = Dsp.Sin(TAU * f * i / SR) > 0 ? 0.3f : -0.3f;
                    Dsp.Mix(y, bp, Dsp.N(t), 0.4f);
                }
                return y;
            }, 0.06f), true);
            yield return M("tv_click", Make(r =>
            {
                var y = Dsp.Buf(1.2);
                Thud(y, 0, r, 120, 0.05f, 0.8f);
                Click(y, 0, r, 1500, 0.6f);
                var deg = Hum(0.9, r, 60, 8, 0.8f, 0.6f);
                Env(deg, t => Dsp.Exp(-t / 0.25));
                Dsp.Mix(y, deg, Dsp.N(0.02), 0.5f);
                return y;
            }, 1320));

            yield return M("phone_ring", Make(r => Ring(r, false), 1400), true);
            yield return M("payphone_ring", Make(r => Ring(r, true), 1410), true);
            yield return M("phone_pickup", Make(r =>
            {
                var y = Dsp.Buf(0.6);
                Thud(y, 0, r, 220, 0.05f, 0.6f);
                Dsp.Modes(y, 0, new[] { 2200f, 3100f }, new[] { 0.02f, 0.01f }, new[] { 0.3f, 0.2f }, r);
                Dsp.Mix(y, Inst.Bell(1500, 0.5, 0.15f, r, 0.2f), Dsp.N(0.02));
                return y;
            }, 1420));
            yield return M("phone_hangup", Make(r =>
            {
                var y = Dsp.Buf(0.8);
                Thud(y, 0, r, 160, 0.08f, 0.9f);
                Dsp.Modes(y, 0, new[] { 1800f, 2700f }, new[] { 0.03f, 0.02f }, new[] { 0.3f, 0.2f }, r);
                Dsp.Mix(y, Inst.Bell(1500, 0.6, 0.3f, r, 0.3f), Dsp.N(0.01));
                return y;
            }, 1430));
            yield return M("phone_dial_tone", LoopOf(3, 0.2, sec =>
            {
                var y = Hum(sec, new Rng(1440), 350, 1, 1, 0);
                Dsp.Mix(y, Hum(sec, new Rng(1441), 440, 1, 1, 0), 0);
                return y;
            }, 0.08f), true);
            for (int k = 1; k <= 4; k++)
            {
                var r = new Rng(1450 + k);
                var v = k == 1 ? Speech.Male : k == 2 ? Speech.Female : k == 3 ? Speech.Announcer : new Speech.Voice { f0 = 160, breathy = 0.35f, rate = 3.2f, formant = 1.1f };
                var x = Speech.Babble(9, v, r);
                Dsp.Speaker(x, 320, 3300, 2.5f, 5, 1700);
                for (int i = 0; i < x.Length; i++) x[i] += r.Sym() * 0.015f;
                Dsp.Fade(x, 0.05, 0.4);
                Dsp.NormalizeRms(x, 0.12f);
                yield return M($"phone_voice_{k}", x);
            }
            yield return M("typewriter", Make(r =>
            {
                var y = Dsp.Buf(3.4);
                double t = 0.05;
                for (int k = 0; k < 18; k++)
                {
                    Dsp.Modes(y, Dsp.N(t), new[] { r.Range(1400, 2200), r.Range(3000, 4200), 600f }, new[] { 0.015f, 0.01f, 0.03f },
                              new[] { 0.6f, 0.3f, 0.4f }, r);
                    Thud(y, Dsp.N(t), r, 140, 0.03f, 0.4f);
                    t += r.Range(0.08f, 0.2f) + (r.Chance(0.15f) ? 0.25f : 0);
                }
                Dsp.Mix(y, Inst.Bell(2900, 1.0, 0.6f, r, 0.4f), Dsp.N(t + 0.05));
                var ret = Dsp.Noise(0.4, r);
                Dsp.Filter(ret, Biquad.BandPass(1800, 2));
                Env(ret, tt => Dsp.Sin(Math.PI * tt / 0.4));
                Dsp.Mix(y, ret, Dsp.N(t + 0.25), 0.4f);
                Thud(y, Dsp.N(t + 0.65), r, 120, 0.06f, 0.6f);
                return y;
            }, 1500));
            yield return M("bell_desk", Make(r =>
            {
                var y = Dsp.Buf(3.0);
                Dsp.Modes(y, 0, new[] { 2260f, 2290f, 5570f, 6120f, 9300f }, new[] { 1.6f, 1.5f, 0.5f, 0.4f, 0.15f },
                          new[] { 0.6f, 0.5f, 0.25f, 0.2f, 0.08f }, r);
                Click(y, 0, r, 3000, 0.3f);
                return y;
            }, 1510));
            yield return M("vending_buy", Make(r =>
            {
                var y = Dsp.Buf(3.0);
                Dsp.Mix(y, Hum(0.12, r, 1000, 1, 1, 0), 0, 0.2f);
                var mot = Hum(0.9, r, 140, 6, 1.1f, 0.4f);
                Env(mot, t => Math.Min(1, t / 0.05f) * Math.Min(1, (0.9f - t) / 0.1f));
                Dsp.Mix(y, mot, Dsp.N(0.25), 0.3f);
                Thud(y, Dsp.N(1.3), r, 90, 0.1f, 1f);
                Dsp.Modes(y, Dsp.N(1.3), new[] { 600f, 1300f, 2400f }, new[] { 0.15f, 0.1f, 0.06f }, new[] { 0.4f, 0.3f, 0.2f }, r);
                for (int k = 0; k < 6; k++)
                    Dsp.Modes(y, Dsp.N(1.5 + k * 0.12), new[] { 1300f + k * 30 }, new[] { 0.04f }, new[] { 0.15f * (1 - k / 6f) }, r);
                return y;
            }, 1520));
            yield return M("ice_drop", Make(r =>
            {
                var y = Dsp.Buf(1.6);
                for (int k = 0; k < 14; k++)
                    Dsp.Modes(y, Dsp.N(0.05 + r.Next() * 0.6 * (1 + k * 0.03)), new[] { r.Range(1800, 4500), r.Range(900, 1600) },
                              new[] { 0.02f, 0.03f }, new[] { 0.4f, 0.3f }, r);
                Thud(y, Dsp.N(0.05), r, 200, 0.05f, 0.5f);
                return y;
            }, 1530));
            yield return M("valve_squeak", Make(r => StickSlip(r, 0.5, 300, 600, new[] { 1200f, 2100f }, new[] { 12f, 14f }), 1540));
            yield return M("projector_on", Make(r =>
            {
                var y = Dsp.Buf(1.5);
                Thud(y, 0, r, 120, 0.04f, 0.7f);
                Click(y, 0, r, 1800, 0.5f);
                var sp = Hum(1.4, r, 180, 6, 1.2f, 0.3f);
                Env(sp, t => Math.Min(1, t / 1.0f) * 0.4f);
                Dsp.Mix(y, sp, Dsp.N(0.05));
                return y;
            }, 1550));
            yield return M("clock_chime", Make(r =>
            {
                var y = Dsp.Buf(7.0);
                int[] notes = { 76, 72, 74, 67 };
                for (int k = 0; k < notes.Length; k++) Dsp.Mix(y, Inst.Bell(Dsp.MidiHz(notes[k]), 2.5, 0.9f, r, 1.4f), Dsp.N(k * 0.75));
                for (int k = 0; k < 3; k++) Dsp.Mix(y, Inst.Bell(Dsp.MidiHz(55), 2.5, 1f, r, 1.8f), Dsp.N(3.4 + k * 1.1));
                return y;
            }, 1560));

            // music devices ----------------------------------------------------------------------
            foreach (var tune in new[] { "lullaby", "carousel", "waltz_box", "lullaby_reverse" })
                yield return M("musicbox_" + tune, Music.MusicBox(tune, 1600 + tune.Length), true);
            yield return M("musicbox_wind", Make(r =>
            {
                var y = Dsp.Buf(1.4);
                for (int k = 0; k < 12; k++)
                    Dsp.Modes(y, Dsp.N(k * 0.1 + r.Next() * 0.01), new[] { 3500f, 5200f, 1800f }, new[] { 0.008f, 0.006f, 0.012f },
                              new[] { 0.4f, 0.2f, 0.2f }, r);
                return y;
            }, 1610));
            var waltz = Music.Waltz(1700);
            yield return M("gramophone_waltz", Music.Gramophone(waltz, 1701, 0.3f), true);
            yield return M("gramophone_waltz_slow", Music.Gramophone(Music.Waltz(1702, true, 108), 1703, 0.45f), true);
            yield return M("gramophone_love_song", Music.Gramophone(Music.LoveSong(1704), 1705, 0.35f), true);
            yield return M("gramophone_waltz_old", Music.Gramophone(Music.Waltz(1706, true, 132), 1707, 0.9f), true);
            yield return M("gramophone_needle", Make(r =>
            {
                var y = Dsp.Buf(1.5);
                Thud(y, 0, r, 90, 0.05f, 0.6f);
                Dsp.VinylCrackle(y, r, 120, 0.15f, 0.02f);
                Env(y, t => Math.Min(1, (1.5f - t) / 0.5f));
                return y;
            }, 1710));
            yield return M("juke_doowop", Music.DooWop(1800), true);
            yield return M("juke_ballad", Music.Ballad(1801), true);
            yield return M("juke_swing", Music.Swing(1802, true), true);
            yield return M("juke_select", Make(r =>
            {
                var y = Dsp.Buf(2.6);
                for (int k = 0; k < 4; k++) Thud(y, Dsp.N(k * 0.3), r, 100 + k * 20, 0.06f, 0.6f);
                var mot = Hum(1.0, r, 110, 5, 1.2f, 0.3f);
                Env(mot, t => Math.Min(1, t / 0.1f) * Math.Min(1, (1f - t) / 0.2f));
                Dsp.Mix(y, mot, Dsp.N(0.4), 0.3f);
                Thud(y, Dsp.N(1.5), r, 80, 0.08f, 0.8f);
                Dsp.VinylCrackle(y, r, 80, 0.1f, 0.01f);
                return y;
            }, 1810));
            yield return M("piano_lounge", Music.LoungePiano(1900), true);
            int[] keys = { 60, 62, 64, 65, 67, 69, 71, 72, 74, 76 };
            foreach (var inst in new[] { "grand", "detuned", "toy", "organ" })
                for (int k = 0; k < keys.Length; k++)
                    yield return M($"note_{inst}_{k}", Music.InstrumentNote(inst, keys[k], 2000 + k));
            yield return M("elev_muzak", Music.Muzak(2100), true);

            // VCR / UI ------------------------------------------------------------------------------
            yield return M("ui_tape", Make(r =>
            {
                var y = Dsp.Buf(1.6);
                Thud(y, 0, r, 110, 0.06f, 0.8f);
                Click(y, Dsp.N(0.1), r, 2000, 0.4f);
                var mot = Hum(1.2, r, 75, 8, 1.0f, 0.4f);
                Env(mot, t => Math.Min(1, t / 0.2f) * Math.Min(1, (1.2f - t) / 0.4f));
                Dsp.Mix(y, mot, Dsp.N(0.3), 0.25f);
                Thud(y, Dsp.N(1.0), r, 140, 0.04f, 0.4f);
                return y;
            }, 2200));
            yield return M("ui_ff", LoopOf(2.5, 0.2, sec =>
            {
                var r = new Rng(2210);
                var y = Hum(sec, r, 330, 6, 1.0f, 0.3f);
                var hiss = Dsp.Noise(sec, r);
                Dsp.Filter(hiss, Biquad.HighPass(3000));
                Dsp.Mix(y, hiss, 0, 0.2f);
                return y;
            }, 0.08f), true);
            yield return M("ui_blip", Make(r =>
            {
                var y = Dsp.Buf(0.12);
                for (int i = 0; i < y.Length; i++) y[i] = Dsp.Sin(TAU * 1600 * i / SR) * Dsp.Exp(-(double)i / SR / 0.04);
                return y;
            }, 2220));
            yield return M("test_beacon", Make(r =>
            {
                // broadband click + pink burst: easy to localise, for the headphone direction test
                var y = Dsp.Buf(0.5);
                Click(y, 0, r, 300, 1f, 0.004f);
                var p = Dsp.PinkNoise(0.15, r);
                Env(p, t => Dsp.Exp(-t / 0.04));
                Dsp.Mix(y, p, 0, 0.6f);
                return y;
            }, 2230));
        }

        static float[] Make(Func<Rng, float[]> f, int seed)
        {
            var x = f(new Rng(seed));
            Dsp.Normalize(x, 0.9f);
            return x;
        }

        static float[] Ring(Rng r, bool pay)
        {
            var y = Dsp.Buf(6.0);
            // the clapper hits two bells ~20 times a second
            Action<double, double> burst = (start, len) =>
            {
                for (double t = start; t < start + len; t += 0.05)
                {
                    Dsp.Modes(y, Dsp.N(t), new[] { pay ? 1650f : 1850f, pay ? 2200f : 2450f, 4100f, 5600f },
                              new[] { 0.3f, 0.25f, 0.1f, 0.06f }, new[] { 0.3f, 0.25f, 0.1f, 0.05f }, r, 0.004f);
                }
            };
            if (pay)
            {
                burst(0, 0.4);
                burst(0.6, 0.4);
                burst(3.0, 0.4);
                burst(3.6, 0.4);
            }
            else burst(0, 2.0);
            return y;
        }
    }
}
