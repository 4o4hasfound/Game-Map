using System;
using System.Collections.Generic;

// Plain C# synthesis toolkit used to bake every sound in the game (no Unity dependency, so it can also be
// run and checked outside the editor).
namespace HotelSynth
{
    public sealed class Rng
    {
        uint s;

        public Rng(int seed)
        {
            s = (uint)seed * 2654435761u + 0x9E3779B9u;
            if (s == 0) s = 1;
        }

        public float Next()
        {
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            return (s & 0xFFFFFF) / 16777216f;
        }

        public float Sym() => Next() * 2f - 1f;
        public float Range(float a, float b) => a + (b - a) * Next();
        public int Int(int a, int b) => a + (int)(Next() * (b - a));
        public bool Chance(float p) => Next() < p;

        public float Gauss()
        {
            float u = Math.Max(Next(), 1e-7f), v = Next();
            return (float)(Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v));
        }

        public T Pick<T>(IList<T> l) => l[Int(0, l.Count)];
    }

    public struct Biquad
    {
        float b0, b1, b2, a1, a2, z1, z2;

        static Biquad Make(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            return new Biquad { b0 = (float)(b0 / a0), b1 = (float)(b1 / a0), b2 = (float)(b2 / a0), a1 = (float)(a1 / a0), a2 = (float)(a2 / a0) };
        }

        static double W(double f) => 2 * Math.PI * Math.Min(f, Dsp.SR * 0.45) / Dsp.SR;

        public static Biquad LowPass(double f, double q = 0.707)
        {
            double w = W(f), c = Math.Cos(w), a = Math.Sin(w) / (2 * q);
            return Make((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + a, -2 * c, 1 - a);
        }

        public static Biquad HighPass(double f, double q = 0.707)
        {
            double w = W(f), c = Math.Cos(w), a = Math.Sin(w) / (2 * q);
            return Make((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + a, -2 * c, 1 - a);
        }

        public static Biquad BandPass(double f, double q = 1)
        {
            double w = W(f), c = Math.Cos(w), a = Math.Sin(w) / (2 * q);
            return Make(a, 0, -a, 1 + a, -2 * c, 1 - a);
        }

        public static Biquad Peak(double f, double q, double db)
        {
            double A = Math.Pow(10, db / 40), w = W(f), c = Math.Cos(w), a = Math.Sin(w) / (2 * q);
            return Make(1 + a * A, -2 * c, 1 - a * A, 1 + a / A, -2 * c, 1 - a / A);
        }

        public static Biquad LowShelf(double f, double db)
        {
            double A = Math.Pow(10, db / 40), w = W(f), c = Math.Cos(w), s = Math.Sin(w), a = s / 2 * Math.Sqrt(2), sq = 2 * Math.Sqrt(A) * a;
            return Make(A * ((A + 1) - (A - 1) * c + sq), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - sq),
                (A + 1) + (A - 1) * c + sq, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - sq);
        }

        public static Biquad HighShelf(double f, double db)
        {
            double A = Math.Pow(10, db / 40), w = W(f), c = Math.Cos(w), s = Math.Sin(w), a = s / 2 * Math.Sqrt(2), sq = 2 * Math.Sqrt(A) * a;
            return Make(A * ((A + 1) + (A - 1) * c + sq), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - sq),
                (A + 1) - (A - 1) * c + sq, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - sq);
        }

        public static Biquad WithState(Biquad coeffs, Biquad state)
        {
            coeffs.z1 = state.z1;
            coeffs.z2 = state.z2;
            return coeffs;
        }

        public float Process(float x)
        {
            float y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            if (float.IsNaN(y) || Math.Abs(y) > 1e6f) { z1 = z2 = 0; return 0; }
            return y;
        }
    }

    public struct Pink
    {
        float b0, b1, b2, b3, b4, b5, b6;

        public float Next(Rng r)
        {
            float w = r.Sym();
            b0 = 0.99886f * b0 + w * 0.0555179f;
            b1 = 0.99332f * b1 + w * 0.0750759f;
            b2 = 0.96900f * b2 + w * 0.1538520f;
            b3 = 0.86650f * b3 + w * 0.3104856f;
            b4 = 0.55000f * b4 + w * 0.5329522f;
            b5 = -0.7616f * b5 - w * 0.0168980f;
            float p = b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362f;
            b6 = w * 0.115926f;
            return p * 0.11f;
        }
    }

    public struct Brown
    {
        float z;

        public float Next(Rng r)
        {
            z = (z + 0.02f * r.Sym()) / 1.02f;
            return z * 3.5f;
        }
    }

    public static class Dsp
    {
        public const int SR = 44100;
        public const float TAU = (float)(Math.PI * 2);

        public static float[] Buf(double seconds) => new float[Math.Max(1, (int)(seconds * SR))];
        public static int N(double seconds) => (int)(seconds * SR);

        public static float Sin(double x) => (float)Math.Sin(x);
        public static float Exp(double x) => (float)Math.Exp(x);
        public static float Clamp(float x, float a, float b) => x < a ? a : x > b ? b : x;
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float MidiHz(double m) => (float)(440 * Math.Pow(2, (m - 69) / 12.0));
        public static float Db(float db) => (float)Math.Pow(10, db / 20);

        public static void Filter(float[] x, Biquad b)
        {
            for (int i = 0; i < x.Length; i++) x[i] = b.Process(x[i]);
        }

        public static void Filter(float[] x, params Biquad[] bs)
        {
            for (int k = 0; k < bs.Length; k++)
            {
                var b = bs[k];
                for (int i = 0; i < x.Length; i++) x[i] = b.Process(x[i]);
            }
        }

        public static void Gain(float[] x, float g)
        {
            for (int i = 0; i < x.Length; i++) x[i] *= g;
        }

        public static float Peak(float[] x)
        {
            float p = 0;
            foreach (var v in x) p = Math.Max(p, Math.Abs(v));
            return p;
        }

        public static float Rms(float[] x)
        {
            double s = 0;
            foreach (var v in x) s += v * v;
            return (float)Math.Sqrt(s / Math.Max(1, x.Length));
        }

        public static void Normalize(float[] x, float peak = 0.9f)
        {
            float p = Peak(x);
            if (p > 1e-9f) Gain(x, peak / p);
        }

        public static void NormalizeRms(float[] x, float rms, float maxPeak = 0.98f)
        {
            float r = Rms(x);
            if (r < 1e-9f) return;
            Gain(x, rms / r);
            float p = Peak(x);
            if (p > maxPeak) Gain(x, maxPeak / p);
        }

        public static void Fade(float[] x, double inSec, double outSec)
        {
            int a = N(inSec), b = N(outSec);
            for (int i = 0; i < a && i < x.Length; i++) x[i] *= (float)i / a;
            for (int i = 0; i < b && i < x.Length; i++) x[x.Length - 1 - i] *= (float)i / b;
        }

        public static void Mix(float[] dst, float[] src, int offset, float g = 1f)
        {
            for (int i = 0; i < src.Length; i++)
            {
                int j = offset + i;
                if (j >= 0 && j < dst.Length) dst[j] += src[i] * g;
            }
        }

        // Mix wrapping around the end: material that runs past the end of a loop continues at its start.
        public static void MixWrap(float[] dst, float[] src, int offset, float g = 1f)
        {
            int n = dst.Length;
            for (int i = 0; i < src.Length; i++)
            {
                int j = ((offset + i) % n + n) % n;
                dst[j] += src[i] * g;
            }
        }

        // Turn a rendered buffer into a seamless loop: the last `xf` seconds are crossfaded into the start.
        public static float[] Loop(float[] x, double xf)
        {
            int c = Math.Min(N(xf), x.Length / 3);
            int len = x.Length - c;
            var y = new float[len];
            Array.Copy(x, y, len);
            for (int i = 0; i < c; i++)
            {
                float t = (float)i / c;
                float a = (float)Math.Sqrt(t), b = (float)Math.Sqrt(1 - t);
                y[i] = x[i] * a + x[len + i] * b;
            }
            return y;
        }

        public static float SoftClip(float x, float drive = 1f)
        {
            return (float)Math.Tanh(x * drive) / (float)Math.Tanh(drive);
        }

        public static void Saturate(float[] x, float drive)
        {
            for (int i = 0; i < x.Length; i++) x[i] = SoftClip(x[i], drive);
        }

        public static void Reverse(float[] x) => Array.Reverse(x);

        public static float[] Concat(params float[][] parts)
        {
            int n = 0;
            foreach (var p in parts) n += p.Length;
            var y = new float[n];
            int o = 0;
            foreach (var p in parts)
            {
                Array.Copy(p, 0, y, o, p.Length);
                o += p.Length;
            }
            return y;
        }

        // ---------------------------------------------------------------- generators
        public static float[] Noise(double sec, Rng r, float g = 1f)
        {
            var x = Buf(sec);
            for (int i = 0; i < x.Length; i++) x[i] = r.Sym() * g;
            return x;
        }

        public static float[] PinkNoise(double sec, Rng r, float g = 1f)
        {
            var x = Buf(sec);
            var p = new Pink();
            for (int i = 0; i < x.Length; i++) x[i] = p.Next(r) * g;
            return x;
        }

        public static float[] BrownNoise(double sec, Rng r, float g = 1f)
        {
            var x = Buf(sec);
            var p = new Brown();
            for (int i = 0; i < x.Length; i++) x[i] = p.Next(r) * g;
            return x;
        }

        // damped sinusoid modes: the basis of impacts, bells, knocks and footsteps
        public static void Modes(float[] dst, int offset, float[] freqs, float[] decays, float[] amps, Rng r = null, float detune = 0f)
        {
            for (int m = 0; m < freqs.Length; m++)
            {
                float f = freqs[m] * (1 + (r != null ? r.Sym() * detune : 0));
                if (f >= SR * 0.45f) continue;
                double w = TAU * f / SR;
                float dec = decays[m], a = amps[m];
                int len = Math.Min(dst.Length - offset, N(dec * 7));
                float ph = r != null ? r.Next() * TAU : 0f;
                for (int i = 0; i < len; i++)
                {
                    float t = (float)i / SR;
                    dst[offset + i] += a * (float)Math.Sin(w * i + ph) * Exp(-t / dec);
                }
            }
        }

        public static float[] Burst(double sec, Rng r, double decay, Biquad filt)
        {
            var x = Noise(sec, r);
            for (int i = 0; i < x.Length; i++) x[i] *= Exp(-(double)i / SR / decay);
            Filter(x, filt);
            return x;
        }

        // Minnaert bubble: rising-pitch damped sine, the sound of drops and running water
        public static void Bubble(float[] dst, int offset, float f0, float amp, float decay, float rise)
        {
            double ph = 0;
            int len = Math.Min(dst.Length - offset, N(decay * 6));
            for (int i = 0; i < len; i++)
            {
                float t = (float)i / SR;
                float f = f0 * (1 + rise * t / decay);
                ph += TAU * f / SR;
                dst[offset + i] += amp * (float)Math.Sin(ph) * Exp(-t / decay);
            }
        }

        public static float[] KarplusStrong(float f, double sec, float damp, Rng r, float bright = 0.5f)
        {
            int p = Math.Max(2, (int)(SR / f));
            var ring = new float[p];
            for (int i = 0; i < p; i++) ring[i] = r.Sym();
            var lp = Biquad.LowPass(2000 + bright * 8000);
            for (int i = 0; i < p; i++) ring[i] = lp.Process(ring[i]);
            var y = Buf(sec);
            int idx = 0;
            float prev = 0;
            for (int i = 0; i < y.Length; i++)
            {
                float v = ring[idx];
                float nv = damp * 0.5f * (v + prev);
                prev = v;
                ring[idx] = nv;
                y[i] = v;
                idx = (idx + 1) % p;
            }
            return y;
        }

        // ---------------------------------------------------------------- simple baked reverb (used for music only)
        public static float[] Room(float[] x, float rt, float mix, float damp = 0.4f, double tail = 1.5)
        {
            int[] combs = { 1557, 1617, 1491, 1422, 1277, 1356 };
            int[] aps = { 225, 556, 441 };
            var y = new float[x.Length + N(tail)];
            var outp = new float[y.Length];
            for (int c = 0; c < combs.Length; c++)
            {
                int L = combs[c];
                var buf = new float[L];
                int pos = 0;
                float fb = (float)Math.Pow(10, -3.0 * L / (rt * SR));
                float lp = 0;
                for (int i = 0; i < y.Length; i++)
                {
                    float inp = i < x.Length ? x[i] : 0f;
                    float v = buf[pos];
                    lp = v + (lp - v) * damp;
                    buf[pos] = inp + lp * fb;
                    pos = (pos + 1) % L;
                    outp[i] += v / combs.Length;
                }
            }
            foreach (int L in aps)
            {
                var buf = new float[L];
                int pos = 0;
                for (int i = 0; i < outp.Length; i++)
                {
                    float d = buf[pos];
                    float v = -0.5f * outp[i] + d;
                    buf[pos] = outp[i] + 0.5f * v;
                    outp[i] = v;
                    pos = (pos + 1) % L;
                }
            }
            for (int i = 0; i < y.Length; i++) y[i] = (i < x.Length ? x[i] : 0f) * (1 - mix) + outp[i] * mix * 1.4f;
            return y;
        }

        // reverb for a looping piece: the tail that runs past the end is folded back onto the start
        public static float[] RoomWrap(float[] x, float rt, float mix, float damp = 0.4f)
        {
            var y = Room(x, rt, mix, damp, Math.Min(rt * 1.5, 4.0));
            var o = new float[x.Length];
            for (int i = 0; i < y.Length; i++) o[i % o.Length] += y[i];
            return o;
        }

        // ---------------------------------------------------------------- playback media
        // pitch wow/flutter by variable-rate resampling (tape, worn records)
        public static float[] WowFlutter(float[] x, float wowDepth, float wowRate, float flutterDepth, float flutterRate, Rng r)
        {
            var y = new float[x.Length];
            double pos = 0;
            double ph1 = r.Next() * TAU, ph2 = r.Next() * TAU;
            for (int i = 0; i < y.Length; i++)
            {
                double rate = 1 + wowDepth * Math.Sin(ph1) + flutterDepth * Math.Sin(ph2);
                ph1 += TAU * wowRate / SR;
                ph2 += TAU * flutterRate / SR;
                int i0 = (int)pos;
                if (i0 + 1 >= x.Length) break;
                float fr = (float)(pos - i0);
                y[i] = x[i0] + (x[i0 + 1] - x[i0]) * fr;
                pos += rate;
            }
            return y;
        }

        public static void VinylCrackle(float[] x, Rng r, float density, float level, float hiss)
        {
            var hp = Biquad.HighPass(1500);
            var lp = Biquad.LowPass(7000);
            for (int i = 0; i < x.Length; i++)
            {
                float c = 0;
                if (r.Next() < density / SR) c = r.Sym() * level * (r.Chance(0.1f) ? 3f : 1f);
                float h = r.Sym() * hiss;
                x[i] += lp.Process(hp.Process(c + h));
            }
            // rumble + once-per-revolution tick (78 rpm -> 1.3 Hz)
            for (int i = 0; i < x.Length; i++)
            {
                double t = (double)i / SR;
                x[i] += level * 0.08f * (float)Math.Sin(TAU * 13 * t);
                if (i % (int)(SR / 1.3) == 0) for (int k = 0; k < 40 && i + k < x.Length; k++) x[i + k] += level * 0.6f * Exp(-k / 6.0) * (k % 2 == 0 ? 1 : -1);
            }
        }

        // band-limited speaker / medium colouration
        public static void Speaker(float[] x, float lo, float hi, float drive, float resonance = 0f, float resF = 1200f)
        {
            Filter(x, Biquad.HighPass(lo, 0.8), Biquad.LowPass(hi, 0.8));
            if (resonance != 0) Filter(x, Biquad.Peak(resF, 1.2, resonance));
            if (drive > 0) Saturate(x, drive);
        }
    }
}
