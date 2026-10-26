using UnityEngine;

// One spatialised sound source with its own binaural renderer (all DSP is done here, Unity's panner is bypassed).
//
// Per sample, on the audio thread:
//   mono input -> distance/occlusion low-pass (2 poles) -> gain -> delay line
//   per ear: interaural time delay (spherical head, Woodworth/Brown-Duda) + pinna reflections
//            (Brown-Duda 5-tap model: front/back + elevation colouration) -> head-shadow shelf
//            (Brown-Duda one-pole/one-zero filter, ear-angle dependent)
//   distance-attenuated send to the listener and source spaces (ReverbBus)
// In speaker modes the ears are replaced by an equal-power panner (stereo) or pair-wise VBAP (5.1 / 7.1).
//
// The main thread (Update) works out where the sound appears to come from (direct, around corners through
// door portals, or through walls) with AcousticWorld and hands smoothed targets to the audio thread.
[RequireComponent(typeof(AudioSource))]
public class SpatialVoice : MonoBehaviour
{
    [Header("Source")]
    public float volume = 1f;
    public float minDistance = 1f;       // full level inside this radius
    public float maxDistance = 30f;      // fades to silence here
    public float rolloff = 1f;           // 1 = inverse distance law (-6 dB per doubling)
    [Range(0, 1)] public float directivity;   // 0 omni, 1 strongly forward (speakers, TV)
    public float sendLevel = 1f;         // amount fed to the room reverb
    public bool occlusion = true;
    public bool headLocked;              // e.g. phone handset at the ear: no propagation
    public string label;
    // Unity only mixes a limited number of real voices and, because this class applies all attenuation in its own
    // DSP, Unity sees every source at full volume. Priorities therefore tell it what matters: events and things the
    // player operates first, background loops (placed ambience) last. Inaudible loops are paused outright.
    public bool ambient;
    // diagnostics (-stress A/B): restore the earlier parking behaviour to measure its cost
    public static bool DiagPauseParking, DiagNoInaudibleParking;
    public bool UnityVirtual => Source && Source.isVirtual;
    float quietSince = -1f, nextWake;

    public AudioSource Source { get; private set; }
    public float LastGainDb { get; private set; } = -120f;
    public float LastAzimuth { get; private set; }
    public float LastDistance { get; private set; }
    public string LastPath { get; private set; } = "";
    public bool Audible => LastGainDb > -65f;

    // ---- targets written by the main thread
    volatile float tGain, tItdL, tItdR, tAlphaL, tAlphaR, tCut, tSend, tPinnaCos, tPinnaElev, tPanL, tPanR;
    readonly float[] tSpk = new float[8];
    volatile bool active;
    volatile int mode;

    // ---- audio-thread state
    const int LineSize = 4096;
    readonly float[] line = new float[LineSize];
    int wpos;
    float gain, itdL, itdR, alphaL, alphaR, cut = 20000f, send, pcos, pelev, panL, panR;
    readonly float[] spk = new float[8];
    float lp1, lp2;
    float shL_x1, shL_y1, shR_x1, shR_y1;
    float sr = 48000f;
    readonly float[] sendBuf = new float[8192];
    static readonly float[] PinnaRho = { 0.5f, -1f, 0.5f, -0.25f, 0.25f };
    static readonly float[] PinnaA = { 1f, 5f, 5f, 5f, 5f };
    static readonly float[] PinnaB = { 2f, 4f, 7f, 11f, 13f };
    static readonly float[] PinnaD = { 1f, 0.5f, 0.5f, 0.5f, 0.5f };
    const float PinnaStrength = 0.45f;
    readonly float[] pinnaDelay = new float[5];
    const float HeadRadius = 0.0875f, SpeedOfSound = 343f;

    float nextSolve;
    bool virtualized;
    double virtualAt;
    float virtualTime;
    bool hasPath;
    float sTrans = 1f, sLogCut = 9.9f, sSend = 1f, sLen, sRemote;
    volatile float tRemote;
    // the space around the source itself (only traced while the sound reaches the listener from elsewhere)
    public AcousticSpace SourceSpace { get; private set; }
    public float RemoteWeight { get; private set; }
    float nextSpaceEval;
    float SourceOneShotAttack => 1f - Mathf.Exp(-1f / (0.0015f * sr));
    Vector3 sDir;
    AcousticWorld.Path path;

    void Awake() => Init();

    void Init()
    {
        if (Source) return;
        Source = GetComponent<AudioSource>();
        Source.spatialBlend = 0f;
        Source.playOnAwake = false;
        Source.dopplerLevel = 0f;
        Source.volume = 1f;
        Source.panStereo = 0f;
        Source.priority = 64;
        sr = AudioSettings.outputSampleRate;
        nextSolve = Random.value * 0.15f;
    }

    AudioClip resumeClip;
    float resumeTime, resumeAt, resumePitch = 1f;

    void OnEnable()
    {
        AudioEngine.Register(this);
        // loops keep going "off screen": resume where they would be now
        if (resumeClip && Source)
        {
            var c = resumeClip;
            resumeClip = null;
            Play(c, true, resumeTime + (Time.time - resumeAt) * resumePitch, resumePitch);
        }
    }

    void OnDisable()
    {
        AudioEngine.Unregister(this);
        active = false;
        if (Source && (Source.isPlaying || virtualized) && Source.loop)
        {
            resumeClip = Source.clip;
            resumeTime = virtualized ? virtualTime+(float)(AudioSettings.dspTime-virtualAt)*Source.pitch : Source.time;
            resumeAt = Time.time;
            resumePitch = Source.pitch;
        }
    }

    public void Play(AudioClip clip, bool loop, float startTime = -1f, float pitch = 1f)
    {
        if (!clip) return;
        Init();
        if (!isActiveAndEnabled)
        {
            if (loop) { resumeClip = clip; resumeTime = Mathf.Max(0f, startTime); resumeAt = Time.time; resumePitch = pitch; }
            return;
        }
        hasPath = false;
        virtualized = false;
        Source.clip = clip;
        Source.loop = loop;
        Source.priority = !loop ? 24 : ambient ? 200 : 96;
        Source.pitch = pitch;
        if (startTime >= 0f) Source.time = Mathf.Repeat(startTime, clip.length);
        UpdateSpatial(true);
        Source.Play();
    }

    public void Stop()
    {
        Init();
        Source.Stop();
        virtualized = false;
        active = false;
    }

    public bool IsPlaying => Source && (Source.isPlaying || virtualized);

    void Update()
    {
        var listener = AudioEngine.Instance ? AudioEngine.Instance.Listener : null;
        if (Source && Source.loop && listener)
        {
            bool outOfRange = !headLocked && (listener.position-transform.position).sqrMagnitude > maxDistance*maxDistance;
            // a loop that has stayed below -80 dB (behind several walls) for a second frees its voice
            bool inaudible = !DiagNoInaudibleParking && !headLocked && quietSince >= 0f && Time.time - quietSince > 1f;
            if (!virtualized && Source.isPlaying && (outOfRange || inaudible || volume < 0.00001f))
            {
                // Stop, not Pause: a paused AudioSource keeps its mixer channel, and enough parked loops
                // exhaust the real voices so that new sounds (a piano key, a door) start virtual and silent
                virtualTime=Source.time; virtualAt=AudioSettings.dspTime;
                if (DiagPauseParking) Source.Pause(); else Source.Stop();
                virtualized=true; active=false; RemoteWeight=0f;
            }
            if (virtualized)
            {
                if (outOfRange || volume < 0.00001f) return;
                if (inaudible)
                {
                    // re-check the propagation about once a second; resume once it would be heard (-74 dB)
                    // within the per-frame propagation budget, like every other solve
                    if (Time.time < nextWake || !AcousticWork.PermitSolve()) return;
                    nextWake = Time.time + 0.8f + Random.value * 0.4f;
                    UpdateSpatial(true);
                    active = false;
                    if (LastGainDb < -74f) return;
                    quietSince = -1f;
                }
                float resumeAt = Mathf.Repeat(virtualTime+(float)(AudioSettings.dspTime-virtualAt)*Source.pitch,Source.clip.length);
                virtualized=false; hasPath=false; UpdateSpatial(true);
                if (DiagPauseParking) { Source.time=resumeAt; Source.UnPause(); }
                else { Source.Play(); Source.time=resumeAt; }
            }
        }
        if (!Source || !Source.isPlaying) { active = false; RemoteWeight = 0f; return; }
        UpdateSpatial(false);
        if (Source.loop && LastGainDb < -80f) { if (quietSince < 0f) quietSince = Time.time; }
        else quietSince = -1f;
    }

    void UpdateSpatial(bool immediate)
    {
        var eng = AudioEngine.Instance;
        if (!eng || !eng.Listener)
        {
            active = false;
            return;
        }
        Transform lis = eng.Listener;
        mode = (int)eng.Mode;
        Vector3 src = transform.position;
        Vector3 lp = lis.position;

        float pathLen;
        float trans = 1f, cutoff = 20000f, sendMul = 1f;
        Vector3 apparent = src;
        if (headLocked)
        {
            pathLen = Vector3.Distance(src, lp);
            LastPath = "handset";
        }
        else
        {
            if (immediate || (Time.time >= nextSolve && AcousticWork.PermitSolve()))
            {
                float distance = Vector3.Distance(src, lp);
                nextSolve = Time.time + (distance < 8f ? 0.10f : distance < 22f ? 0.22f : 0.5f);
                path = AcousticWork.Solve(src, lp, occlusion);
            }
            // glide between propagation results so walking through a doorway never makes a sound jump:
            // level and tone move over ~0.35 s, the apparent direction swings round over ~0.25 s
            float k = immediate || !hasPath ? 1f : 1f - Mathf.Exp(-Time.deltaTime / 0.35f);
            float kd = immediate || !hasPath ? 1f : 1f - Mathf.Exp(-Time.deltaTime / 0.25f);
            hasPath = true;
            sTrans = Mathf.Lerp(sTrans, path.gain, k);
            sLogCut = Mathf.Lerp(sLogCut, Mathf.Log(Mathf.Max(path.cutoff, 50f)), k);
            sSend = Mathf.Lerp(sSend, path.send, k);
            sRemote = Mathf.Lerp(sRemote, path.remote, k);
            sLen = Mathf.Lerp(sLen, path.length, k);
            Vector3 dirNow = path.apparent - lp;
            if (dirNow.sqrMagnitude > 1e-6f)
                sDir = sDir.sqrMagnitude < 1e-6f ? dirNow.normalized : Vector3.Slerp(sDir, dirNow.normalized, kd);
            apparent = lp + sDir * Mathf.Max(sLen, 0.05f);
            pathLen = sLen;
            trans = sTrans;
            cutoff = Mathf.Exp(sLogCut);
            sendMul = sSend;
            LastPath = path.kind;
        }

        // distance law with a smooth fade near the maximum distance
        float d = Mathf.Max(pathLen, 0.05f);
        float g = Mathf.Pow(minDistance / Mathf.Max(minDistance, d), rolloff);
        g *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(maxDistance * 0.7f, maxDistance, d));
        // air absorption: high frequencies fall off with distance
        cutoff = Mathf.Min(cutoff, 20000f * Mathf.Exp(-d / 85f) + 900f);

        // source directivity (speaker cones point along the object's forward)
        if (directivity > 0f)
        {
            float c = Vector3.Dot(transform.forward, (lp - src).normalized);
            float dir = Mathf.Lerp(1f, 0.5f + 0.5f * c, directivity);
            g *= Mathf.Lerp(0.25f, 1f, dir);
            cutoff *= Mathf.Lerp(0.18f, 1f, dir);
        }
        g *= volume * trans;

        // head-relative direction
        Vector3 local = lis.InverseTransformPoint(apparent);
        float dist = local.magnitude;
        Vector3 u = dist > 1e-4f ? local / dist : Vector3.forward;
        float az = Mathf.Atan2(u.x, u.z);                 // 0 front, + right
        float el = Mathf.Asin(Mathf.Clamp(u.y, -1f, 1f));
        LastAzimuth = az * Mathf.Rad2Deg;
        LastDistance = pathLen;
        LastGainDb = 20f * Mathf.Log10(Mathf.Max(g, 1e-6f));

        // angles between the source direction and each ear axis
        float thL = Mathf.Acos(Mathf.Clamp(-u.x, -1f, 1f));
        float thR = Mathf.Acos(Mathf.Clamp(u.x, -1f, 1f));
        float near = Mathf.Clamp01(1f - dist / 1.2f);       // proximity: stronger level differences

        tItdL = Itd(thL) * sr;
        tItdR = Itd(thR) * sr;
        tAlphaL = ShadowAlpha(thL, near);
        tAlphaR = ShadowAlpha(thR, near);
        tPinnaCos = Mathf.Cos(Mathf.Abs(az) * 0.5f);
        tPinnaElev = Mathf.Sin(0.5f * (Mathf.PI * 0.5f - el)) ;
        tCut = cutoff;
        tGain = g;
        tSend = g * sendLevel * sendMul; // reflected energy falls with the same propagation/distance as the source
        tRemote = headLocked ? 0f : sRemote;
        // trace the source's own surroundings while it is heard from another space (2 rays per frame, cheap)
        RemoteWeight = 0f;
        if (!headLocked && sRemote > 0.15f && LastGainDb > -60f && Source.isPlaying)
        {
            SourceSpace = AcousticWork.SourceProbe(src);
            RemoteWeight = tSend * sRemote;
        }

        // speaker panning gains
        float pan = Mathf.Clamp(Mathf.Sin(az), -1f, 1f);
        float p = (pan + 1f) * 0.25f * Mathf.PI;
        tPanL = Mathf.Cos(p);
        tPanR = Mathf.Sin(p);
        if (mode == (int)AudioEngine.OutputMode.Surround) Vbap(az, eng.SpeakerAngles, tSpk);

        if (immediate)
        {
            gain = tGain; itdL = tItdL; itdR = tItdR; alphaL = tAlphaL; alphaR = tAlphaR; cut = tCut; send = tSend;
            pcos = tPinnaCos; pelev = tPinnaElev; panL = tPanL; panR = tPanR;
            for (int i = 0; i < 8; i++) spk[i] = tSpk[i];
        }
        active = true;
    }

    // interaural delay for one ear, theta = angle between source and the ear axis (Brown-Duda)
    static float Itd(float theta)
    {
        float t = theta < Mathf.PI * 0.5f ? 1f - Mathf.Cos(theta) : 1f + theta - Mathf.PI * 0.5f;
        return HeadRadius / SpeedOfSound * t;
    }

    // head-shadow alpha: 2 (+6 dB treble) facing the ear, 0.1 (-20 dB) at 150 degrees behind it
    static float ShadowAlpha(float theta, float near)
    {
        const float amin = 0.1f, thmin = 150f * Mathf.Deg2Rad;
        float a = (1f + amin * 0.5f) + (1f - amin * 0.5f) * Mathf.Cos(Mathf.Min(theta / thmin, 1f) * Mathf.PI);
        return Mathf.Lerp(a, a < 1f ? a * 0.6f : Mathf.Min(a * 1.15f, 2.3f), near);
    }

    static void Vbap(float az, float[] angles, float[] gains)
    {
        for (int i = 0; i < 8; i++) gains[i] = 0f;
        if (angles == null) return;
        float a = az * Mathf.Rad2Deg;
        int best0 = -1, best1 = -1;
        float bestSpan = 999f;
        for (int i = 0; i < angles.Length; i++)
        {
            if (float.IsNaN(angles[i])) continue;
            for (int j = 0; j < angles.Length; j++)
            {
                if (i == j || float.IsNaN(angles[j])) continue;
                float span = Mathf.DeltaAngle(angles[i], angles[j]);
                if (span <= 0f || span > 180f) continue;
                float off = Mathf.DeltaAngle(angles[i], a);
                if (off < 0f || off > span) continue;
                if (span < bestSpan) { bestSpan = span; best0 = i; best1 = j; }
            }
        }
        if (best0 < 0) return;
        float t = Mathf.DeltaAngle(angles[best0], a) / bestSpan;
        gains[best0] = Mathf.Cos(t * Mathf.PI * 0.5f);
        gains[best1] = Mathf.Sin(t * Mathf.PI * 0.5f);
    }

    // ------------------------------------------------------------------ audio thread
    void OnAudioFilterRead(float[] data, int channels)
    {
        int frames = data.Length / channels;
        if (!active)
        {
            System.Array.Clear(data, 0, data.Length);
            return;
        }
        if (frames > sendBuf.Length) { System.Array.Clear(data,0,data.Length); return; }
        // per-block smoothing coefficient (~25 ms)
        float k = 1f - Mathf.Exp(-1f / (0.025f * sr));
        float attackK = SourceOneShotAttack;
        float cutT = tCut, gT = tGain, sT = tSend, iL = tItdL, iR = tItdR, aL = tAlphaL, aR = tAlphaR, pc = tPinnaCos, pe = tPinnaElev;
        float plT = tPanL, prT = tPanR;
        int md = mode;
        float K = sr / (SpeedOfSound / HeadRadius);    // bilinear constant for the head-shadow filter
        // anything below about -65 dB is not worth the DSP: skip it entirely
        const float Quiet = 0.0006f;
        bool silent = gain < Quiet && gT < Quiet && send < Quiet && sT < Quiet;
        if (silent)
        {
            System.Array.Clear(data, 0, data.Length);
            return;
        }
        bool pinnaOn = gain > 0.004f || gT > 0.004f;
        long timing = AudioTiming.Begin();
        for (int f = 0; f < frames; f += 32)
        {
            int end = Mathf.Min(frames, f + 32);
            // sub-block parameter update
            float kk = 1f - Mathf.Pow(1f - k, end - f);
            cut += (cutT - cut) * kk;
            alphaL += (aL - alphaL) * kk;
            alphaR += (aR - alphaR) * kk;
            pcos += (pc - pcos) * kk;
            pelev += (pe - pelev) * kk;
            if (pinnaOn && md == (int)AudioEngine.OutputMode.Headphones)
                for(int tap=0;tap<5;tap++)
                    pinnaDelay[tap]=(PinnaA[tap]*pcos*Mathf.Sin(PinnaD[tap]*Mathf.PI*pelev)+PinnaB[tap])*(sr/44100f);
            float c1 = Mathf.Exp(-2f * Mathf.PI * Mathf.Clamp(cut, 60f, 20000f) / sr);
            // head shadow: H(z) = ((1+aK) + (1-aK) z^-1) / ((1+K) + (1-K) z^-1)
            float inv = 1f / (1f + K);
            float bL0 = (1f + alphaL * K) * inv, bL1 = (1f - alphaL * K) * inv;
            float bR0 = (1f + alphaR * K) * inv, bR1 = (1f - alphaR * K) * inv;
            float a1 = (1f - K) * inv;
            for (int n = f; n < end; n++)
            {
                gain += (gT - gain) * (gT > gain ? attackK : k);
                send += (sT - send) * k;
                itdL += (iL - itdL) * k;
                itdR += (iR - itdR) * k;
                panL += (plT - panL) * k;
                panR += (prT - panR) * k;

                int idx = n * channels;
                float x = channels > 1 ? (data[idx] + data[idx + 1]) * 0.5f : data[idx];
                lp1 = x + (lp1 - x) * c1;
                lp2 = lp1 + (lp2 - lp1) * c1;
                float xs = lp2;
                sendBuf[n] = xs * send;
                float xd = xs * gain;
                line[wpos] = xd;

                float yL, yR;
                if (md == (int)AudioEngine.OutputMode.Headphones)
                {
                    // pinna reflections only matter for sounds you can clearly hear
                    yL = pinnaOn ? ReadEar(itdL) : Tap(itdL);
                    yR = pinnaOn ? ReadEar(itdR) : Tap(itdR);
                    // shadow filters
                    float oL = bL0 * yL + bL1 * shL_x1 - a1 * shL_y1;
                    shL_x1 = yL; shL_y1 = oL;
                    float oR = bR0 * yR + bR1 * shR_x1 - a1 * shR_y1;
                    shR_x1 = yR; shR_y1 = oR;
                    yL = oL; yR = oR;
                }
                else
                {
                    yL = xd * panL;
                    yR = xd * panR;
                }
                wpos = (wpos + 1) & (LineSize - 1);

                if (md == (int)AudioEngine.OutputMode.Surround && channels > 2)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        float gsp = c < 8 ? spk[c] : 0f;
                        if (c < 8) spk[c] += (tSpk[c] - spk[c]) * k;
                        data[idx + c] = xd * gsp;
                    }
                }
                else
                {
                    data[idx] = yL;
                    if (channels > 1) data[idx + 1] = yR;
                    for (int c = 2; c < channels; c++) data[idx + c] = 0f;
                }
            }
        }
        ReverbBus.AddSend(sendBuf, frames, tRemote);
        AudioTiming.Voice(timing);
    }

    // direct sound at the ITD plus five pinna reflections (Brown & Duda 1998)
    float ReadEar(float itd)
    {
        float y = Tap(itd);
        float acc = 0f;
        for (int t = 0; t < 5; t++)
        {
            acc += PinnaRho[t] * Tap(itd + pinnaDelay[t]);
        }
        return y + acc * PinnaStrength;
    }

    float Tap(float delay)
    {
        float pos = wpos - delay;
        int i0 = Mathf.FloorToInt(pos);
        float fr = pos - i0;
        float a = line[i0 & (LineSize - 1)];
        float b = line[(i0 + 1) & (LineSize - 1)];
        return a + (b - a) * fr;
    }
}
