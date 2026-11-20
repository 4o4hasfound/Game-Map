using UnityEngine;

// Something to carry: a glass bottle (throw it with the right mouse button to make a noise where it lands) or the
// portable radio (set it down with the right mouse button; it switches itself on a few seconds later).
public class Pickup : Interactable
{
    public string kind = "bottle";          // bottle, radio
    public int count = 1;                   // bottles left here
    [TextArea] public string description = "";
    public override string Prompt => kind == "battery" ? "Take the camera battery (refills the light)" : kind == "radio" ? "Take the portable radio" : count > 1 ? $"Take a bottle ({count} here)" : "Take the bottle";
    public override bool Usable => kind == "battery" ? CameraLight.Instance && CameraLight.Instance.Battery < 0.97f : PlayerHands.Instance && (kind == "radio" ? !PlayerHands.Instance.HasRadio : PlayerHands.Instance.Bottles < PlayerHands.MaxBottles);
    public override void Tap()
    {
        var h = PlayerHands.Instance;
        if (!h || !Usable) return;
        if (kind == "battery")
        {
            CameraLight.Instance.Refill(1f);
            if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("radio_click"), transform.position, 0.25f, 1.1f, 0.5f, 8f);
            if (Hud.Instance) Hud.Instance.Announce("CAMERA BATTERY\nthe light is full again (F)", 2.5f);
            if (Game.Instance) Game.Instance.Log("took a camera battery");
            InteractAnim.Play(InteractAnim.Kind.Take, transform, () => { if (this) gameObject.SetActive(false); });
            return;
        }
        if (kind == "radio") h.HasRadio = true; else h.Bottles++;
        count--;
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick(kind == "radio" ? "radio_click" : "amb_glass"), transform.position, 0.2f, 1.2f, 0.5f, 8f);
        NoiseBus.Emit(transform.position, 26f, true, "pick up " + kind, this);
        if (Hud.Instance) Hud.Instance.Announce(kind == "radio" ? "PORTABLE RADIO\nright click: set it down (it turns on after 5 s)" : $"BOTTLE x{h.Bottles}\nright click: throw", 2.5f);
        if (Game.Instance) Game.Instance.Log("took " + kind);
        if (count <= 0) InteractAnim.Play(InteractAnim.Kind.Take, transform, () => { if (this) gameObject.SetActive(false); });
        else
        {
            // one bottle (the nearest still there) comes to your hand; the others stay
            Transform one = null; float best = float.MaxValue;
            var cam = Game.Instance && Game.Instance.PlayerCamera ? Game.Instance.PlayerCamera.transform.position : transform.position;
            foreach (Transform c in transform)
                if (c.gameObject.activeSelf && c.GetComponent<Renderer>() && (c.position - cam).sqrMagnitude < best) { best = (c.position - cam).sqrMagnitude; one = c; }
            if (one) InteractAnim.Play(InteractAnim.Kind.Take, one, () => { if (one) one.gameObject.SetActive(false); });
        }
    }
}
