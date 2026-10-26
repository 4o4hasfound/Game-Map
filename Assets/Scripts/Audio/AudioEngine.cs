using System.Collections.Generic;
using System.IO;
using UnityEngine;

// The game's sound system: listener, output mode (headphones binaural / stereo speakers / surround),
// room reverb selection, one-shot pool, a room-tone bed, and a WAV recorder for testing the final mix.
public class AudioEngine : MonoBehaviour
{
    public enum OutputMode { Headphones, Speakers, Surround }

    public static AudioEngine Instance { get; private set; }

    public Transform Listener;
    public OutputMode Mode { get; private set; } = OutputMode.Headphones;
    public float[] SpeakerAngles { get; private set; }
    public RoomInfo ListenerRoom { get; private set; }

    static readonly List<SpatialVoice> voices = new List<SpatialVoice>();
    public static IReadOnlyList<SpatialVoice> Voices => voices;

    readonly Queue<SpatialVoice> pool = new Queue<SpatialVoice>();
    readonly List<(SpatialVoice v, float until)> busy = new List<(SpatialVoice, float)>();

    public static void Register(SpatialVoice v)
    {
        if (!voices.Contains(v)) voices.Add(v);
    }

    public static void Unregister(SpatialVoice v)
    {
        voices.Remove(v);
    }

    void Awake()
    {
        Instance = this;
        if (!Listener) Listener = transform;
        SpeakerAngles = AnglesFor(AudioSettings.speakerMode);
        string m = PlayerPrefs.GetString("audio_mode", "Headphones");
        if (m == "Speakers") Mode = OutputMode.Speakers;
    }

    void Update()
    {
        // the room the listener is in is only used for display; acoustics come from geometry (GeometricAcoustics)
        var world = HotelWorld.Instance;
        if (world != null && Listener)
        {
            var r = world.RoomAt(Listener.position);
            if (r != null) ListenerRoom = r;
        }
        // reverberance of sounds arriving from other spaces: energy-weighted average of the spaces they come from
        float wsum = 0f, rt = 0f, rth = 0f, mf = 0f, lvl = 0f;
        foreach (var v in voices)
        {
            if (!v || v.SourceSpace == null || v.RemoteWeight <= 1e-4f) continue;
            float w = v.RemoteWeight * v.RemoteWeight;
            wsum += w;
            rt += v.SourceSpace.Rt60 * w;
            rth += v.SourceSpace.Rt60High * w;
            mf += v.SourceSpace.MeanFree * w;
            lvl += v.SourceSpace.Level * w;
        }
        if (wsum > 1e-6f && ReverbBus.Instance) ReverbBus.Instance.SetRemote(rt / wsum, rth / wsum, mf / wsum, lvl / wsum);
        for (int i = busy.Count - 1; i >= 0; i--)
        {
            if (Time.time >= busy[i].until || !busy[i].v || (!busy[i].v.IsPlaying && Time.time > busy[i].until - 30f))
            {
                var v = busy[i].v;
                busy.RemoveAt(i);
                if (v)
                {
                    v.Stop();
                    v.gameObject.SetActive(false);
                    pool.Enqueue(v);
                }
            }
        }
        if (Input.GetKeyDown(KeyCode.F3)) CycleMode();
    }

    // ------------------------------------------------------------------ one-shots
    public SpatialVoice PlayAt(string clipName, Vector3 pos, float vol = 1f, float pitch = 1f, float minD = 1f, float maxD = 25f,
                               Transform follow = null)
    {
        return PlayAt(SoundBank.Get(clipName), pos, vol, pitch, minD, maxD, follow);
    }

    public SpatialVoice PlayAt(AudioClip clip, Vector3 pos, float vol = 1f, float pitch = 1f, float minD = 1f, float maxD = 25f,
                               Transform follow = null)
    {
        if (!clip) return null;
        // Inverse-distance propagation cannot become shorter than the direct path.
        // Inaudible distant events need no decoder, voice allocation or geometry work.
        if (Listener && !follow && (Listener.position-pos).sqrMagnitude > maxD*maxD) return null;
        if (busy.Count >= 32) return null;
        SpatialVoice v = pool.Count > 0 ? pool.Dequeue() : CreateVoice();
        v.transform.SetParent(follow, false);
        if (follow) v.transform.localPosition = Vector3.zero;
        else v.transform.position = pos;
        v.gameObject.SetActive(true);
        v.volume = vol;
        v.minDistance = minD;
        v.maxDistance = maxD;
        v.directivity = 0f;
        v.headLocked = false;
        v.occlusion = true; // pooled voices may previously have been a headphone test
        v.sendLevel = 0.65f;
        v.label = clip.name;
        v.Play(clip, false, 0f, pitch);
        busy.Add((v, Time.time + clip.length / Mathf.Max(0.05f, pitch) + 0.2f));
        return v;
    }

    SpatialVoice CreateVoice()
    {
        var go = new GameObject("OneShot");
        go.AddComponent<AudioSource>();
        return go.AddComponent<SpatialVoice>();
    }

    // ------------------------------------------------------------------ output mode
    public void CycleMode()
    {
        var next = Mode == OutputMode.Headphones ? OutputMode.Speakers : Mode == OutputMode.Speakers ? OutputMode.Surround : OutputMode.Headphones;
        // without surround hardware, Speakers goes straight back to headphones (it used to fall back to Speakers forever)
        if (next == OutputMode.Surround && !SurroundAvailable) next = OutputMode.Headphones;
        SetMode(next);
    }

    static bool SurroundAvailable => AudioSettings.driverCapabilities != AudioSpeakerMode.Stereo && AudioSettings.driverCapabilities != AudioSpeakerMode.Mono;

    public void SetMode(OutputMode m)
    {
        var playing = new List<(SpatialVoice v, float t)>();
        foreach (var v in voices)
            if (v && v.IsPlaying && v.Source.loop) playing.Add((v, v.Source.time));
        var cfg = AudioSettings.GetConfiguration();
        var want = m == OutputMode.Surround ? AudioSettings.driverCapabilities : AudioSpeakerMode.Stereo;
        if (m == OutputMode.Surround && (want == AudioSpeakerMode.Stereo || want == AudioSpeakerMode.Mono))
        {
            // no surround speakers on this device
            m = OutputMode.Speakers;
            want = AudioSpeakerMode.Stereo;
        }
        if (cfg.speakerMode != want)
        {
            cfg.speakerMode = want;
            AudioSettings.Reset(cfg);
            foreach (var (v, t) in playing)
                if (v) v.Play(v.Source.clip, true, t, v.Source.pitch);
        }
        Mode = m;
        SpeakerAngles = AnglesFor(AudioSettings.speakerMode);
        PlayerPrefs.SetString("audio_mode", m.ToString());
    }

    static float[] AnglesFor(AudioSpeakerMode m)
    {
        float N = float.NaN;
        switch (m)
        {
            case AudioSpeakerMode.Mode5point1: return new[] { -30f, 30f, 0f, N, -110f, 110f };
            case AudioSpeakerMode.Mode7point1: return new[] { -30f, 30f, 0f, N, -90f, 90f, -150f, 150f };
            case AudioSpeakerMode.Quad: return new[] { -45f, 45f, -135f, 135f };
            default: return new[] { -30f, 30f };
        }
    }

    public string ModeLabel => Mode == OutputMode.Headphones ? "HEADPHONES (BINAURAL)" :
        Mode == OutputMode.Speakers ? "STEREO SPEAKERS" : "SURROUND " + AudioSettings.speakerMode.ToString().Replace("Mode", "").Replace("point", ".");

    // ------------------------------------------------------------------ mix recorder (testing)
    static readonly object recLock = new object();
    static List<float> rec;
    static int recChannels;

    public static void StartRecording()
    {
        lock (recLock) { rec = new List<float>(48000 * 2 * 30); }
    }

    public static void CaptureMix(float[] data, int channels)
    {
        if (rec == null) return;
        lock (recLock)
        {
            if (rec == null) return;
            recChannels = channels;
            rec.AddRange(data);
        }
    }

    public static void StopRecording(string path)
    {
        List<float> r;
        int ch;
        lock (recLock)
        {
            r = rec;
            ch = recChannels;
            rec = null;
        }
        if (r == null || ch == 0) return;
        using (var w = new BinaryWriter(File.Create(path)))
        {
            int sr = AudioSettings.outputSampleRate;
            int bytes = r.Count * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + bytes);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);
            w.Write((short)1);
            w.Write((short)ch);
            w.Write(sr);
            w.Write(sr * ch * 2);
            w.Write((short)(ch * 2));
            w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            w.Write(bytes);
            foreach (float s in r) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
        }
    }
}
