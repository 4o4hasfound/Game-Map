using UnityEngine;

// A fire evacuation plan on the wall ("YOU ARE HERE"), at the start of each part and at the stair heads. Film it
// (E) and the camera keeps a still of it: that floor's whole plan appears on the camcorder map (M). Without it the
// map only shows the rooms you have walked through.
public class FloorPlan : Interactable
{
    public int floor;
    public string floorName = "";
    public override string Prompt => Game.Instance && Game.Instance.HasPlan(floor) ? $"Fire plan: {floorName} (filmed, M: map)" : $"Film the fire plan: {floorName} (adds it to the map, M)";
    public override bool Usable => true;

    public override void Tap()
    {
        var g = Game.Instance;
        if (!g) return;
        bool had = g.HasPlan(floor);
        g.FilmPlan(floor);
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("ui_tape"), transform.position, 0.25f, 1.6f, 0.5f, 6f);
        if (Hud.Instance) Hud.Instance.Announce(had ? "FIRE PLAN\nalready on the map (M)" : "FIRE PLAN FILMED\n" + floorName.ToUpperInvariant() + ": press M for the map", 2.5f);
    }
}
