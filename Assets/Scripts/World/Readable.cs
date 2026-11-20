using UnityEngine;

// Paper, a register, a label: tap to read it close up. Names are burnt or scribbled out (only the player matters).
public class Readable : Interactable
{
    public string title = "";
    [TextArea] public string text = "";
    public bool handwritten;
    public bool isLetter;          // the previous viewer's letter in the front office: reading it starts the objectives
    public override string Prompt => "Read";
    public override void Tap()
    {
        InteractAnim.Play(InteractAnim.Kind.Lift, transform);
        if (Hud.Instance) Hud.Instance.ShowPaper(title, Game.Expand(text), handwritten);
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("cloth"), transform.position, 0.15f, 1.4f, 0.5f, 5f);
        if (Game.Instance) { Game.Instance.Log("read " + title); if (isLetter) Game.Instance.LetterRead(); }
    }
}
