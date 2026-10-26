using UnityEngine;

// Room acoustics renderer (sits on the AudioListener, runs after every source has been mixed).
// Two chains:
//   local  - the space around the listener: geometric early reflections (8 taps, delays and directions from the
//            nearest surfaces) + a late tail whose RT60 / damping / level come from GeometricAcoustics.
//   remote - sound that reaches the listener from other spaces (through doors, around corners) keeps the
//            reverberance of the space it was made in; parameters are the energy-weighted average of those spaces.
// Each chain: pre-delay -> 4 allpass diffusers -> 8-line feedback delay network (Householder mixing,
// per-line high-frequency damping, feedback from RT60). Delay-line sets for four size ranges are crossfaded
// when the space size changes a lot, so the tail never clicks or retunes audibly.
public class ReverbBus : MonoBehaviour
{
    public static ReverbBus Instance { get; private set; }
    [Range(0, 2)] public float amount = 0.55f;

    static readonly object Lock = new object();
    static float[] accLocal = new float[8192], accRemote = new float[8192];

    public static void AddSend(float[] buf, int frames, float remoteFraction)
    {
        lock (Lock)
        {
            if (frames > accLocal.Length) frames = accLocal.Length;
            float r = Mathf.Clamp01(remoteFraction), l = 1f - r;
            for (int i = 0; i < frames; i++)
            {
                float x = buf[i];
                accLocal[i] += x * l;
                accRemote[i] += x * r;
            }
        }
    }

    class Allpass
    {
        readonly float[] buf;
        int pos;
        readonly float g;

        public Allpass(int len, float g)
        {
            buf = new float[len];
            this.g = g;
        }

        public float Process(float x)
        {
            float d = buf[pos];
            float y = -g * x + d;
            buf[pos] = x + g * y;
            pos = (pos + 1) % buf.Length;
            return y;
        }
    }

    class Fdn
    {
        const int N = 8;
        readonly float[][] lines = new float[N][];
        readonly int[] len = new int[N], pos = new int[N];
        readonly float[] lp = new float[N], fb = new float[N], o = new float[N];

        public Fdn(float scale, float sr)
        {
            int[] primes = { 1031, 1327, 1523, 1801, 2053, 2311, 2633, 2917 };
            for (int i = 0; i < N; i++)
            {
                len[i] = Mathf.Max(64, Mathf.RoundToInt(primes[i] * scale * sr / 48000f));
                lines[i] = new float[len[i]];
            }
        }

        public void SetDecay(float rt60, float sr)
        {
            for (int i = 0; i < N; i++) fb[i] = Mathf.Pow(10f, -3f * len[i] / (Mathf.Max(rt60, 0.05f) * sr));
        }

        public void Process(float x, float damp, out float l, out float r)
        {
            float sum = 0f;
            for (int i = 0; i < N; i++)
            {
                float v = lines[i][pos[i]];
                lp[i] = v + (lp[i] - v) * damp;
                o[i] = lp[i] * fb[i];
                sum += o[i];
            }
            sum *= 2f / N;
            l = o[0] - o[2] + o[4] - o[6];
            r = o[1] - o[3] + o[5] - o[7];
            for (int i = 0; i < N; i++)
            {
                lines[i][pos[i]] = o[i] - sum + x * ((i & 1) == 0 ? 1f : -1f);
                if (++pos[i] >= len[i]) pos[i] = 0;
            }
        }
    }

    // one reverberant space
    class Chain
    {
        static readonly float[] ClassScale = { 0.35f, 0.6f, 1.0f, 1.55f };
        static readonly float[] edges = { 3f, 7f, 15f };
        readonly Fdn[] fdns;
        readonly Allpass[] diff = new Allpass[4];
        readonly float[] pre = new float[16384];
        int prePos;
        int cls = -1, oldCls = -1;
        float xfade = 1f;
        readonly float sr;
        public float rt = 0.5f, damp = 0.4f, wet = 0.2f, size = 5f, er = 1f;
        public volatile float tRt = 0.5f, tDamp = 0.4f, tWet = 0.2f, tSize = 5f;
        public readonly float[] erDelay = new float[8], erGainL = new float[8], erGainR = new float[8];
        public readonly float[] tErDelay = new float[8], tErGainL = new float[8], tErGainR = new float[8];
        float erLpL, erLpR;
        public bool earlyFromGeometry;

        public Chain(float sr)
        {
            this.sr = sr;
            fdns = new Fdn[ClassScale.Length];
            for (int i = 0; i < fdns.Length; i++) fdns[i] = new Fdn(ClassScale[i], sr);
            int[] ap = { 142, 107, 379, 277 };
            for (int i = 0; i < 4; i++) diff[i] = new Allpass(Mathf.RoundToInt(ap[i] * sr / 44100f), 0.62f);
        }

        int ClassFor(float s, int cur)
        {
            int c = s < 3f ? 0 : s < 7f ? 1 : s < 15f ? 2 : 3;
            if (cur < 0) return c;
            if (c > cur && s > edges[Mathf.Min(cur, 2)] * 1.2f) return c;
            if (c < cur && s < edges[Mathf.Max(cur - 1, 0)] * 0.8f) return c;
            return cur;
        }

        public void Render(float[] input, float[] data, int frames, int channels, float amount)
        {
            float k = 1f - Mathf.Exp(-frames / (0.35f * sr));
            rt += (tRt - rt) * k;
            damp += (tDamp - damp) * k;
            wet += (tWet - wet) * k;
            size += (tSize - size) * k;
            for (int t = 0; t < 8; t++)
            {
                erDelay[t] += (tErDelay[t] - erDelay[t]) * k;
                erGainL[t] += (tErGainL[t] - erGainL[t]) * k;
                erGainR[t] += (tErGainR[t] - erGainR[t]) * k;
            }
            int c = ClassFor(size, cls);
            if (c != cls)
            {
                oldCls = cls;
                cls = c;
                xfade = oldCls < 0 ? 1f : 0f;
            }
            fdns[cls].SetDecay(rt, sr);
            if (oldCls >= 0) fdns[oldCls].SetDecay(rt, sr);
            float dampCoef = Mathf.Lerp(0.05f, 0.8f, damp);
            float preS = Mathf.Clamp(size / 343f * 1000f * 0.5f, 2f, 60f) * 0.001f * sr;
            float g = wet * amount;
            float xk = 1f / (0.3f * sr);
            int L = pre.Length;
            for (int i = 0; i < frames; i++)
            {
                float x = input[i];
                pre[prePos] = x;
                float el = 0f, err = 0f;
                if (earlyFromGeometry)
                {
                    for (int t = 0; t < 8; t++)
                    {
                        int d = (int)(erDelay[t] * sr);
                        float v = pre[(prePos - d + L * 4) % L];
                        el += v * erGainL[t];
                        err += v * erGainR[t];
                    }
                }
                erLpL = el + (erLpL - el) * 0.35f;
                erLpR = err + (erLpR - err) * 0.35f;
                int dp = Mathf.RoundToInt(preS * 1.6f);
                float lx = pre[(prePos - dp + L * 4) % L];
                for (int a = 0; a < 4; a++) lx = diff[a].Process(lx);
                prePos = (prePos + 1) % L;
                fdns[cls].Process(lx * 0.35f, dampCoef, out float tl, out float tr);
                if (oldCls >= 0 && xfade < 1f)
                {
                    fdns[oldCls].Process(0f, dampCoef, out float ol, out float or);
                    tl = Mathf.Lerp(ol, tl, xfade);
                    tr = Mathf.Lerp(or, tr, xfade);
                    xfade = Mathf.Min(1f, xfade + xk);
                }
                float outL = erLpL * amount + tl * 0.5f * g;
                float outR = erLpR * amount + tr * 0.5f * g;
                int idx = i * channels;
                data[idx] += outL;
                if (channels > 1) data[idx + 1] += outR;
                if (channels >= 6)
                {
                    data[idx + 4] += tr * 0.4f * g;
                    data[idx + 5] += tl * 0.4f * g;
                }
            }
        }
    }

    float sr;
    Chain local, remote;
    readonly float[] bufL = new float[8192], bufR = new float[8192];

    public string Describe => local == null ? "" :
        $"RT60 {local.tRt:0.00}s  free path {local.tSize:0.0}m  level {local.tWet:0.00} | remote RT60 {remote.tRt:0.00}s";

    void Awake()
    {
        lock(Lock){System.Array.Clear(accLocal,0,accLocal.Length);System.Array.Clear(accRemote,0,accRemote.Length);}
        Instance = this;
        sr = AudioSettings.outputSampleRate;
        local = new Chain(sr) { earlyFromGeometry = true };
        remote = new Chain(sr);
    }

    public void SetLocal(AcousticSpace s)
    {
        if (local == null) return;
        local.tRt = s.Rt60;
        local.tDamp = Mathf.Clamp01(1f - s.Rt60High / Mathf.Max(s.Rt60, 0.05f) + 0.1f);
        local.tWet = s.Level;
        local.tSize = s.MeanFree;
        for (int t = 0; t < 8; t++)
        {
            local.tErDelay[t] = s.ErDelay[t];
            float pan = s.ErPan[t];
            float g = s.ErGain[t] * 0.55f;
            local.tErGainL[t] = g * Mathf.Sqrt(Mathf.Clamp01(0.5f - 0.5f * pan));
            local.tErGainR[t] = g * Mathf.Sqrt(Mathf.Clamp01(0.5f + 0.5f * pan));
        }
    }

    public void SetRemote(float rt60, float rtHigh, float meanFree, float level)
    {
        if (remote == null) return;
        remote.tRt = rt60;
        remote.tDamp = Mathf.Clamp01(1f - rtHigh / Mathf.Max(rt60, 0.05f) + 0.15f);
        remote.tWet = level;
        remote.tSize = meanFree;
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        long timing = AudioTiming.Begin();
        int frames = data.Length / channels;
        if (frames>bufL.Length) return;
        lock (Lock)
        {
            int n = Mathf.Min(frames, accLocal.Length);
            System.Array.Copy(accLocal, bufL, n);
            System.Array.Copy(accRemote, bufR, n);
            System.Array.Clear(accLocal, 0, n);
            System.Array.Clear(accRemote, 0, n);
            if (n < frames)
            {
                System.Array.Clear(bufL, n, frames - n);
                System.Array.Clear(bufR, n, frames - n);
            }
        }
        if (local == null) return;
        local.Render(bufL, data, frames, channels, amount);
        remote.Render(bufR, data, frames, channels, amount);
        AudioEngine.CaptureMix(data, channels);
        AudioTiming.Block(timing,frames,(int)sr);
    }
}
