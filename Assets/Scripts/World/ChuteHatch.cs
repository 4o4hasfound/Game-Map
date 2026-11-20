using UnityEngine;

// The laundry chute hatch (guest floor and ground floor). Drop a bottle down it: a second later it smashes in the
// basement laundry, far from you. A lure that works across floors.
public class ChuteHatch : Interactable
{
    public Vector3 bottom;            // where things land (basement laundry)
    float pendingAt = -1f;
    public override string Prompt => PlayerHands.Instance && PlayerHands.Instance.Bottles > 0 ? "Drop a bottle down the chute" : "Laundry chute (falls to the basement)";
    public override bool Usable => PlayerHands.Instance && PlayerHands.Instance.Bottles > 0;
    public override void Tap()
    {
        if (!Usable) return;
        PlayerHands.Instance.UseBottle();
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("amb_chute_wind"), transform.position, 0.3f, 1.3f, 0.6f, 10f);
        pendingAt = Time.time + 1.4f;
        if (Game.Instance) Game.Instance.Log("dropped a bottle down the chute");
    }
    void Update()
    {
        if (pendingAt < 0f || Time.time < pendingAt) return;
        pendingAt = -1f;
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("glass_break"), bottom, 0.9f, 1f, 1.2f, 40f);
        NoiseBus.Emit(bottom, 72f, false, "bottle (chute)", this);
        if (Hud.Instance) Hud.Instance.Caption("[glass smashing far below]", bottom);
    }
}
