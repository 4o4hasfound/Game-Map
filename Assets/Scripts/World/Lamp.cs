using System.Collections.Generic;
using UnityEngine;

// A light fitting with a behaviour, ported from the earlier HotelGame's LightFlicker: steady, flicker (bursts of
// stutter), dying (mostly dark, catching now and then), strobe, buzz, candle, TV glow, emergency (slow pulse),
// vending (a hum with a stutter). Tubes struggle to strike when power returns. Lights stutter as the listener walks
// under them, and in the finale everything near the recorder room flickers.
public class Lamp : MonoBehaviour
{
    public enum Pattern { Steady, Flicker, Dying, Strobe, Buzz, Candle, Tv, Emergency, Vending }
    public static readonly List<Lamp> All = new List<Lamp>();
    public Light lightSource;
    public Renderer bulb;
    public bool on = true;
    public bool powered = true;              // basement lamps wait for the main breaker
    public Pattern pattern = Pattern.Steady;
    public bool fluorescent;
    public float baseIntensity;
    public bool dead;                        // a dead tube or bulb: the switch clicks, the breaker hums, nothing lights
    public float glow = 1f;                  // how bright the fixture itself looks (a shade glows softly, a bare bulb glares)
    public Color emission = new Color(1f, 0.78f, 0.5f);
    static MaterialPropertyBlock mpb;
    float stateTimer, burstUntil, held = 1f, seed, filament = 1f, powerLevel = 1f, powerDelay, lastLevel = -1f;
    public float Level { get; private set; } = 1f;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start()
    {
        if (lightSource && baseIntensity <= 0f) baseIntensity = lightSource.intensity;
        seed = Random.value * 100f;
        stateTimer = Random.Range(0.3f, 8f);
        powerLevel = on && powered ? 1f : 0f;
        Apply(powerLevel);
    }

    public void Set(bool value) { on = value && !dead; powerDelay = 0f; }

    public void PowerUp(float delay)
    {
        powered = true;
        on = !dead;
        powerDelay = Time.time + delay;
    }

    static float Step(float x) => Mathf.PerlinNoise(x, 0.37f) > 0.45f ? 1f : 0.04f;

    // lamps far from you (out of sight on the floor) flicker at a quarter of the rate; the skipped time is carried
    // over so their timers and power-up still run at the right speed
    float skippedDt;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), BaseColorId = Shader.PropertyToID("_BaseColor");

    void Update()
    {
        var gp = Game.Instance;
        if (gp && ((Time.frameCount + (int)seed) & 3) != 0 && (gp.PlayerFeet - transform.position).sqrMagnitude > 24f * 24f)
        { skippedDt += Time.deltaTime; return; }
        float t = Time.time + seed;
        float dt = Time.deltaTime + skippedDt;
        skippedDt = 0f;
        float lvl = 1f;
        stateTimer -= dt;
        switch (pattern)
        {
            case Pattern.Steady:
                lvl = 0.97f + 0.03f * Mathf.PerlinNoise(t * 3f, 0f);
                break;
            case Pattern.Buzz:
                lvl = 0.9f + 0.1f * Mathf.PerlinNoise(t * 12f, 1f);
                if (stateTimer < 0f) { stateTimer = Random.Range(2f, 8f); burstUntil = Time.time + Random.Range(0.05f, 0.3f); }
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
                lvl = Time.time < burstUntil ? Step(t * (17f + seed * 0.1f)) * 0.8f : held;
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
        }
        // the listener passing underneath makes lights stutter
        var it = Listener.Instance;
        if (it && it.Visible)
        {
            var d = it.transform.position - transform.position;
            if (Mathf.Abs(d.y + 2f) < 3f && d.sqrMagnitude < 30f) lvl *= Step(t * 24f) * 0.9f + 0.1f;
        }
        var g = Game.Instance;
        if (g && g.Finale && Vector3.Distance(transform.position, LevelLayout.VcrSpot) < 14f) lvl *= Step(t * 15f);
        // the building's wiring browns out and blacks out (emergency lights run on batteries)
        if (pattern != Pattern.Emergency && powered) lvl *= PowerGrid.Factor(transform.position);

        float powerTarget = on && powered ? 1f : 0f;
        if (Time.time >= powerDelay) powerLevel = Mathf.MoveTowards(powerLevel, powerTarget, dt * (powerTarget > powerLevel ? 1.5f : 6f));
        if (powerTarget > 0f && powerLevel < 1f && powerLevel > 0.05f) lvl *= Step(t * 22f);   // tubes struggle to strike
        filament = Mathf.Lerp(filament, lvl, 1f - Mathf.Exp(-dt / 0.045f));
        Level = (fluorescent ? lvl : filament) * powerLevel;
        if (Mathf.Abs(Level - lastLevel) > 0.01f) Apply(Level);
    }

    void Apply(float level)
    {
        lastLevel = level;
        if (lightSource)
        {
            lightSource.intensity = baseIntensity * level;
            lightSource.enabled = level > 0.01f;
        }
        if (bulb)
        {
            mpb ??= new MaterialPropertyBlock();
            bulb.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionId, emission * 2.2f * level * glow);
            mpb.SetColor(BaseColorId, Color.Lerp(new Color(0.25f, 0.24f, 0.22f), Color.Lerp(emission, Color.white, 0.5f), level));
            bulb.SetPropertyBlock(mpb);
        }
    }

    public float Intensity => lightSource && lightSource.enabled ? lightSource.intensity : 0f;
}
