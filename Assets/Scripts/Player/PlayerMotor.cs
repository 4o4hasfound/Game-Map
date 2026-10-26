using UnityEngine;

// Rigidbody walker: WASD relative to the camera, Shift to hurry, Ctrl/C to crouch (crawlspaces), stairs via
// invisible ramps. Raises a Step event every stride so the camera bob and the footstep sounds are in sync.
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class PlayerMotor : MonoBehaviour
{
    public float walkSpeed = 2.1f;
    public float runSpeed = 3.9f;
    public float crouchSpeed = 1.1f;
    public float acceleration = 22f;
    public float standHeight = 1.8f, crouchHeight = 1.05f;
    public float strideWalk = 0.72f, strideRun = 0.95f, strideCrouch = 0.5f;
    public LayerMask groundMask = ~(1 << 9 | 1 << 2);
    public Transform cameraTransform;

    public bool IsGrounded { get; private set; }
    public bool Crouching { get; private set; }
    public bool Running { get; private set; }
    public bool Frozen;
    public Seat SeatedOn { get; private set; }               // sitting: no walking, the head stays free to look
    public Vector3 DesiredVelocity { get; private set; }     // where the player is trying to go (doors use it)
    public Vector3 Velocity => rb ? rb.linearVelocity : Vector3.zero;
    public float HorizontalSpeed => new Vector2(Velocity.x, Velocity.z).magnitude;
    public float Exertion { get; private set; }       // rises while running, used for breathing
    public float StepPhase { get; private set; }      // radians, pi per step
    public float EyeHeight { get; private set; } = 1.62f;
    public RaycastHit GroundHit => groundHit;
    public event System.Action<int, float> Step;      // foot (0 left / 1 right), strength
    public event System.Action<float> Landed;

    Rigidbody rb;
    CapsuleCollider capsule;
    Vector3 input;
    Vector3 carry, carryAcc;
    RaycastHit groundHit;
    float stepDist;
    int foot;
    bool wasGrounded = true;
    float fallSpeed;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        SetHeight(standHeight);
    }

    void SetHeight(float h)
    {
        capsule.height = h;
        capsule.center = new Vector3(0f, h * 0.5f, 0f);
    }

    public void AddCarry(Vector3 v) => carryAcc = v;

    public void Teleport(Vector3 pos, float yawDelta)
    {
        rb.position = pos;
        transform.position = pos;
        if (!rb.isKinematic) rb.linearVelocity = Quaternion.Euler(0, yawDelta, 0) * rb.linearVelocity;
        var cam = cameraTransform ? cameraTransform.GetComponent<VHSCamera>() : null;
        if (cam) cam.AddYaw(yawDelta);
    }

    void Update()
    {
        input = Vector3.zero;
        if (SeatedOn)
        {
            // any walking key gets you up again (E on the seat does too, see Seat)
            bool move = Cursor.lockState == CursorLockMode.Locked &&
                        (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f || Input.GetKeyDown(KeyCode.Space));
            if (move) StandUp();
        }
        if (!Frozen && !SeatedOn && Cursor.lockState == CursorLockMode.Locked)
        {
            float h = Input.GetAxisRaw("Horizontal"), v = Input.GetAxisRaw("Vertical");
            Vector3 fwd = cameraTransform ? cameraTransform.forward : transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            input = Vector3.ClampMagnitude(fwd * v + right * h, 1f);
        }
        bool wantCrouch = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
        if (!wantCrouch && Crouching)
        {
            // only stand up if there is headroom
            Vector3 p = transform.position + Vector3.up * (crouchHeight - 0.1f);
            if (Physics.SphereCast(p, capsule.radius * 0.9f, Vector3.up, out _, standHeight - crouchHeight + 0.05f, groundMask, QueryTriggerInteraction.Ignore))
                wantCrouch = true;
        }
        if (wantCrouch != Crouching)
        {
            Crouching = wantCrouch;
            SetHeight(Crouching ? crouchHeight : standHeight);
        }
        Running = !Crouching && Input.GetKey(KeyCode.LeftShift) && input.sqrMagnitude > 0.1f;
        // builds up over ~11 s of running; recovery is exponential (fast at first, then slower), slower while walking
        if (Running) Exertion = Mathf.MoveTowards(Exertion, 1f, Time.deltaTime * 0.09f);
        else
        {
            float tau = input.sqrMagnitude > 0.1f ? 15f : 9f;
            Exertion = Mathf.Max(0f, Exertion - (Exertion / tau + 0.004f) * Time.deltaTime);
        }
        float eye = SeatedOn ? SeatedOn.height + 0.74f : Crouching ? 0.95f : 1.62f;
        EyeHeight = Mathf.Lerp(EyeHeight, eye, 1f - Mathf.Exp(-Time.deltaTime * (SeatedOn ? 5f : 9f)));
    }

    // Sit: the body is placed in front of the seat and held still; the eyes come down to seated height.
    public void SitOn(Seat seat)
    {
        if (SeatedOn || !seat) return;
        SeatedOn = seat;
        Running = false;
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        Vector3 p = seat.transform.position;
        p.y = seat.FloorY;
        rb.position = p;
        transform.position = p;
        var cam = cameraTransform ? cameraTransform.GetComponent<VHSCamera>() : null;
        if (cam) cam.AddYaw(Mathf.DeltaAngle(cam.yaw, seat.transform.eulerAngles.y));
    }

    public void StandUp()
    {
        var seat = SeatedOn;
        if (!seat) return;
        // step out in front of the seat (or to either side) wherever the body fits
        Vector3 basePos = seat.transform.position;
        basePos.y = seat.FloorY + 0.03f;
        Vector3 fwd = seat.transform.forward, right = seat.transform.right;
        Vector3 stand = basePos + fwd * 0.55f;
        foreach (var off in new[] { fwd * 0.55f, fwd * 0.8f, right * 0.6f, -right * 0.6f, -fwd * 0.6f })
        {
            Vector3 c = basePos + off;
            if (!Physics.CheckCapsule(c + Vector3.up * (capsule.radius + 0.05f), c + Vector3.up * (standHeight - capsule.radius), capsule.radius * 0.95f,
                                      groundMask, QueryTriggerInteraction.Ignore)) { stand = c; break; }
        }
        rb.position = stand;
        transform.position = stand;
        rb.isKinematic = false;
        SeatedOn = null;
        seat.Vacated();
    }

    void FixedUpdate()
    {
        if (rb.isKinematic) return; // automated tours move the frozen body explicitly
        float r = capsule.radius * 0.9f;
        Vector3 origin = transform.position + Vector3.up * (r + 0.1f);
        IsGrounded = Physics.SphereCast(origin, r, Vector3.down, out groundHit, 0.25f, groundMask, QueryTriggerInteraction.Ignore);

        float speed = Crouching ? crouchSpeed : Running ? runSpeed : walkSpeed;
        Vector3 target = input * speed;
        DesiredVelocity = target;
        carry = Vector3.MoveTowards(carry, carryAcc, Time.fixedDeltaTime * 3f);
        carryAcc = Vector3.zero;
        Vector3 vel = rb.linearVelocity;
        Vector3 hv = new Vector3(vel.x - carry.x, 0f, vel.z - carry.z);
        hv = Vector3.MoveTowards(hv, target, acceleration * (IsGrounded ? 1f : 0.3f) * Time.fixedDeltaTime);
        bool idle = input.sqrMagnitude < 0.01f && carry.sqrMagnitude < 1e-4f;
        if (IsGrounded)
        {
            // walk along the slope (ramps under the stairs), never get launched off the top or slide down
            Vector3 along = Vector3.ProjectOnPlane(hv, groundHit.normal);
            if (along.sqrMagnitude > 1e-6f) along = along.normalized * hv.magnitude;
            vel = along + carry;
            if (idle) vel.y = 0f;
            rb.useGravity = !idle;
        }
        else
        {
            rb.useGravity = true;
            vel.x = hv.x + carry.x;
            vel.z = hv.z + carry.z;
        }
        rb.linearVelocity = vel;

        // strides
        float hs = new Vector2(hv.x, hv.z).magnitude;
        if (!IsGrounded) fallSpeed = Mathf.Max(fallSpeed, -vel.y);
        if (IsGrounded && !wasGrounded)
        {
            if (fallSpeed > 2.5f) Landed?.Invoke(Mathf.Clamp01(fallSpeed / 8f));
            fallSpeed = 0f;
        }
        wasGrounded = IsGrounded;
        if (IsGrounded && hs > 0.3f)
        {
            float stride = Crouching ? strideCrouch : Running ? strideRun : strideWalk;
            stepDist += hs * Time.fixedDeltaTime;
            StepPhase = Mathf.Repeat(StepPhase + hs * Time.fixedDeltaTime / stride * Mathf.PI, Mathf.PI * 2f);
            if (stepDist >= stride)
            {
                stepDist -= stride;
                foot ^= 1;
                Step?.Invoke(foot, Mathf.Clamp01(hs / runSpeed) * (Crouching ? 0.45f : 1f) + (Running ? 0.25f : 0f));
            }
        }
    }
}
