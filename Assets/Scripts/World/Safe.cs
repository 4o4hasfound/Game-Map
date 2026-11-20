using UnityEngine;

// The manager's wall safe (part one): the basement key is inside. Its four-digit code is written in two halves
// upstairs: one behind the dresser in Room 214 (east wing), one in the previous viewer's bag in Room 209 (west wing,
// reached through the burnt-out 207). With both halves you dial it and the door swings open.
public class Safe : Interactable
{
    public Transform door;          // hinged on its left edge
    public KeyItem key;             // inside, reachable once open
    public bool Open { get; private set; }
    static int Halves => Game.Instance ? (Game.Instance.HasKey("code_a") ? 1 : 0) + (Game.Instance.HasKey("code_b") ? 1 : 0) : 0;
    public override string Prompt => Open ? "" : Halves < 2 ? $"Wall safe: a four-digit code ({Halves} of 2 halves found)" : "Dial the code and open the safe";
    public override bool Usable => !Open;

    public override void Tap()
    {
        if (Open) return;
        var ae = AudioEngine.Instance;
        if (Halves < 2)
        {
            if (ae) ae.PlayAt(SoundBank.Pick("door_locked"), transform.position, 0.3f, 1.5f, 0.5f, 8f);
            NoiseBus.Emit(transform.position, 24f, true, "safe dial", this);
            if (Hud.Instance) Hud.Instance.Announce("THE SAFE\nyou need both halves of the code", 2f);
            return;
        }
        Open = true;
        if (ae)
        {
            ae.PlayAt(SoundBank.Pick("light_tick"), transform.position, 0.35f, 0.7f, 0.5f, 8f);
            ae.PlayAt(SoundBank.Pick("door_unlatch_metal"), transform.position, 0.5f, 0.9f, 0.6f, 12f);
        }
        NoiseBus.Emit(transform.position, 34f, true, "safe", this);
        if (door) door.localRotation = Quaternion.Euler(0f, -105f, 0f);
        // out of the way of the key: the open safe no longer catches your aim
        var col = GetComponent<Collider>();
        if (col) col.enabled = false;
        if (key) { key.gameObject.SetActive(true); key.Reveal(); }
        if (Game.Instance) Game.Instance.SafeOpened();
    }
}
