using UnityEngine;

// Something the player can use. Tap = a short press of E / left click; Hold = keep it pressed (drag doors, pull the
// dresser, jiggle a lock, hold eject). While an interactable is held with WantsMouse, the mouse drives it, not the view.
public abstract class Interactable : MonoBehaviour
{
    public virtual string Prompt => "Use";
    public virtual bool Usable => true;
    public virtual bool WantsMouse => false;
    public virtual void Tap() { }
    public virtual void Hold(float dt, Vector2 mouse) { }
    public virtual void Release() { }
    public virtual float HoldProgress => -1f;    // 0..1 shows a progress ring, <0 none
}
