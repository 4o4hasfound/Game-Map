using UnityEngine;

// A placed sound from the map: a continuous loop, or random one-shots (drips, knocks, creaks) at an interval.
// Optionally tied to a light so that e.g. a fluorescent tube only buzzes while it is lit.
[RequireComponent(typeof(SpatialVoice))]
public class AmbientEmitter : MonoBehaviour
{
    public string clip;
    public float volume = 0.6f;
    public bool loop = true;
    public Vector2 interval = new Vector2(3, 9);
    public float pitch = 1f;
    public LightFlicker link;
    public bool powered = true;

    SpatialVoice voice;
    float next;
    float baseVol;

    // overall mix of the placed ambience: most of the building should be quiet, with a few sounds that stand out
    static float CategoryGain(string clip, out float maxD)
    {
        maxD = -1f;
        switch (clip)
        {
            case "light_buzz": maxD = 2.5f; return 0.045f;           // only audible right under the tube
            case "amb_room_tone_low": maxD = 1f; return 0f;
            case "amb_rain_roof": case "amb_rain": maxD = 20f; return 0.18f;
            case "amb_wind": case "amb_wind_shaft": case "amb_chute_wind": maxD = 12f; return 0.07f;
            case "amb_water_flow": case "amb_water_trickle": maxD = 10f; return 0.12f;
            case "amb_fridge": case "amb_vending": case "amb_ice": case "amb_transformer": maxD = 6f; return 0.12f;
            case "amb_boiler": case "amb_pump": case "amb_steam": maxD = 16f; return 0.22f;
            case "amb_drip": maxD = 10f; return 0.22f;
            case "amb_pipe_knock": case "amb_creak": maxD = 16f; return 0.16f;
            case "amb_clock_tick": maxD = 5f; return 0.2f;
            case "amb_escalator": maxD = 10f; return 0.16f;
            default: return 0.25f;
        }
    }

    void Awake()
    {
        voice = GetComponent<SpatialVoice>();
        voice.ambient = true;
        volume *= CategoryGain(clip, out float maxD);
        if (maxD > 0f) voice.maxDistance = Mathf.Min(voice.maxDistance, maxD);
        baseVol = volume;
        voice.sendLevel = 0.32f;
        if (!loop) interval = new Vector2(Mathf.Max(interval.x * 1.8f, 4f), Mathf.Max(interval.y * 2.5f, 12f));
    }

    void OnEnable()
    {
        if (!voice) voice = GetComponent<SpatialVoice>();
        voice.volume = volume;
        if (loop)
        {
            var c = SoundBank.Get(clip);
            // start somewhere random so identical loops never phase up
            if (c) voice.Play(c, true, Random.value * c.length, 1f);
        }
        next = Time.time + Random.Range(interval.x, interval.y);
    }

    void Update()
    {
        float lvl = link ? link.Level : 1f;
        if (!powered) lvl = 0f;
        voice.volume = baseVol * (link ? Mathf.Clamp01(lvl * 1.2f) : lvl);
        if (!loop && powered && baseVol > 0.001f && Time.time >= next)
        {
            next = Time.time + Random.Range(interval.x, interval.y);
            var c = SoundBank.Pick(clip);
            if (c && AudioEngine.Instance)
            {
                var v = AudioEngine.Instance.PlayAt(c, transform.position, baseVol * Random.Range(0.65f, 1f),
                                                  1f, voice.minDistance, voice.maxDistance);
                if (v) v.sendLevel = 0.32f;
            }
        }
    }
}
