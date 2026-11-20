using UnityEngine;

// The hotel's failing wiring. Every half minute or so a floor browns out: every mains light on it sags and stutters
// together for a second or two with a surge hum, the camcorder's picture tears, and now and then the floor blacks
// out completely and the tubes have to struggle back. Emergency lights (battery) and the street static don't care.
// Lamp.cs multiplies its level by Factor(position).
public class PowerGrid : MonoBehaviour
{
    public static PowerGrid Instance { get; private set; }
    float next, start, length;
    int floor = -99;
    bool blackout;
    float seed;
    public int EventFloor => Active ? floor : -99;
    public bool Active => Time.time >= start && Time.time < start + length;
    public bool Blackout => Active && blackout;
    public static bool Quiet;            // the autotest: no random events (they made its light checks depend on timing)

    void Awake() { Instance = this; seed = Random.value * 50f; next = Time.time + Random.Range(18f, 30f); }

    static float Step(float x) => Mathf.PerlinNoise(x, 0.71f) > 0.42f ? 1f : 0.05f;

    void Update()
    {
        var g = Game.Instance;
        if (!g || g.State != Game.Phase.Hotel || Quiet) return;
        if (Time.time < next) return;
        int pf = LevelLayout.FloorOf(g.PlayerFeet);
        floor = Random.value < 0.75f ? pf : Random.Range(-1, 2);
        blackout = Random.value < 0.22f;
        length = blackout ? Random.Range(2.2f, 4f) : Random.Range(0.5f, 2.2f);
        start = Time.time;
        next = Time.time + length + Random.Range(24f, 55f);
        if (floor == pf)
        {
            var ae = AudioEngine.Instance;
            var head = g.PlayerFeet + Vector3.up * 2.6f;
            if (ae)
            {
                ae.PlayAt(SoundBank.Pick(blackout ? "power_down" : "light_buzz"), head + Random.insideUnitSphere * 2f, blackout ? 0.35f : 0.25f, Random.Range(0.8f, 1f), 1f, 12f);
                if (Random.value < 0.6f) ae.PlayAt(SoundBank.Pick("light_tick"), head + Random.insideUnitSphere * 3f, 0.2f, 0.9f, 0.5f, 10f);
            }
            if (VhsScreen.Instance) VhsScreen.Instance.Tear(blackout ? 0.6f : 0.35f, false);
        }
    }

    // tests / the screenshot tour: start an event now
    public void Force(int onFloor, bool black, float seconds)
    {
        floor = onFloor; blackout = black; start = Time.time; length = seconds;
        next = Time.time + seconds + 30f;
    }

    // how much of the mains reaches a lamp at this position right now (1 normally)
    public static float Factor(Vector3 pos)
    {
        var p = Instance;
        if (!p || !p.Active) return 1f;
        if (LevelLayout.FloorOf(pos) != p.floor) return 1f;
        float t = (Time.time - p.start) / p.length;
        float x = Time.time * 23f + pos.x * 0.37f + pos.z * 0.21f + p.seed;
        if (p.blackout)
        {
            // drop out, darkness, then the tubes struggle back
            if (t < 0.12f) return Step(x) * (1f - t / 0.12f);
            if (t < 0.7f) return 0f;
            return Step(x * 1.3f) * Mathf.InverseLerp(0.7f, 1f, t);
        }
        float sag = Mathf.Lerp(0.25f, 0.7f, Mathf.PerlinNoise(Time.time * 2f, p.seed));
        return Step(x) * sag;
    }
}
