using UnityEngine;

// The brass slide bolt on the inside of a guest-room (or recorder room) door. Slide it across and the listener
// cannot open the door: it beats on it, then gives up. In the finale it breaks the bolt after a few blows.
public class DoorBolt : Interactable
{
    public HingeDoor door;
    public Transform slide;
    public override string Prompt => !door ? "" : door.bolted ? "Draw the bolt back" : door.Openness > 0.06f ? "Bolt (close the door first)" : "Slide the bolt across";
    public override bool Usable => door && !door.locked && (door.bolted || door.Openness <= 0.06f);
    public override void Tap()
    {
        if (!Usable) return;
        door.SetBolt(!door.bolted);
        if (Game.Instance) Game.Instance.Log((door.bolted ? "bolted " : "unbolted ") + door.portalName);
    }
    void Update() { if (slide && door) slide.localPosition = new Vector3(door.bolted ? 0.06f : 0f, 0f, 0f); }
}
