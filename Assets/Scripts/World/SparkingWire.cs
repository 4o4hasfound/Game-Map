using UnityEngine;

// A live cable hanging out of the ceiling. Every so often it arcs: a burst of blue-white flashes that throw the
// room's shadows about, a shower of sparks, the snap of the arc. Sometimes loud enough for the listener to come
// and look (38 dB).
public class SparkingWire : MonoBehaviour
{
    public Light flash;
    public ParticleSystem sparks;
    float next, burstUntil, nextFlicker;

    void Start()
    {
        next = Time.time + Random.Range(4f, 25f);
        if (flash) flash.enabled = false;
    }

    public static readonly System.Collections.Generic.List<SparkingWire> All = new System.Collections.Generic.List<SparkingWire>();
    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);
    public void Burst() { next = Time.time; burstUntil = 0f; }

    void Update()
    {
        if (Time.time >= next && Time.time >= burstUntil)
        {
            burstUntil = Time.time + Random.Range(0.25f, 0.9f);
            next = burstUntil + Random.Range(9f, 30f);
            var ae = AudioEngine.Instance;
            if (ae)
            {
                ae.PlayAt(SoundBank.Pick("light_pop"), transform.position, 0.45f, Random.Range(1.1f, 1.5f), 0.6f, 18f);
                ae.PlayAt(SoundBank.Pick("light_buzz"), transform.position, 0.3f, Random.Range(1.2f, 1.6f), 0.5f, 10f);
            }
            if (Random.value < 0.35f) NoiseBus.Emit(transform.position, 38f, false, "sparks", this);
            if (Hud.Instance && Random.value < 0.5f) Hud.Instance.Caption("[electrical crackle]", transform.position);
        }
        bool on = Time.time < burstUntil;
        if (on && Time.time >= nextFlicker)
        {
            nextFlicker = Time.time + Random.Range(0.02f, 0.07f);
            bool lit = Random.value < 0.55f;
            if (flash) { flash.enabled = lit; flash.intensity = Random.Range(2f, 7f); }
            if (lit && sparks) sparks.Emit(Random.Range(4, 14));
            if (lit && AudioEngine.Instance && Random.value < 0.3f) AudioEngine.Instance.PlayAt(SoundBank.Pick("light_tick"), transform.position, 0.3f, Random.Range(1.3f, 1.8f), 0.4f, 10f);
        }
        if (!on && flash && flash.enabled) flash.enabled = false;
    }
}
