using UnityEngine;

// Looks for something usable straight ahead, shows its prompt on the OSD and uses it with E (or left click).
// Doors are grabbed instead: hold E / the mouse button and move the mouse forward to push, back to pull.
// A quick tap still gives the door a shove. Walking away while holding pulls the door after you.
// Loose objects are carried in front of the camera: E puts them down, right mouse button or G throws.
public class Interactor : MonoBehaviour
{
    public PlayerMotor player;
    public float reach = 2.3f;
    public LayerMask mask = ~(1 << 9 | 1 << 2);

    public Interactable Current { get; private set; }
    public static bool HoldingDoor { get; private set; }

    HingeDoor held;
    static Interactor instance;
    Carryable carried;
    Collider[] carriedColliders;
    float carriedSince;
    public static bool Carrying => instance && instance.carried;

    void Awake() => instance = this;

    public static void Pick(Carryable c)
    {
        if (!instance || instance.carried || !c) return;
        instance.carried = c;
        instance.carriedSince = Time.time;
        instance.carriedColliders = c.GetComponents<Collider>();
        var body = instance.player ? instance.player.GetComponent<Collider>() : null;
        if (body) foreach (var cc in instance.carriedColliders) Physics.IgnoreCollision(body, cc, true);
        c.Hold(true);
    }

    void Release(Vector3 velocity, bool thrown)
    {
        var c = carried;
        carried = null;
        if (!c) return;
        if (thrown) c.Throw(velocity); else { c.Hold(false); c.GetComponent<Rigidbody>().linearVelocity = velocity; }
        var body = player ? player.GetComponent<Collider>() : null;
        if (body) foreach (var cc in carriedColliders) if (cc) Physics.IgnoreCollision(body, cc, false);
    }

    void FixedUpdate()
    {
        if (!carried) return;
        Vector3 hold = transform.position + transform.forward * 0.55f - transform.up * 0.18f + transform.right * 0.12f;
        carried.MoveTowards(hold, Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized + Vector3.forward * 1e-4f));
        // dropped if it gets stuck behind something (once it has had time to come to hand from arm's length)
        if (Time.time - carriedSince > 0.6f && Vector3.Distance(carried.transform.position, hold) > 1.4f) Release(Vector3.zero, false);
    }
    float holdStart, dragged;
    Vector3 grabOffset;

    void Update()
    {
        bool down = Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0);
        bool hold = Input.GetKey(KeyCode.E) || Input.GetMouseButton(0);
        if (held)
        {
            UpdateHold(hold);
            return;
        }
        if (carried)
        {
            Current = null;
            if (Cursor.lockState != CursorLockMode.Locked) return;
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.G))
                Release(transform.forward * 7.5f + Vector3.up * 1.2f + (player ? player.Velocity : Vector3.zero), true);
            else if (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0))
                Release(player ? player.Velocity * 0.5f : Vector3.zero, false);
            return;
        }
        Current = null;
        var ray = new Ray(transform.position, transform.forward);
        if (Physics.SphereCast(ray, 0.06f, out var hit, reach, mask, QueryTriggerInteraction.Collide))
            Current = hit.collider.GetComponentInParent<Interactable>();
        if (Current && !Current.CanUse) Current = null;
        if (Cursor.lockState != CursorLockMode.Locked || !Current) return;
        Current.WhileLooking(player);
        if (!down) return;
        if (Current is HingeDoor door)
        {
            held = door;
            door.Held = true;
            holdStart = Time.time;
            dragged = 0f;
            grabOffset = door.transform.InverseTransformPoint(hit.point);
            HoldingDoor = true;
        }
        else Current.Use(player);
    }

    void UpdateHold(bool hold)
    {
        Vector3 grab = held.transform.TransformPoint(grabOffset);
        bool tooFar = Vector3.Distance(transform.position, grab) > reach + 0.8f;
        if (!hold || tooFar)
        {
            // a short tap without dragging is a shove
            if (!tooFar && Time.time - holdStart < 0.25f && dragged < 0.15f) held.Use(player);
            held.Held = false;
            held = null;
            HoldingDoor = false;
            return;
        }
        float push = Input.GetAxis("Mouse Y") * 0.35f;
        // walking while holding: moving away pulls the leaf with you, moving towards it pushes
        Vector3 toGrab = grab - transform.position;
        toGrab.y = 0f;
        if (toGrab.sqrMagnitude > 1e-4f) push += Vector3.Dot(player.DesiredVelocity, toGrab.normalized) * 0.25f;
        dragged += Mathf.Abs(push);
        held.Drag(player, Mathf.Clamp(push, -1.2f, 1.2f));
    }
}
