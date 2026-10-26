using System;

namespace HotelSynth
{
    // Speech-like babble (unintelligible on purpose): glottal source -> three moving formant resonators,
    // fricative / plosive / nasal consonants, syllable rhythm, phrase intonation that falls (or rises for questions),
    // breaths and pauses. Used for radio talk, the news reader, TV shows and the voices on the phone.
    public static class Speech
    {
        const int SR = Dsp.SR;

        public class Voice
        {
            public float f0 = 115f;          // speaking pitch
            public float range = 0.25f;      // intonation range (fraction of f0)
            public float rate = 4.6f;        // syllables per second
            public float formant = 1f;       // vocal tract scale (1 male, 1.15 female)
            public float breathy = 0.04f;
            public float pauses = 1f;
            public bool monotone;
        }

        public static Voice Male => new Voice();
        public static Voice Female => new Voice { f0 = 205f, range = 0.3f, rate = 5.0f, formant = 1.16f, breathy = 0.06f };
        public static Voice Announcer => new Voice { f0 = 98f, range = 0.18f, rate = 4.2f, formant = 0.97f };
        public static Voice Robot => new Voice { f0 = 140f, range = 0f, rate = 2.2f, formant = 1.05f, monotone = true, pauses = 2f };

        public static float[] Babble(double seconds, Voice v, Rng r)
        {
            var y = Dsp.Buf(seconds);
            int n = y.Length;
            // control tracks
            var f0 = new float[n];
            var amp = new float[n];
            var f1 = new float[n];
            var f2 = new float[n];
            var f3 = new float[n];
            var noise = new float[n];
            var noiseF = new float[n];
            int i = Dsp.N(0.15);
            float lastF1 = 500, lastF2 = 1500, lastF3 = 2500;
            while (i < n)
            {
                int syl = r.Int(4, 13);
                bool question = r.Chance(0.18f);
                for (int s = 0; s < syl && i < n; s++)
                {
                    float phrasePos = (float)s / syl;
                    // consonant
                    int ctype = r.Int(0, 6);
                    int clen = ctype switch { 0 => Dsp.N(r.Range(0.05f, 0.1f)), 1 => Dsp.N(0.04f), 2 => Dsp.N(0.05f), _ => Dsp.N(r.Range(0.02f, 0.05f)) };
                    for (int k = 0; k < clen && i + k < n; k++)
                    {
                        float tk = (float)k / clen;
                        if (ctype == 0) { noise[i + k] = 0.35f * Dsp.Sin(Math.PI * tk); noiseF[i + k] = r.Chance(0.5f) ? 5500 : 2600; }
                        else if (ctype == 1) { if (k > clen * 0.7f) { noise[i + k] = 0.6f * (1 - tk); noiseF[i + k] = 1800; } }
                        else if (ctype == 2) { amp[i + k] = 0.35f; f0[i + k] = v.f0; f1[i + k] = 280; f2[i + k] = 1200; f3[i + k] = 2400; }
                        else { noise[i + k] = 0.08f; noiseF[i + k] = 3500; }
                    }
                    i += clen;
                    // vowel
                    var vow = Inst.Vowels[r.Int(0, 6)];
                    int vlen = Dsp.N(r.Range(0.08f, 0.2f) * 4.6f / v.rate);
                    bool accent = r.Chance(0.25f);
                    float baseF = v.monotone ? v.f0 : v.f0 * (1 + v.range * (0.35f - 0.55f * phrasePos)) * (accent ? 1 + v.range * 0.6f : 1);
                    if (question && s == syl - 1) baseF *= 1 + v.range;
                    for (int k = 0; k < vlen && i + k < n; k++)
                    {
                        float tk = (float)k / vlen;
                        float glide = Math.Min(1f, tk * 4);
                        f1[i + k] = Dsp.Lerp(lastF1, vow[0] * v.formant, glide);
                        f2[i + k] = Dsp.Lerp(lastF2, vow[1] * v.formant, glide);
                        f3[i + k] = Dsp.Lerp(lastF3, vow[2] * v.formant, glide);
                        float drift = question && s == syl - 1 ? 1 + 0.2f * tk : 1 - 0.06f * tk;
                        f0[i + k] = baseF * drift;
                        amp[i + k] = Math.Min(1f, tk / 0.15f) * Math.Min(1f, (1 - tk) / 0.25f) * (accent ? 1.1f : 0.85f);
                    }
                    lastF1 = vow[0] * v.formant;
                    lastF2 = vow[1] * v.formant;
                    lastF3 = vow[2] * v.formant;
                    i += vlen;
                    if (r.Chance(0.15f)) i += Dsp.N(r.Range(0.03f, 0.08f));   // word gap
                }
                // phrase pause, sometimes with a breath
                int pause = Dsp.N(r.Range(0.25f, 0.8f) * v.pauses);
                if (r.Chance(0.4f))
                    for (int k = 0; k < pause / 2 && i + k < n; k++) { noise[i + k] = 0.05f * Dsp.Sin(Math.PI * k / (pause / 2.0)); noiseF[i + k] = 1400; }
                i += pause;
            }
            // render
            double ph = 0;
            var b1 = new Biquad();
            var b2 = new Biquad();
            var b3 = new Biquad();
            var bn = new Biquad();
            for (int k = 0; k < n; k++)
            {
                if (k % 64 == 0)
                {
                    float fa = Math.Max(f1[k], 200), fb = Math.Max(f2[k], 600), fc = Math.Max(f3[k], 1800);
                    b1 = Copy(b1, Biquad.BandPass(fa, fa / 90));
                    b2 = Copy(b2, Biquad.BandPass(fb, fb / 120));
                    b3 = Copy(b3, Biquad.BandPass(fc, fc / 170));
                    bn = Copy(bn, Biquad.BandPass(Math.Max(noiseF[k], 500), 1.5));
                }
                float f = f0[k] > 0 ? f0[k] : v.f0;
                ph += f * (1 + 0.004f * r.Sym()) / SR;
                ph -= Math.Floor(ph);
                float g = ph < 0.6 ? Dsp.Sin(Math.PI * ph / 0.6) : 0f;
                float src = (g - 0.38f) * amp[k] + v.breathy * r.Sym() * amp[k];
                float voiced = b1.Process(src) + 0.6f * b2.Process(src) + 0.25f * b3.Process(src);
                float fric = bn.Process(r.Sym()) * noise[k];
                y[k] = voiced + fric * 0.6f;
            }
            Dsp.Filter(y, Biquad.HighPass(90));
            Dsp.Normalize(y, 0.8f);
            return y;
        }

        // keep the filter state when the coefficients change (smooth formant motion)
        static Biquad Copy(Biquad old, Biquad coeffs) => Biquad.WithState(coeffs, old);
    }
}
