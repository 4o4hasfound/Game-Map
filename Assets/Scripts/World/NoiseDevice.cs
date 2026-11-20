using System.Collections.Generic;
using UnityEngine;

// Something that makes a noise for a while: a radio, a TV, the jukebox, a piano, the chapel organ, a music box,
// a record player, washing machines, the generator, the vending machine (one thump). Switch it on and the
// listener comes to it, stands by it listening, and smashes it: a lure, used once.
// Loud machines also MASK: noises made near a running washer, the generator or the roaring boiler are covered.
// Some run on their own (the boiler roars on and off; the ice machine hums).
public class NoiseDevice : Interactable
{
    public static readonly List<NoiseDevice> All = new List<NoiseDevice>();
    public string label = "radio";
    public string[] clips = { "radio_station_0" };
    public string startClip = "radio_click";
    public float db = 62f;                 // at 1 m, what the listener hears
    public float volume = 0.5f;            // what you hear
    public float duration = 40f;
    public float attendTime = 2.2f;        // how long it stands over it, listening, before it smashes it
    public float mask, maskRadius = 6f;    // dB of cover within the radius while running
    public bool oneShot;                   // vending machine: one loud thump per use
    public int uses = 1;
    public bool auto;                      // runs by itself (boiler, ice machine)
    public Vector2 autoCycle = new Vector2(30f, 25f);   // off, on
    public float startDelay;               // the portable radio switches itself on a few seconds after you set it down
    [TextArea] public string description = "";
    public Lamp glow;                      // a TV's flicker on the walls, the jukebox's lights: only while it plays

    public bool Playing { get; private set; }
    public bool Broken { get; private set; }
    public float PlayingSince { get; private set; }
    SpatialVoice voice;
    float until, nextPulse, autoNext, startAt = -1f;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public override string Prompt => auto ? "" : Broken ? $"{Cap(label)} (smashed)" : Playing ? $"Switch off the {label}" : uses <= 0 ? $"{Cap(label)} (empty)" : oneShot ? $"Use the {label} (loud)" : $"Switch on the {label}";
    public override bool Usable => !auto && !Broken && (Playing || uses > 0);
    static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    void Start()
    {
        var go = new GameObject("Voice");
        go.transform.SetParent(transform, false);
        go.AddComponent<AudioSource>();
        voice = go.AddComponent<SpatialVoice>();
        voice.minDistance = 1f;
        voice.maxDistance = Mathf.Clamp(db * 0.5f, 18f, 40f);
        voice.volume = 0f;
        voice.sendLevel = 0.6f;
        if (auto) autoNext = Time.time + Random.Range(5f, autoCycle.x);
        if (startDelay > 0f) startAt = Time.time + startDelay;
    }

    public override void Tap()
    {
        InteractAnim.Play(InteractAnim.Kind.Press, transform);
        if (auto || Broken) return;
        if (Playing) { TurnOff(); return; }
        if (uses <= 0) return;
        TurnOn(true);
    }

    public void TurnOn(bool byPlayer)
    {
        if (Broken) return;
        uses--;
        if (AudioEngine.Instance && !string.IsNullOrEmpty(startClip)) AudioEngine.Instance.PlayAt(SoundBank.Pick(startClip), transform.position, 0.45f, 1f, 0.6f, 12f);
        if (oneShot)
        {
            if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick(clips[0]), transform.position, volume, 1f, 1f, 35f);
            NoiseBus.Emit(transform.position + Vector3.up * 0.5f, db, false, "device " + label, this);
            if (Hud.Instance) Hud.Instance.Caption($"[{label}]", transform.position);
            if (Game.Instance) Game.Instance.Log("used " + label);
            return;
        }
        Playing = true;
        if (glow) glow.Set(true);
        PlayingSince = Time.time;
        until = Time.time + duration;
        nextPulse = Time.time + 0.4f;
        var c = SoundBank.Pick(clips[Random.Range(0, clips.Length)]);
        if (c && voice) { voice.volume = volume; voice.Play(c, true, 0f, 1f); }
        if (Game.Instance && byPlayer) Game.Instance.Log("switched on " + label);
    }

    public void TurnOff()
    {
        Playing = false;
        if (glow) glow.Set(false);
        if (voice) voice.Stop();
        if (AudioEngine.Instance && !string.IsNullOrEmpty(startClip)) AudioEngine.Instance.PlayAt(SoundBank.Pick(startClip), transform.position, 0.35f, 0.9f, 0.6f, 12f);
    }

    // the listener smashes it
    public void Break()
    {
        Playing = false;
        Broken = true;
        if (glow) glow.Set(false);
        if (voice) voice.Stop();
        var ae = AudioEngine.Instance;
        if (ae)
        {
            ae.PlayAt(SoundBank.Pick("glass_break"), transform.position + Vector3.up * 0.4f, 0.8f, 0.8f, 1f, 35f);
            ae.PlayAt(SoundBank.Pick("amb_debris"), transform.position, 0.6f, 0.7f, 1f, 30f);
            ae.PlayAt(SoundBank.Pick("power_down"), transform.position, 0.4f, 1f, 1f, 20f);
        }
        NoiseBus.Emit(transform.position, 70f, false, "it smash " + label, this);
        if (Hud.Instance) Hud.Instance.Caption($"[{label} smashed]", transform.position);
        if (Game.Instance) Game.Instance.Log("it smashed " + label);
        foreach (var r in GetComponentsInChildren<Renderer>()) r.transform.localRotation *= Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(-25f, 25f), Random.Range(-12f, 12f));
    }

    void Update()
    {
        if (startAt > 0f && Time.time >= startAt) { startAt = -1f; TurnOn(true); }
        if (auto)
        {
            if (Time.time >= autoNext)
            {
                if (Playing) { Playing = false; if (voice) voice.Stop(); autoNext = Time.time + autoCycle.x; }
                else
                {
                    Playing = true; PlayingSince = Time.time; autoNext = Time.time + autoCycle.y;
                    var c = SoundBank.Get(clips[0]);
                    if (c && voice) { voice.volume = volume; voice.Play(c, true, Random.value * c.length, 1f); }
                }
            }
            return;
        }
        if (!Playing) return;
        if (Time.time >= nextPulse)
        {
            nextPulse = Time.time + 1.2f;
            NoiseBus.Emit(transform.position + Vector3.up * 0.5f, db, false, "device " + label, this);
            if (Hud.Instance && Random.value < 0.3f) Hud.Instance.Caption($"[{label} playing]", transform.position);
        }
        if (Time.time >= until) TurnOff();
    }

    // how much running machines cover a noise made at p (dB)
    public static float MaskAt(Vector3 p, NoiseDevice except = null)
    {
        float m = 0f;
        foreach (var d in All)
        {
            if (!d || !d.Playing || d.mask <= 0f || d == except) continue;
            float dist = Vector3.Distance(d.transform.position, p);
            if (dist > d.maskRadius || Mathf.Abs(d.transform.position.y - p.y) > 3f) continue;
            m = Mathf.Max(m, d.mask * (1f - dist / d.maskRadius * 0.6f));
        }
        return m;
    }
}
