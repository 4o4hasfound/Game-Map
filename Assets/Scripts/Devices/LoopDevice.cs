using UnityEngine;

// Simple switchable sound sources: taps, projector, security monitors, vending and ice machines, clocks,
// typewriters and the reception bell.
public class LoopDevice : PoweredDevice
{
    public enum Kind { Faucet, Projector, Monitors, Vending, IceMachine, Clock, Typewriter, Bell }
    public Kind kind;
    public string loopClip;
    public bool startOn = true;
    public LightFlicker projectorLight;

    SpatialVoice voice;
    bool on;
    float nextChime;

    public override string Prompt
    {
        get
        {
            switch (kind)
            {
                case Kind.Faucet: return on ? "TURN OFF THE TAP" : "TURN ON THE TAP";
                case Kind.Projector: return on ? "STOP THE PROJECTOR" : "START THE PROJECTOR";
                case Kind.Monitors: return on ? "SWITCH OFF MONITORS" : "SWITCH ON MONITORS";
                case Kind.Vending: return "BUY SOMETHING";
                case Kind.IceMachine: return "GET ICE";
                case Kind.Clock: return "PULL THE CHIME";
                case Kind.Typewriter: return "TYPE";
                default: return "RING THE BELL";
            }
        }
    }

    void Start()
    {
        float level = kind == Kind.Clock ? 0.16f : kind == Kind.Monitors ? 0.18f : (kind == Kind.Faucet || kind == Kind.Projector) ? 0.4f : 0.12f;
        voice = MakeVoice(kind + "Sound", level, 0.6f, 14f, kind == Kind.Monitors ? 0.4f : 0f);
        if (string.IsNullOrEmpty(loopClip))
            loopClip = kind == Kind.Clock ? "amb_clock_tick" : kind == Kind.Vending ? "amb_vending" : kind == Kind.IceMachine ? "amb_ice" :
                kind == Kind.Monitors ? "amb_tv_static" : kind == Kind.Projector ? "amb_projector" : kind == Kind.Faucet ? "amb_faucet" : null;
        on = startOn && kind != Kind.Typewriter && kind != Kind.Bell;
        if (on && loopClip != null) voice.Play(SoundBank.Get(loopClip), true, Random.value * 4f);
        if (projectorLight) projectorLight.ProjectorOn = on;
        nextChime = Time.time + Random.Range(40f, 120f);
    }

    public override void Use(PlayerMotor p)
    {
        Vector3 at = transform.position + Vector3.up * 0.8f;
        switch (kind)
        {
            case Kind.Vending: if (powered) OneShot("vending_buy", at, 0.9f); break;
            case Kind.IceMachine: if (powered) OneShot("ice_drop", at, 0.9f); break;
            case Kind.Clock: OneShot("clock_chime", transform.position + Vector3.up * 1.9f, 0.9f, 1f, 1.5f, 40f); break;
            case Kind.Typewriter: OneShot("typewriter", transform.position + Vector3.up * 0.1f, 0.9f, 1f, 0.6f, 20f); break;
            case Kind.Bell: OneShot("bell_desk", transform.position + Vector3.up * 0.05f, 1f, Random.Range(0.98f, 1.02f), 0.8f, 35f); break;
            default:
                on = !on;
                OneShot(kind == Kind.Faucet ? "valve_squeak" : "radio_click", at, 0.6f);
                if (on && powered && loopClip != null) voice.Play(SoundBank.Get(loopClip), true, 0f);
                else voice.Stop();
                if (kind == Kind.Projector)
                {
                    if (on) OneShot("projector_on", at, 0.7f);
                    if (projectorLight) projectorLight.ProjectorOn = on;
                }
                break;
        }
    }

    public override void SetPower(bool v)
    {
        base.SetPower(v);
        if (kind == Kind.Faucet || kind == Kind.Clock) return;
        if (!v) voice.Stop();
        else if (on && loopClip != null) voice.Play(SoundBank.Get(loopClip), true, 0f);
        if (projectorLight) projectorLight.ProjectorOn = v && on;
    }

    void Update()
    {
        if (kind == Kind.Clock && Time.time > nextChime)
        {
            nextChime = Time.time + Random.Range(90f, 180f);
            OneShot("clock_chime", transform.position + Vector3.up * 1.9f, 0.8f, 1f, 1.5f, 40f);
        }
    }
}
