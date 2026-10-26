using System.Collections.Generic;
using UnityEngine;

// Behaviour of one lamp. The light, the glow of its fixture and its buzz (AmbientEmitter linked to it) all follow
// Level. Only rare close-up starter clicks accompany fluorescent restrikes; flicker itself is usually silent.
public class LightFlicker : MonoBehaviour
{
    public enum Pattern { Steady, Flicker, Dying, Strobe, Buzz, Candle, Tv, Emergency, Vending, Jukebox, Projector, Off }

    public static readonly List<LightFlicker> All = new List<LightFlicker>();

    public Pattern pattern = Pattern.Steady;
    public string circuit = "";
    public Renderer fixture;
    public bool ticks = true;
    public bool fluorescent = true;
    float filament = 1f;
    public bool shadowsWanted;

    public Light Light { get; private set; }
    public float Level { get; private set; } = 1f;      // 0..1 brightness right now (incl. power)
    public bool Powered { get; private set; } = true;
    public bool Culled;

    float baseIntensity;
    Color baseColor;
    float powerLevel = 1f, powerTarget = 1f, powerDelay;
    float stateTimer, burst, burstUntil, held = 1f;
    float seed;
    float nextTick;
    static float nextGlobalTick;
    bool wasBright = true;
    MaterialPropertyBlock mpb;
    readonly List<(int index, Color emission)> glow = new List<(int, Color)>();

    public static Pattern Parse(string s)
    {
        switch (s)
        {
            case "flicker": return Pattern.Flicker;
            case "dying": return Pattern.Dying;
            case "strobe": return Pattern.Strobe;
            case "buzz": return Pattern.Buzz;
            case "candle": return Pattern.Candle;
            case "tv": return Pattern.Tv;
            case "emergency": return Pattern.Emergency;
            case "vending": return Pattern.Vending;
            case "jukebox": return Pattern.Jukebox;
            case "projector": return Pattern.Projector;
            case "off": return Pattern.Off;
            default: return Pattern.Steady;
        }
    }

    void Awake()
    {
        Light = GetComponent<Light>();
        if (Light)
        {
            baseIntensity = Light.intensity;
            baseColor = Light.color;
        }
        seed = Random.value * 100f;
        stateTimer = Random.Range(0.3f, 8f);
        nextTick = Time.time + Random.Range(10f, 30f);
        mpb = new MaterialPropertyBlock();
        if (fixture)
        {
            var mats = fixture.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] && mats[i].name.StartsWith("Emit_"))
                    glow.Add((i, mats[i].GetColor("_EmissionColor")));
        }
        if (pattern == Pattern.Off) Level = 0f;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public void SetPower(bool on, float delay)
    {
        Powered = on;
        powerTarget = on ? 1f : 0f;
        powerDelay = Time.time + delay;
    }

    void Update()
    {
        float t = Time.time + seed;
        float dt = Time.deltaTime;
        float lvl = 1f;
        stateTimer -= dt;
        switch (pattern)
        {
            case Pattern.Off:
                lvl = 0f;
                break;
            case Pattern.Steady:
                lvl = 0.97f + 0.03f * Mathf.PerlinNoise(t * 3f, 0f);
                break;
            case Pattern.Buzz:
                lvl = 0.9f + 0.1f * Mathf.PerlinNoise(t * 12f, 1f);
                if (stateTimer < 0f) { stateTimer = Random.Range(2f, 8f); burstUntil = Time.time + Random.Range(0.05f,0.3f); }
                if (Time.time < burstUntil) lvl *= Random.value < 0.5f ? 0.2f : 1f;
                break;
            case Pattern.Flicker:
                if (stateTimer < 0f) { stateTimer = Random.Range(2.5f, 10f); burstUntil = Time.time + Random.Range(0.08f, 0.85f); }
                lvl = Time.time < burstUntil ? Step(t * 18f) : 0.96f + 0.04f * Mathf.PerlinNoise(t * 4f, 2f);
                break;
            case Pattern.Dying:
                if (stateTimer < 0f)
                {
                    stateTimer = Random.Range(1.5f, 7f);
                    held = Random.value < 0.35f ? Random.Range(0.25f, 0.9f) : 0.02f;
                    burstUntil = Time.time + Random.Range(0.1f, 0.8f);
                }
                lvl = Time.time < burstUntil ? Step(t * (17f+seed*0.1f)) * 0.8f : held;
                break;
            case Pattern.Strobe:
                if (stateTimer < 0f) { stateTimer = Random.Range(0.04f, 0.35f); held = Random.value < 0.55f ? 1f : 0.03f; }
                lvl = held;
                break;
            case Pattern.Candle:
                lvl = 0.75f + 0.25f * Mathf.PerlinNoise(t * 7f, 3f) - (Mathf.PerlinNoise(t * 1.3f, 9f) < 0.2f ? 0.2f : 0f);
                break;
            case Pattern.Tv:
                if (stateTimer < 0f) { stateTimer = Random.Range(0.08f, 0.7f); held = Random.Range(0.35f, 1f); }
                lvl = held * (0.92f + 0.08f * Mathf.PerlinNoise(t * 30f, 4f));
                break;
            case Pattern.Emergency:
                lvl = 0.35f + 0.65f * Mathf.Max(0f, Mathf.Sin(t * 2.2f));
                break;
            case Pattern.Vending:
                lvl = 0.95f + 0.05f * Mathf.PerlinNoise(t * 20f, 5f);
                if (stateTimer < 0f) { stateTimer = Random.Range(6f, 20f); burstUntil = Time.time + 0.3f; }
                if (Time.time < burstUntil) lvl *= Step(t * 30f);
                break;
            case Pattern.Jukebox:
                lvl = 0.8f + 0.2f * Mathf.Sin(t * 1.7f);
                if (Light) Light.color = Color.Lerp(baseColor, new Color(1f, 0.3f, 0.5f), 0.5f + 0.5f * Mathf.Sin(t * 0.6f));
                break;
            case Pattern.Projector:
                lvl = ProjectorOn ? 0.9f + 0.1f * Mathf.Sin(t * 150f) : 0f;
                break;
        }
        if (Time.time >= powerDelay) powerLevel = Mathf.MoveTowards(powerLevel, powerTarget, dt * (powerTarget > powerLevel ? 1.5f : 6f));
        if (powerTarget > 0f && powerLevel < 1f && powerLevel > 0.05f) lvl *= Step(t * 22f);   // tubes struggle to strike
        filament=Mathf.Lerp(filament,lvl,1f-Mathf.Exp(-dt/0.045f));
        Level = (fluorescent?lvl:filament) * powerLevel;

        if (Light)
        {
            Light.intensity = baseIntensity * Level;
            Light.enabled = !Culled && Level > 0.01f;
        }
        if (fixture && glow.Count > 0)
            foreach (var (i, c) in glow)
            {
                fixture.GetPropertyBlock(mpb, i);
                mpb.SetColor("_EmissionColor", c * Mathf.Max(Level, 0.02f));
                fixture.SetPropertyBlock(mpb, i);
            }
        bool bright = Level > 0.5f;
        bool starter = pattern == Pattern.Flicker || pattern == Pattern.Dying || pattern == Pattern.Buzz;
        if (ticks && fluorescent && starter && bright && !wasBright && Time.time >= nextTick && AudioEngine.Instance)
        {
            nextTick = Time.time + Random.Range(9f, 24f);
            var eng = AudioEngine.Instance;
            var p = fixture ? fixture.bounds.center : transform.position;
            if (eng.Listener && (eng.Listener.position - p).sqrMagnitude < 16f &&
                Time.time >= nextGlobalTick && Random.value < 0.3f)
            {
                nextGlobalTick = Time.time + 2f;
                var v = eng.PlayAt(SoundBank.Pick("light_tick"), p, 0.045f, 1f, 0.3f, 4f);
                if (v) v.sendLevel = 0.12f;
            }
        }
        wasBright = bright;
    }

    public bool ProjectorOn = true;

    static float Step(float x) => Mathf.PerlinNoise(x, 0.37f) > 0.45f ? 1f : 0.04f;
}
