using UnityEngine;

// Room 214's dresser. Hold to pull it away from the wall, a step at a time; every step scrapes (55 dB). It stays
// where you leave it. Behind it hangs the basement key.
public class Dresser : Interactable
{
    public float pulled;              // metres from the wall
    public float needed = 0.55f;
    public Vector3 pullDir = Vector3.back;
    public KeyItem key;
    public Transform body;           // the furniture (this component sits on its use-trigger)
    Vector3 home;
    float stepT;

    Transform Body => body ? body : transform;
    void Start() { home = Body.position; Apply(); }

    public override string Prompt => pulled >= needed ? "Pulled out" : "Hold: pull the dresser";
    public override bool Usable => pulled < needed;
    public override float HoldProgress => pulled / needed;

    public override void Hold(float dt, Vector2 mouse)
    {
        if (pulled >= needed) return;
        stepT += dt;
        if (stepT < 0.45f) return;
        stepT = 0f;
        PullStep();
    }

    public void PullStep()
    {
        pulled = Mathf.Min(needed, pulled + 0.11f);
        Apply();
        if (AudioEngine.Instance)
        {
            AudioEngine.Instance.PlayAt(SoundBank.Pick("fs_rubble"), Body.position, 0.55f, 0.55f, 1f, 30f);
            AudioEngine.Instance.PlayAt(SoundBank.Pick("amb_creak"), transform.position, 0.35f, 1.3f, 1f, 25f);
        }
        NoiseBus.Emit(transform.position, 55f, true, "dresser");
        if (Hud.Instance) Hud.Instance.Caption("[scrape]", transform.position);
        if (pulled >= needed && key) key.Reveal();
    }

    public override void Release() { stepT = 0f; }

    void Apply() { Body.position = home + pullDir.normalized * pulled; }
}
