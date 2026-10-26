using UnityEngine;

// Somewhere to sit (chairs, armchairs, sofas, benches, stools: exported by the map generator). E sits down; E again,
// or any walking key, stands up. The object sits on the seat surface facing the way a sitter would face.
public class Seat : Interactable
{
    public float height = 0.46f;         // seat surface above the floor
    public string kind = "chair";
    bool occupied;

    public float FloorY => transform.position.y - height;
    public override string Prompt => occupied ? "STAND UP" : kind == "bar_stool" ? "SIT ON THE STOOL" : kind == "sofa" || kind == "bench" ? "SIT DOWN" : "SIT";

    public override void Use(PlayerMotor player)
    {
        if (!player) return;
        if (occupied) { player.StandUp(); return; }
        if (player.SeatedOn) return;
        occupied = true;
        player.SitOn(this);
        // old upholstery and joints: a creak of the frame, a rustle of clothing
        OneShot("amb_creak", transform.position, 0.12f, 1f, 0.5f, 10f);
        OneShot("cloth", transform.position + Vector3.up * 0.3f, 0.08f, 1f, 0.4f, 6f);
        NoiseBus.Emit(transform.position, 0.6f, false);
    }

    public void Vacated()
    {
        occupied = false;
        OneShot("cloth", transform.position + Vector3.up * 0.3f, 0.06f, 1f, 0.4f, 6f);
    }
}
