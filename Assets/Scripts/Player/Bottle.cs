using UnityEngine;

// A thrown bottle: it smashes on the first hard contact (72 dB where it lands, not where you are).
public class Bottle : MonoBehaviour
{
    float born;
    bool done;
    void Start() => born = Time.time;
    void OnCollisionEnter(Collision c)
    {
        if (done || Time.time - born < 0.05f) return;
        if (c.relativeVelocity.magnitude < 2.5f && Time.time - born < 3f) return;
        done = true;
        var p = c.GetContact(0).point;
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("glass_break"), p, 0.85f, Random.Range(0.95f, 1.08f), 1.2f, 40f);
        NoiseBus.Emit(p, 72f, false, "bottle smash", this);
        if (Hud.Instance) Hud.Instance.Caption("[glass smashing]", p);
        Destroy(gameObject);
    }
    void Update() { if (!done && Time.time - born > 6f) Destroy(gameObject); }
}
