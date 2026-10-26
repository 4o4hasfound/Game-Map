using UnityEngine;

// Main breaker for one floor's circuit: everything on the circuit dies in a cascade (lights, tube buzz, vending
// glow), with the clunk of the lever and the hum winding down; switching it back makes the tubes struggle to strike.
public class BreakerPanel : Interactable
{
    public string circuit = "L1";
    bool on = true;

    public override string Prompt => on ? "CUT POWER (" + circuit + ")" : "RESTORE POWER (" + circuit + ")";

    public override void Use(PlayerMotor player)
    {
        on = !on;
        Vector3 p = transform.position + Vector3.up * 1.4f;
        OneShot("breaker_clunk", p, 1f);
        OneShot(on ? "power_up" : "power_down", p, 0.8f, 1f, 1f, 30f);
        foreach (var l in LightFlicker.All)
        {
            if (l.circuit != circuit) continue;
            float d = Vector3.Distance(l.transform.position, transform.position);
            l.SetPower(on, (on ? 0.4f : 0.05f) + d * 0.015f + Random.Range(0f, on ? 1.2f : 0.3f));
        }
        foreach (var dev in FindObjectsByType<PoweredDevice>(FindObjectsSortMode.None))
            if (dev.circuit == circuit) dev.SetPower(on);
    }
}

// Devices that stop when the floor loses power (vending machine, TVs, jukebox, monitors ...)
public abstract class PoweredDevice : Interactable
{
    public string circuit = "";
    protected bool powered = true;

    public virtual void SetPower(bool on)
    {
        powered = on;
    }
}
