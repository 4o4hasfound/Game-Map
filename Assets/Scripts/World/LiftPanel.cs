using UnityEngine;

// The service lift's call panel in the basement pump room (part two). It needs power (the main breaker, which needs
// its fuse) and the lift key from the recorder room. Hold to call: the car takes 30 s to come down and the call
// bell rings all the way (64 dB every 2 s). It hears the bell and comes, hurrying, and during the call it breaks
// bolts. So draw it away first (the generator holds it longest), bolt the doors to buy seconds, or hide and come
// back. When the car arrives its doors open; step in and it carries you up to part three.
public class LiftPanel : Interactable
{
    public float callTime = 30f;
    public GameObject[] hideOnArrival; // the closed lift doors in the wall (and the sign taped to them)
    public GameObject car;             // the lit car behind them, shown when it arrives
    public bool Calling { get; private set; }
    public bool Arrived { get; private set; }
    public float Progress { get; private set; }
    float held, bellT, creakT;
    SpatialVoice motor;

    static bool Powered => Game.Instance && Game.Instance.PowerIsOn;
    static bool HasKey => Game.Instance && Game.Instance.HasKey("liftkey");
    public override string Prompt => Arrived ? "" : Calling ? $"The lift is coming ({Mathf.CeilToInt(callTime - Progress)} s)" :
        !Powered ? "Lift call panel (dead: no power)" : !HasKey ? "Lift call panel (a keyhole: needs the lift key)" : "Hold: call the lift (the bell is loud, 30 s to come down)";
    public override bool Usable => !Arrived && !Calling;
    public override float HoldProgress => Usable && Powered && HasKey ? held / 0.8f : -1f;

    public override void Hold(float dt, Vector2 mouse)
    {
        if (!Usable || !Powered || !HasKey) return;
        held += dt;
        if (held >= 0.8f) Call();
    }
    public override void Release() { held = 0f; }
    public override void Tap()
    {
        if (Usable && (!Powered || !HasKey) && AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("elev_button"), transform.position, 0.3f, 1f, 0.5f, 6f);
    }

    public void Call()
    {
        if (Calling || Arrived) return;
        Calling = true; Progress = 0f; bellT = 0f; creakT = 3f; held = 0f;
        var ae = AudioEngine.Instance;
        if (ae)
        {
            ae.PlayAt(SoundBank.Pick("elev_button"), transform.position, 0.5f, 1f, 0.5f, 8f);
            ae.PlayAt(SoundBank.Pick("elev_start"), transform.position + Vector3.up * 3f, 0.6f, 0.8f, 1f, 30f);
            motor = ae.PlayAt(SoundBank.Get("elev_motor"), transform.position + new Vector3(-1.75f, 2f, 1.5f), 0.5f, 0.75f, 1f, 25f);
        }
        if (Game.Instance) Game.Instance.LiftCallStarted();
    }

    // tests: back to waiting at the top of the shaft
    public void ResetLift()
    {
        ResetCall();
        Arrived = false;
        if (hideOnArrival != null) foreach (var h in hideOnArrival) if (h) h.SetActive(true);
        if (car) car.SetActive(false);
    }

    public void ResetCall()
    {
        if (!Calling) return;
        Calling = false; Progress = 0f;
        if (motor) { motor.Stop(); motor = null; }
    }

    void Update()
    {
        if (!Calling) return;
        float dt = Time.deltaTime;
        Progress += dt;
        bellT -= dt;
        creakT -= dt;
        var ae = AudioEngine.Instance;
        if (bellT <= 0f)
        {
            bellT = 2f;
            if (ae) ae.PlayAt(SoundBank.Pick("elev_chime"), transform.position, 0.8f, 0.72f, 1.5f, 40f);
            NoiseBus.Emit(transform.position, 64f, false, "lift bell", this);
            if (Listener.Instance) Listener.Instance.Alert(transform.position, "the lift bell");
            if (Hud.Instance) Hud.Instance.Caption("[the lift bell]", transform.position);
        }
        if (creakT <= 0f)
        {
            creakT = Random.Range(3f, 6f);
            if (ae) ae.PlayAt(SoundBank.Pick("elev_creak"), transform.position + new Vector3(-1.75f, 2.5f, 1.5f), 0.45f, Random.Range(0.8f, 1f), 1f, 25f);
        }
        if (Progress >= callTime) Arrive();
    }

    // the skip key (testing): the lift is here now
    public void ArriveNow() { if (!Arrived) Arrive(); }

    void Arrive()
    {
        Calling = false;
        Arrived = true;
        if (motor) { motor.Stop(); motor = null; }
        var ae = AudioEngine.Instance;
        if (ae)
        {
            ae.PlayAt(SoundBank.Pick("elev_stop"), transform.position + Vector3.up, 0.6f, 0.9f, 1f, 20f);
            ae.PlayAt(SoundBank.Pick("elev_door"), transform.position, 0.7f, 0.9f, 1f, 20f);
        }
        NoiseBus.Emit(transform.position, 50f, false, "lift doors", this);
        if (hideOnArrival != null) foreach (var h in hideOnArrival) if (h) h.SetActive(false);
        if (car) car.SetActive(true);
        if (Game.Instance) Game.Instance.LiftArrived();
    }
}
