using UnityEngine;

// A physically swinging door leaf (object origin on the hinge). The leaf has angular momentum and hinge friction:
// it coasts after a push and stays wherever it stops; nothing snaps it open or shut.
//   tap E / click      a firm shove away from you (or towards shut if it is open)
//   hold E / mouse     grab it: move the mouse forward to push it away, back to pull it towards you
//   walk into it       the door stays still
//   run into it        a shut door bursts open and bangs against its stop: loud, and the monster hears it
// It creaks while it moves (louder the faster), latches with a thud when it arrives shut, thumps when it hits its
// stop. The acoustics read the actual gap the leaf leaves (see Openness), so a door ajar lets through a little,
// muffled sound, and the sound gets clearer and fuller as the gap widens.
public class HingeDoor : Interactable
{
    public string portalName;
    public bool locked;
    public bool metal;
    public float openAngle = 100f;
    public float startOpen;

    Quaternion closedRot;
    Vector3 leafDir;
    float angle, angVel;
    bool latched = true;
    float lastCreak, lastThump;
    Rigidbody body;
    float width = 0.9f;

    const float Friction = 2.2f;        // hinge damping (1/s): a firm push travels ~90 degrees
    const float AirDrag = 0.6f;            // extra drag at speed (air, worn hinge)

    public bool Held { get; set; }
    public float Angle => angle;

    // acoustic aperture: the free gap at the swinging edge grows like (1 - cos) for small angles and is the full
    // doorway at 90 degrees: a door 10 degrees ajar passes far less than one 45 degrees open
    public float Openness
    {
        get
        {
            float a = Mathf.Min(Mathf.Abs(angle), 90f) * Mathf.Deg2Rad;
            return Mathf.Clamp01(0.75f * (1f - Mathf.Cos(a)) + 0.25f * Mathf.Sin(a));
        }
    }

    public override string Prompt => locked ? "LOCKED" : "HOLD E TO MOVE DOOR  (TAP: " + (Mathf.Abs(angle) > 15f ? "CLOSE" : "OPEN") + ")";

    void Awake()
    {
        closedRot = transform.rotation;
        var r = GetComponentInChildren<Renderer>();
        Vector3 c = r ? r.bounds.center : transform.position + transform.right;
        leafDir = c - transform.position;
        leafDir.y = 0f;
        width = Mathf.Max(0.5f, leafDir.magnitude * 2f);
        if (leafDir.sqrMagnitude < 1e-4f) leafDir = transform.right;
        leafDir.Normalize();
        body = GetComponent<Rigidbody>();
        if (startOpen > 0.01f && !locked)
        {
            angle = openAngle * startOpen * (Random.value < 0.5f ? 1f : -1f);
            latched = false;
            Apply();
        }
    }

    Vector3 HandlePos => transform.position + transform.rotation * Quaternion.Inverse(closedRot) * leafDir * (width * 0.85f) + Vector3.up * 1.0f;

    // sign of rotation that moves the leaf away from point p
    float AwayFrom(Vector3 p)
    {
        Vector3 tip = transform.position + Quaternion.AngleAxis(angle, Vector3.up) * leafDir * width;
        Vector3 tipPlus = transform.position + Quaternion.AngleAxis(angle + 5f, Vector3.up) * leafDir * width;
        return (tipPlus - p).sqrMagnitude > (tip - p).sqrMagnitude ? 1f : -1f;
    }

    void Unlatch(float dir)
    {
        if (!latched) return;
        latched = false;
        OneShot(metal ? "door_unlatch_metal" : "door_unlatch", HandlePos, 0.5f, 1f, 0.5f, 12f);
        angle = 1.5f * dir;
    }

    public override void Use(PlayerMotor player)
    {
        if (locked)
        {
            OneShot("door_locked", HandlePos, 0.8f);
            NoiseBus.Emit(HandlePos, 0.8f, true);
            return;
        }
        if (Mathf.Abs(angle) > 15f)
            angVel = -Mathf.Sign(angle) * 160f;                       // swing it back towards shut
        else
        {
            float dir = player ? AwayFrom(player.transform.position) : 1f;
            Unlatch(dir);
            angVel = dir * 190f;
        }
    }

    // while held: mouse forward pushes the leaf away from the player, back pulls it towards them
    public void Drag(PlayerMotor player, float push)
    {
        if (locked)
        {
            if (Mathf.Abs(push) > 0.2f && Time.time - lastThump > 0.6f)
            {
                lastThump = Time.time;
                OneShot("door_locked", HandlePos, 0.6f);
            }
            return;
        }
        float dir = player ? AwayFrom(player.transform.position) : 1f;
        if (latched)
        {
            if (Mathf.Abs(push) < 0.05f) return;
            Unlatch(dir * Mathf.Sign(push));
        }
        float targetVel = dir * push * 260f;
        angVel = Mathf.Lerp(angVel, targetVel, 0.35f);
    }

    // Only a running player impact can shove the leaf.
    public void Shove(Vector3 at, Vector3 velocity, bool running)
    {
        if (!running || Held) return;
        if (locked)
        {
            if (Time.time - lastThump > 0.8f)
            {
                lastThump = Time.time;
                OneShot(metal ? "door_close_metal" : "door_close", HandlePos, 0.7f, 1f);
                NoiseBus.Emit(HandlePos, 2.5f, true);
            }
            return;
        }
        float dir = AwayFrom(at - velocity.normalized * 0.5f);
        float lever = Mathf.Clamp(Vector3.Distance(Vector3.ProjectOnPlane(at - transform.position, Vector3.up), Vector3.zero), 0.2f, width);
        float push = Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude;
        if (push < 0.2f) return;
        if (!running) return;                                     // walking into a door does not move it
        bool burst = latched && running && push > 2.8f;
        Unlatch(dir);
        float w = push / lever * Mathf.Rad2Deg;                    // tangential speed at the contact point
        if (burst)
        {
            angVel = dir * Mathf.Max(w * 1.6f, 420f);
            OneShot(metal ? "door_close_metal" : "door_close", HandlePos, 1f, 1f, 1f, 35f);
            NoiseBus.Emit(HandlePos, 4.5f, true);
        }
        else if (Mathf.Sign(angVel) != dir || Mathf.Abs(angVel) < w) angVel = dir * w * 1.05f;
    }

    // test / script helper: give the leaf a swing towards shut at `speed` degrees per second
    public void SwingTowardsShut(float speed)
    {
        if (locked || latched) return;
        angVel = -Mathf.Sign(angle) * speed;
    }

    // a draught or someone swings it shut
    public void Slam()
    {
        if (locked || latched) return;
        angVel = -Mathf.Sign(angle) * 380f;
    }

    // something coming through from `from` (the monster)
    public void OpenAway(Vector3 from)
    {
        if (locked) return;
        if (Openness > 0.85f) return;
        // Continue an existing opening; recomputing away at the swung tip can reverse an already open leaf.
        float dir = Mathf.Abs(angle)>5f ? Mathf.Sign(angle) : AwayFrom(from);
        Unlatch(dir);
        if (Mathf.Abs(angVel) < 120f) angVel = dir * 200f;
        NoiseBus.Emit(HandlePos, 1.2f, false);
    }

    // wind, the elevator, a script
    public void Nudge(float degrees)
    {
        if (locked || latched) return;
        angVel += degrees * 2.2f;
    }

    void FixedUpdate()
    {
        if (latched && Mathf.Abs(angVel) < 1f) return;
        float dt = Time.fixedDeltaTime;
        float prev = angle;
        // hinge friction + speed-dependent drag
        angVel -= angVel * (Friction + AirDrag * Mathf.Abs(angVel) / 100f) * dt;
        if (!Held && Mathf.Abs(angVel) < 2f) angVel = 0f;           // static friction: it stays where it stopped
        angle += angVel * dt;

        // stops at the wall on either side
        if (Mathf.Abs(angle) > openAngle)
        {
            float impact = Mathf.Abs(angVel);
            angle = Mathf.Sign(angle) * openAngle;
            angVel = -angVel * 0.25f;
            if (impact > 60f && Time.time - lastThump > 0.3f)
            {
                lastThump = Time.time;
                float v = Mathf.Clamp01(impact / 400f);
                OneShot(metal ? "door_close_metal" : "door_close", HandlePos, 0.3f + v, 1f, 1f, 35f);
                NoiseBus.Emit(HandlePos, 1f + 3f * v, false);
            }
        }
        // arriving shut: latch with a thud (or bounce off the frame if it came in too fast and crooked)
        bool closing = angVel * prev < 0f;
        if (closing && (Mathf.Sign(prev) != Mathf.Sign(angle) || Mathf.Abs(angle) < 0.6f))
        {
            {
                float impact = Mathf.Abs(angVel);
                angle = 0f;
                angVel = 0f;
                latched = true;
                Held = false;
                OneShot(metal ? "door_close_metal" : "door_close", HandlePos, Mathf.Clamp(0.35f + impact / 300f, 0.35f, 1f), 1f);
                NoiseBus.Emit(HandlePos, 0.8f + impact / 120f, false);
            }
        }
        // creak while moving, louder and higher when fast
        float spd = Mathf.Abs(angVel);
        if (spd > 25f && Time.time - lastCreak > Mathf.Lerp(3.2f, 1.7f, Mathf.Clamp01(spd / 300f)))
        {
            lastCreak = Time.time;
            OneShot(metal ? "door_creak_metal" : "door_creak", HandlePos, Mathf.Clamp(spd / 1600f, 0.025f, 0.18f),
                    1f);
        }
        Apply();
    }

    void Apply()
    {
        var rot = Quaternion.AngleAxis(angle, Vector3.up) * closedRot;
        if (body) body.MoveRotation(rot);
        else transform.rotation = rot;
    }

    // the player bumping into the leaf
    void OnCollisionStay(Collision c)
    {
        var pm = c.rigidbody ? c.rigidbody.GetComponent<PlayerMotor>() : null;
        if (!pm || c.contactCount == 0) return;
        var desired = pm.DesiredVelocity;
        if (desired.sqrMagnitude < 0.04f) return;
        var cp = c.GetContact(0);
        // only when walking into the leaf, not along it
        if (Vector3.Dot(desired.normalized, -cp.normal) < 0.3f && Vector3.Dot(desired.normalized, cp.normal) < 0.3f) return;
        Shove(cp.point, desired, pm.Running);
    }
}
