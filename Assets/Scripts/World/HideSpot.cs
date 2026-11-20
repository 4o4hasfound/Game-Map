using UnityEngine;

// A wardrobe, a bed, a bath, a locker, a cart, a table under its long cloth, behind the pews, a cage, the cold room.
// Inside you look out through a gap (and lean with your head to peek). It can't see you; it can only hear you.
// So keep still: looking around quickly, or moving your head when the camera is on, rustles (26 dB), and your
// breathing is still a noise unless you hold your breath (SPACE). If it is standing right there and hears you, it
// pulls you out.
public class HideSpot : Interactable
{
    public static HideSpot Current { get; private set; }
    public static bool Inside => Current != null;
    public Transform view;            // where the eye goes
    public string kind = "wardrobe";
    Vector3 exitPos;
    float exitYaw, enteredAt, lastRustle;

    public override string Prompt => Current == this ? "Get out" : "Hide " + Where(kind);
    static string Where(string k)
    {
        switch (k)
        {
            case "bed": return "under the bed";
            case "bath": return "in the bath";
            case "cart": return "in the cart";
            case "locker": return "in the locker";
            case "table": return "under the table";
            case "pew": return "behind the pews";
            case "cage": return "in the cage";
            case "curtain": return "behind the curtain";
            case "shelf": return "behind the shelves";
            case "desk": return "under the desk";
            case "cold": return "in the cold room";
            default: return "in the wardrobe";
        }
    }

    public override void Tap()
    {
        var m = PlayerMotor.Instance;
        if (!m) return;
        if (Current == this) { Leave(); return; }
        if (Current) return;
        exitPos = m.transform.position;
        exitYaw = m.Yaw;
        Current = this;
        enteredAt = Time.time;
        m.Locked = true;
        m.HideView = view;
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick(kind == "wardrobe" || kind == "locker" ? "door_creak" : "cloth"), view.position, 0.25f, 1.2f, 0.5f, 15f);
        NoiseBus.Emit(view.position, 28f, true, "hide");
        if (Game.Instance) Game.Instance.Log("hid " + name);
    }

    public void Leave()
    {
        var m = PlayerMotor.Instance;
        Current = null;
        if (!m) return;
        m.HideView = null;
        m.Teleport(exitPos, exitYaw, 0f);
        m.Locked = false;
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick(kind == "wardrobe" || kind == "locker" ? "door_creak" : "cloth"), view.position, 0.25f, 1.1f, 0.5f, 15f);
        NoiseBus.Emit(view.position, 28f, true, "hide");
    }

    public static void ForceLeave() { if (Current) Current.Leave(); }

    // how much you are moving inside right now (0 still .. 1+ fidgeting)
    public static float Fidget
    {
        get
        {
            float f = PlayerMotor.Instance ? PlayerMotor.Instance.HideLookSpeed / 160f : 0f;
            var ft = FaceTracker.Instance;
            if (Settings.UseCamera && ft && ft.Running && ft.FacePresent) f = Mathf.Max(f, ft.Motion / 0.9f);
            return f;
        }
    }

    void Update()
    {
        if (Current != this || Time.time - enteredAt < 0.8f) return;
        if (Fidget > 1f && Time.time - lastRustle > 1.2f)
        {
            lastRustle = Time.time;
            if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick(kind == "wardrobe" || kind == "locker" ? "amb_creak" : "cloth"), view.position, 0.18f, 1.2f, 0.5f, 12f);
            NoiseBus.Emit(view.position, 26f, true, "rustle");
            if (Hud.Instance) Hud.Instance.Caption("[you rustle]", view.position);
        }
    }
}
