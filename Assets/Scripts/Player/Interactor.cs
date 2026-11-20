using UnityEngine;

// Looks for an Interactable under the crosshair. A short press of E / left click is a Tap; keeping it pressed is a Hold
// (ease a door, pull the dresser, throw the breaker, eject the tape). The mouse wheel nudges a door.
// Holding E on a door runs DoorEase: keep its pointer in the band with the arrow keys or the door slips.
public class Interactor : MonoBehaviour
{
    public static Interactor Instance { get; private set; }
    public static bool MouseCaptured { get; private set; }
    public Camera cam;
    public float reach = 2.1f;
    public Interactable Current { get; private set; }
    public Interactable Held { get; private set; }

    float pressTime = -1f;
    bool holding;
    const int Mask = ~((1 << 8) | (1 << 9) | (1 << 15) | (1 << 16));

    void Awake() => Instance = this;

    // after a paper closes: ignore E / the button until it is let go
    static bool blocked;
    public static void BlockUntilRelease() { blocked = true; }

    public static bool PressDown => Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0);
    public static bool PressHeld => Input.GetKey(KeyCode.E) || Input.GetMouseButton(0);

    void Update()
    {
        if (Game.InMenu || (PlayerMotor.Instance && PlayerMotor.Instance.Locked && !HideSpot.Inside))
        {
            Drop();
            Current = null;
            return;
        }
        if (blocked)
        {
            if (PressHeld) { Drop(); Current = HideSpot.Inside ? HideSpot.Current : Find(); return; }
            blocked = false;
        }
        if (Held == null) Current = HideSpot.Inside ? HideSpot.Current : Find();
        // mouse wheel eases the door you are looking at a little open or shut
        float wheel = Input.mouseScrollDelta.y;
        if (Held == null && Mathf.Abs(wheel) > 0.01f && Current is HingeDoor hd) hd.Wheel(Mathf.Sign(wheel));
        if (PressDown && Current && Current.Usable)
        {
            pressTime = Time.time;
            Held = Current;
            holding = false;
        }
        if (Held)
        {
            if (PressHeld)
            {
                if (!holding && Time.time - pressTime > 0.22f) holding = true;
                if (holding)
                {
                    MouseCaptured = Held.WantsMouse;
                    var ease = DoorEase.Instance;
                    if (Held is HingeDoor hd2 && hd2.CanEase && ease)
                    {
                        if (ease.Door != hd2) ease.Begin(hd2);
                        if (!ease.Tick(Time.deltaTime, DoorEase.ArrowInput)) { Held = null; holding = false; MouseCaptured = false; return; }   // it slipped
                        if (hd2.EaseLeft <= 0.005f) { Drop(); return; }                                                                  // all the way
                    }
                    else Held.Hold(Time.deltaTime, new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")));
                    // let go if you walk away from it
                    if (Held && !HideSpot.Inside && Find() != Held && !IsNear(Held)) Drop();
                }
            }
            else
            {
                if (!holding) Held.Tap();
                Drop();
            }
        }
    }

    bool IsNear(Interactable i) => i && Vector3.Distance(cam.transform.position, i.transform.position) < reach + 0.8f;

    void Drop()
    {
        if (DoorEase.Instance && DoorEase.Instance.Door && DoorEase.Instance.Door == Held) DoorEase.Instance.End();
        if (Held && holding) Held.Release();
        Held = null;
        holding = false;
        MouseCaptured = false;
    }

    Interactable Find()
    {
        if (!cam) return null;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out var hit, reach, Mask, QueryTriggerInteraction.Collide))
            return hit.collider.GetComponentInParent<Interactable>();
        return null;
    }
}
