using System.Collections.Generic;
using UnityEngine;

// The main breaker in the basement electrical room. Hold to throw the stiff lever: a loud clunk (it hears that
// from anywhere in the basement), the basement tubes struggle to strike, the recorder room gets power.
public class Breaker : Interactable
{
    public static Breaker Instance { get; private set; }
    public Transform lever;
    public List<Lamp> lamps = new List<Lamp>();     // lamps that come on with the power
    public List<MonitorScreen> screens = new List<MonitorScreen>();
    public bool On { get; private set; }
    float held;
    void Awake() => Instance = this;
    // the main fuse is missing: it is in the storage cages (part two)
    public bool needsFuse = true;
    bool HasFuse => !needsFuse || (Game.Instance && Game.Instance.HasKey("fuse"));
    public override string Prompt => On ? "Main breaker: ON" : !HasFuse ? "Main breaker (the main fuse is missing)" : "Hold: fit the fuse and throw the main breaker (loud)";
    public override bool Usable => !On;
    public override float HoldProgress => On || !HasFuse ? -1f : held / 1.2f;
    public override void Tap()
    {
        if (!On && !HasFuse && AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("light_tick"), transform.position, 0.3f, 0.8f, 0.5f, 6f);
    }
    public override void Hold(float dt, Vector2 mouse)
    {
        if (On || !HasFuse) return;
        held += dt;
        if (lever) lever.localRotation = Quaternion.Euler(-40f + 80f * Mathf.Clamp01(held / 1.2f), 0f, 0f);
        if (held >= 1.2f) Throw();
    }
    public override void Release() { if (!On) { held = 0f; if (lever) lever.localRotation = Quaternion.Euler(-40f, 0f, 0f); } }
    public void Throw()
    {
        if (On) return;
        On = true;
        if (lever) lever.localRotation = Quaternion.Euler(40f, 0f, 0f);
        var ae = AudioEngine.Instance;
        if (ae)
        {
            ae.PlayAt(SoundBank.Pick("breaker_clunk"), transform.position, 1f, 0.9f, 1.5f, 40f);
            ae.PlayAt(SoundBank.Pick("power_up"), transform.position, 0.6f, 1f, 1f, 30f);
        }
        NoiseBus.Emit(transform.position, 72f, true, "breaker", this);
        if (Hud.Instance) Hud.Instance.Caption("[breaker: CLUNK]", transform.position);
        foreach (var l in lamps) if (l) l.PowerUp(Random.Range(0.2f, 1.8f));
        foreach (var s in screens) if (s) { s.on = true; s.Refresh(); }
        if (PlayerMotor.Instance) PlayerMotor.Instance.Shake(0.25f);
        if (Game.Instance) Game.Instance.PowerOn();
    }
}
