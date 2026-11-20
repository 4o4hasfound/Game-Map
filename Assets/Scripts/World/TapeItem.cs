using UnityEngine;

// The parcel left on your doorstep after the doorbell rings: no stamp, no sender, your name on it in handwriting.
// Opening it (the packing tape tears) gives you the videotape and a note; the game then asks you to play it.
// Its label is a list of names, each one scribbled out or burnt; the last line is blank.
public class TapeItem : Interactable
{
    public string prompt = "Open the envelope";
    public override string Prompt => prompt;
    public override void Tap()
    {
        BedroomVcr.HasTape = true;
        if (Game.Instance) Game.Instance.TapeTaken();
        if (Hud.Instance) Hud.Instance.ShowPaper("THE ENVELOPE",
            "No stamp. No sender. Your name on the front, in handwriting you don't know.\n" +
            "Inside, wrapped in a newspaper from 31 October 2003, a videotape, and a card:\n\n\"PLAY ME\"\n\n" +
            "On the tape's label, a list of names:\n<s>▓▓▓▓▓▓ ▓▓▓▓</s>\n<s>▒▒▒▒▒ ▒▒▒▒▒▒▒</s>\n<s>▓▓▓▓ ▓▓▓▓▓▓</s>\n(burnt)\n<s>▒▒▒▒▒▒▒ ▒▒▒</s>\n\n______________", true);
        var ae = AudioEngine.Instance;
        if (ae)
        {
            ae.PlayAt(SoundBank.Pick("tape_rip"), transform.position, 0.55f, 1.05f, 0.5f, 8f);
            ae.PlayAt(SoundBank.Pick("cloth"), transform.position, 0.25f, 1.2f, 0.5f, 5f);
        }
        // it comes up to you, and it's yours
        InteractAnim.Play(InteractAnim.Kind.Take, transform, () => { if (this) gameObject.SetActive(false); });
    }
}
