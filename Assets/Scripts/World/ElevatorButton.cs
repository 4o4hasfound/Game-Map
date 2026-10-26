using UnityEngine;

// The single button inside a car: takes you to the other floor this elevator serves.
// On a dead elevator the doors themselves answer with a locked rattle.
public class ElevatorButton : Interactable
{
    public ElevatorStop stop;
    public string label;
    public bool broken;

    public override string Prompt => broken ? "OUT OF ORDER" : "RIDE TO " + label;

    public override void Use(PlayerMotor player)
    {
        if (broken)
        {
            OneShot("door_locked", transform.position + Vector3.up * 0.2f, 0.7f);
            return;
        }
        OneShot("elev_button", transform.position, 0.7f, Random.Range(0.97f, 1.03f), 0.3f, 8f);
        if (ElevatorSystem.Instance) ElevatorSystem.Instance.Ride(stop);
    }
}
