using UnityEngine;

// The master VCR in the antenna room on the roof (part three), broadcasting you, labelled with today's date. Hold to
// eject: it grinds for 18 s (60 dB, every second) while the tape fights back, and it hears that from anywhere on
// the roof and comes, hurrying. Let go and the tape winds back in. The door bolts, but in the finale it breaks a
// bolt in four blows: draw it away first (the PA horn at the far corner holds it longest).
public class SecurityVcr : Interactable
{
    public float ejectTime = 18f;
    public float windBack = 1.5f;          // seconds of progress lost per second you are not holding
    int heldFrame = -10;
    public float progress;
    public bool Done { get; private set; }
    SpatialVoice grind;
    float noiseT;

    // the master on the roof runs on the antenna room's own supply (it has been broadcasting all night)
    bool Powered => true;
    public override string Prompt => Done ? "" : !Powered ? "Master VCR (no power: the main breaker is off)" : progress > 0f ? "Keep holding: eject" : "Hold: eject the tape (loud, 12 s)";
    public override bool Usable => !Done && Game.Instance && Game.Instance.State == Game.Phase.Hotel;
    public override float HoldProgress => Powered ? progress / ejectTime : -1f;

    public override void Hold(float dt, Vector2 mouse)
    {
        if (Done || !Powered) return;
        if (Game.Instance) Game.Instance.StartFinale();
        heldFrame = Time.frameCount;
        progress += dt;
        if (!grind && AudioEngine.Instance)
            grind = AudioEngine.Instance.PlayAt(SoundBank.Get("amb_projector"), transform.position, 0.6f, 0.6f, 0.5f, 25f);
        noiseT -= dt;
        if (noiseT <= 0f) { noiseT = 1f; NoiseBus.Emit(transform.position, 60f, true, "vcr", this); if (Listener.Instance) Listener.Instance.Alert(transform.position, "the master VCR"); if (Hud.Instance) Hud.Instance.Caption("[VCR grinding]", transform.position); }
        if (progress >= ejectTime)
        {
            Done = true;
            if (grind) grind.Stop();
            if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("ui_tape"), transform.position, 0.8f, 0.8f, 0.5f, 20f);
            if (Game.Instance) Game.Instance.TapeOut();
        }
    }

    public override void Tap() { if (!Powered && AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("light_tick"), transform.position, 0.3f, 1f, 0.5f, 6f); }

    public override void Release() { if (grind) { grind.Stop(); grind = null; } }

    public void ResetForLoop() { if (grind) { grind.Stop(); grind = null; } progress = 0f; }

    void Update()
    {
        // let go and the tape is pulled back in
        if (!Done && progress > 0f && Time.frameCount - heldFrame > 2) progress = Mathf.Max(0f, progress - windBack * Time.deltaTime);
    }
}
