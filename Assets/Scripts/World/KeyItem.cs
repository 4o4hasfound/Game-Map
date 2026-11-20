using UnityEngine;

// Something you need to get on: a half of the safe code (behind the dresser in 214, in the bag in 209), the
// basement key (in the safe), the main fuse, the lift key, the owner's keycard. Some only become reachable when
// something is moved or opened (Reveal).
public class KeyItem : Interactable
{
    public string keyId = "basement";
    public string label = "basement key";
    public bool reachable;
    public override string Prompt => "Take the " + label;
    public override bool Usable => reachable;

    public void Reveal() { reachable = true; }

    public override void Tap()
    {
        if (!reachable) return;
        if (Game.Instance) Game.Instance.GiveKey(keyId);
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("ice_drop"), transform.position, 0.3f, 1.8f, 0.5f, 10f);
        NoiseBus.Emit(transform.position, 30f, true, "key");
        InteractAnim.Play(InteractAnim.Kind.Take, transform, () => { if (this) gameObject.SetActive(false); });
    }
}
