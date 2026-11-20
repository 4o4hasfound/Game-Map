using System.Collections.Generic;
using UnityEngine;
// A wall switch, bedside lamp switch or pull cord: toggles its lamps (a click the Faceless can hear).
public class LightSwitch : Interactable
{
    public Lamp[] lamps;
    public bool pullCord;
    public override string Prompt => lamps.Length > 0 && lamps[0].on ? "Light off" : "Light on";

    public override void Tap()
    {
        InteractAnim.Play(pullCord ? InteractAnim.Kind.Press : InteractAnim.Kind.Flip, transform);
        bool v = !(lamps.Length > 0 && lamps[0].on);
        foreach (var l in lamps) if (l) l.Set(v);
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick(pullCord ? "light_tick" : "light_tick"), transform.position, 0.5f, pullCord ? 0.8f : 1f, 0.5f, 15f);
        if (v && pullCord && AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("light_buzz"), transform.position, 0.2f, 1f, 0.5f, 10f);
        NoiseBus.Emit(transform.position, pullCord ? 30f : 35f, true, "switch");
    }
}
