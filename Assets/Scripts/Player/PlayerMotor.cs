using UnityEngine;

// First-person movement: walk, crouch, run (8 s of stamina), lean (Q/R or your head via the webcam), mouse look.
// The camera is a handheld camcorder, ported from the earlier HotelGame's VHSCamera: the view follows the mouse
// through an under-damped spring (inertia, slight overshoot); every stride jolts it (bounce, sway, roll) in step
// with the footsteps; landing compresses it; it sways with your breathing (more after running); it rolls into
// strafes and fast turns; constant small handheld jitter, plus trauma shake (banging, the breaker).
[RequireComponent(typeof(CharacterController))]
public class PlayerMotor : MonoBehaviour
{
    public static PlayerMotor Instance { get; private set; }
    public Transform cameraTransform;
    public float walkSpeed = 1.6f, crouchSpeed = 0.8f, runSpeed = 3.8f;
    // the playthrough bot (PlayBot) drives the same movement a player does: a move vector instead of WASD, its own look
    public static bool BotDriven;
    public static Vector2 BotMove;
    public static bool BotRun;
    public static bool BotCrouch;
    // crouching (stalking) is silent but tiring: it uses stamina, less than running, and less still standing still.
    // Out of stamina you straighten up (if there is room above you) and your steps are heard again
    public const float CrouchMoveCost = 0.5f, CrouchStillCost = 0.2f;
    public float staminaMax = 8f, staminaRecover = 9f;

    [Header("Handheld camera")]
    public float rotationStiffness = 240f;
    [Range(0.3f, 1.2f)] public float rotationDampingRatio = 0.68f;
    public float bobVertical = 0.022f, bobLateral = 0.012f, bobRoll = 0.4f, bobPitch = 0.3f;
    public float strafeRoll = 0.5f, turnRoll = 0.012f, jitter = 0.22f, breathing = 0.35f;

    public event System.Action<int, float> Step;
    public event System.Action<float> Landed;

    public float HorizontalSpeed { get; private set; }
    public bool Running { get; private set; }
    public bool Crouching { get; private set; }
    public bool Silent => Crouching && !exhausted;          // crouched with stamina left: the listener hears no steps
    public bool Exhausted => exhausted;
    public float Exertion { get; private set; }
    public bool IsGrounded => cc.isGrounded || groundedRay;
    public RaycastHit GroundHit { get; private set; }
    public float Stamina { get; private set; }
    public bool Locked { get; set; }            // menus, hiding, cutscenes
    public Transform HideView;                  // inside a hiding place: the eye sits here
    public float HideLookSpeed { get; private set; }   // how fast you look around inside (rustle)
    public float Yaw => yaw;
    public float Pitch => pitch;
    public float StepPhase => (foot + Mathf.Clamp01(stride / Mathf.Max(0.01f, strideLen))) * Mathf.PI;

    CharacterController cc;
    float yaw, pitch, vy, stride, strideLen = 0.72f, lastRun = -10f, leanNow, eyeH = 1.6f, airTime;
    float hideYaw, hidePitch;
    int foot;
    bool exhausted, groundedRay;
    Vector3 vel;
    // handheld state
    float smYaw, smPitch, yawVel, pitchVel, bobWeight, landOffset, landVel, roll, trauma, noiseSeed, smY, runBob = 1f;
    bool hasY;

    void Awake()
    {
        Instance = this;
        cc = GetComponent<CharacterController>();
        Stamina = staminaMax;
        noiseSeed = Random.value * 100f;
        Landed += i => { landVel -= Mathf.Min(i * 0.45f, 0.4f); trauma = Mathf.Max(trauma, Mathf.Min(i * 0.12f, 0.12f)); };
    }

    public void Shake(float amount) => trauma = Mathf.Clamp01(trauma + amount * 0.6f);

    public void Teleport(Vector3 pos, float yawDeg, float pitchDeg = 0f)
    {
        cc.enabled = false;
        transform.position = pos;
        cc.enabled = true;
        yaw = smYaw = yawDeg;
        pitch = smPitch = pitchDeg;
        yawVel = pitchVel = 0f;
        vy = 0f;
        vel = Vector3.zero;
        hasY = false;
        transform.rotation = Quaternion.Euler(0, yaw, 0);
        ApplyCamera(0f);
    }

    public void SetLook(float yawDeg, float pitchDeg) { yaw = smYaw = yawDeg; pitch = smPitch = pitchDeg; yawVel = pitchVel = 0f; transform.rotation = Quaternion.Euler(0, yaw, 0); }

    static float HeadLean => Settings.UseCamera && Settings.HeadLean && FaceTracker.Instance && FaceTracker.Instance.Running && FaceTracker.Instance.FacePresent ? FaceTracker.Instance.Lean : 0f;

    void Update()
    {
        float dt = Time.deltaTime;
        if (HideView)
        {
            // peek out of the hiding place: look around a little (and lean with your head), no movement
            float before = hideYaw + hidePitch;
            if (!Game.InMenu)
            {
                float s2 = 2.2f * Settings.Sensitivity;
                hideYaw = Mathf.Clamp(hideYaw + Input.GetAxisRaw("Mouse X") * s2, -45f, 45f);
                hidePitch = Mathf.Clamp(hidePitch - Input.GetAxisRaw("Mouse Y") * s2, -30f, 30f);
            }
            HideLookSpeed = Mathf.Lerp(HideLookSpeed, Mathf.Abs(hideYaw + hidePitch - before) / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt * 8f));
            float lean = HeadLean;
            leanNow = Mathf.MoveTowards(leanNow, lean, dt * 3f);
            cameraTransform.position = HideView.position + HideView.right * leanNow * 0.12f;
            cameraTransform.rotation = HideView.rotation * Quaternion.Euler(hidePitch, hideYaw, -leanNow * 4f);
            HorizontalSpeed = 0f; Running = false;
            return;
        }
        hideYaw = hidePitch = 0f;
        HideLookSpeed = 0f;
        if (Locked)
        {
            HorizontalSpeed = 0f; Running = false;
            ApplyCamera(dt);
            return;
        }
        bool input = !Game.InMenu;
        if (input && !Interactor.MouseCaptured && !BotDriven)
        {
            float s = 2.2f * Settings.Sensitivity;
            yaw += Input.GetAxisRaw("Mouse X") * s;
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * s, -80f, 80f);
        }
        transform.rotation = Quaternion.Euler(0, yaw, 0);

        Vector2 move = Vector2.zero;
        if (input)
        {
            move = BotDriven ? BotMove : new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                               (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            if (move.sqrMagnitude > 1f) move.Normalize();
        }
        bool wantCrouch = input && (BotDriven ? BotCrouch : Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.LeftControl));
        // no room to stand up (under something low): you stay down, tired or not
        bool lowCeiling = cc.height < 1.7f && Physics.SphereCast(transform.position + Vector3.up * 0.5f, cc.radius * 0.9f, Vector3.up, out _, 1.0f, Layers.Blockers, QueryTriggerInteraction.Ignore);
        Crouching = (wantCrouch && !exhausted) || lowCeiling;
        bool wantRun = input && !Crouching && (BotDriven ? BotRun : Input.GetKey(KeyCode.LeftShift)) && move.y > 0.1f && !exhausted;
        Running = wantRun && Stamina > 0f;
        if (Running) { Stamina -= dt; lastRun = Time.time; if (Stamina <= 0f) { Stamina = 0f; exhausted = true; } }
        else if (Crouching && !exhausted)
        {
            Stamina -= dt * (HorizontalSpeed > 0.2f ? CrouchMoveCost : CrouchStillCost);
            lastRun = Time.time;
            if (Stamina <= 0f) { Stamina = 0f; exhausted = true; }
        }
        else if (Time.time - lastRun > 1.5f) Stamina = Mathf.Min(staminaMax, Stamina + staminaMax / staminaRecover * dt);
        if (exhausted && Stamina >= staminaMax * 0.4f) exhausted = false;
        Exertion = Running ? Mathf.Min(1f, Exertion + dt / 5f) : Mathf.Max(0f, Exertion - dt / 14f);

        float speed = Crouching ? crouchSpeed : Running ? runSpeed : walkSpeed;
        Vector3 wish = (transform.right * move.x + transform.forward * move.y) * speed;
        vel = Vector3.MoveTowards(vel, wish, (wish.sqrMagnitude > 0.01f ? 14f : 18f) * dt);
        if (cc.isGrounded && vy < 0f) vy = -2f;
        vy += Physics.gravity.y * dt;
        bool wasGrounded = IsGrounded;
        if (cc.enabled) cc.Move((vel + Vector3.up * vy) * dt);
        HorizontalSpeed = new Vector2(cc.velocity.x, cc.velocity.z).magnitude;

        groundedRay = Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out var hit, 0.6f, ~((1 << 8) | (1 << 9) | (1 << 15) | (1 << 14)), QueryTriggerInteraction.Ignore);
        if (groundedRay) GroundHit = hit;
        if (!IsGrounded) airTime += dt;
        else
        {
            if (!wasGrounded && airTime > 0.35f) Landed?.Invoke(Mathf.Clamp01(airTime - 0.3f));
            airTime = 0f;
        }

        // footsteps by distance travelled
        strideLen = Running ? 1.15f : Crouching ? 0.55f : 0.72f;
        if (IsGrounded && HorizontalSpeed > 0.3f)
        {
            stride += HorizontalSpeed * dt;
            if (stride >= strideLen)
            {
                stride = 0f;
                foot ^= 1;
                Step?.Invoke(foot, Running ? 1f : Crouching ? 0.25f : 0.6f);
            }
        }

        float targetH = Crouching ? 1.1f : 1.75f;
        cc.height = Mathf.MoveTowards(cc.height, targetH, dt * 3f);
        cc.center = Vector3.up * cc.height * 0.5f;
        eyeH = Mathf.MoveTowards(eyeH, Crouching ? 1.0f : 1.6f, dt * 2.5f);
        ApplyCamera(dt);
    }

    static void Spring(ref float x, ref float v, float target, float k, float zeta, float dt)
    {
        float omega = Mathf.Sqrt(k);
        int steps = Mathf.CeilToInt(dt / 0.008f);
        float h = dt / Mathf.Max(steps, 1);
        for (int i = 0; i < steps; i++)
        {
            float a = k * (target - x) - 2f * zeta * omega * v;
            v += a * h;
            x += v * h;
        }
    }

    void ApplyCamera(float dt)
    {
        if (!cameraTransform) return;
        // lean: Q / R (Tab) or your head
        float want = 0f;
        if (!Locked && !Game.InMenu)
        {
            if (Input.GetKey(KeyCode.Q)) want -= 1f;
            if (Input.GetKey(KeyCode.Tab) || Input.GetKey(KeyCode.R)) want += 1f;
            want = Mathf.Clamp(want + HeadLean, -1f, 1f);
        }
        float maxLean = 0.35f;
        var side = transform.right * Mathf.Sign(want);
        if (Mathf.Abs(want) > 0.01f && Physics.SphereCast(transform.position + Vector3.up * eyeH, 0.15f, side, out var h, maxLean + 0.1f, (1 << 10) | (1 << 11) | (1 << 12), QueryTriggerInteraction.Ignore))
            want *= Mathf.Clamp01((h.distance - 0.1f) / maxLean);
        leanNow = dt <= 0f ? want : Mathf.MoveTowards(leanNow, want, dt * 3f);

        if (dt <= 0f) { smYaw = yaw; smPitch = pitch; }
        Spring(ref smYaw, ref yawVel, yaw, rotationStiffness, rotationDampingRatio, dt);
        Spring(ref smPitch, ref pitchVel, pitch, rotationStiffness, rotationDampingRatio, dt);

        Vector3 v = cc ? cc.velocity : Vector3.zero;
        bobWeight = Mathf.MoveTowards(bobWeight, IsGrounded && !Locked ? Mathf.Clamp01(HorizontalSpeed / walkSpeed) : 0f, dt * 8f);
        Spring(ref landOffset, ref landVel, 0f, 140f, 0.42f, dt);

        float t = Time.time;
        float exert = Breath.Holding ? 0f : Exertion;
        float breathAmp = breathing * (1f + 0.25f * exert) * (Breath.Holding ? 0.25f : 1f);
        float breath = Mathf.Sin(t * (1.4f + 1.6f * exert)) * breathAmp;

        Vector3 right = Quaternion.Euler(0f, smYaw, 0f) * Vector3.right;
        float sideways = Vector3.Dot(v, right);
        float targetRoll = Mathf.Clamp(-sideways * strafeRoll * (Running ? 0.5f : 1f) - yawVel * turnRoll, -7f, 7f);
        roll = Mathf.Lerp(roll, targetRoll, 1f - Mathf.Exp(-Mathf.Max(dt, 0f) * 6f));

        // running strides come fast (3.3 a second): a full-size bob at that rate reads as shaking, so it's kept small
        runBob = Mathf.MoveTowards(runBob, Running ? 0.4f : Crouching ? 0.6f : 1f, dt * 3f);
        float run = runBob;
        float amp = bobWeight * run;
        float ph = StepPhase;
        float s = Mathf.Sin(ph), c = Mathf.Cos(ph);
        float bobY = -Mathf.Abs(s) * bobVertical * amp + bobVertical * amp * 0.5f;
        float bobX = c * bobLateral * amp;
        float bobR = c * bobRoll * amp;
        float bobP = (Mathf.Abs(s) - 0.5f) * bobPitch * amp;

        trauma = Mathf.MoveTowards(trauma, 0f, Time.unscaledDeltaTime * 1.3f);
        float shake = Mathf.Min(trauma * trauma, 0.06f);
        float N(float o, float f) => (Mathf.PerlinNoise(noiseSeed + o, t * f) - 0.5f) * 2f;
        float jx = N(1f, 1.3f) * jitter + N(11f, 22f) * 6f * shake;
        float jy = N(2f, 1.1f) * jitter + N(12f, 22f) * 6f * shake;
        float jz = N(3f, 0.9f) * jitter * 0.6f + N(13f, 22f) * 4f * shake;
        Vector3 jPos = new Vector3(N(4f, 20f), N(5f, 20f), 0f) * 0.04f * shake;

        // body height is smoothed: ground-contact micro-bounces must not shake the picture
        float py = transform.position.y;
        smY = hasY && dt > 0f ? Mathf.Lerp(smY, py, 1f - Mathf.Exp(-dt / 0.06f)) : py;
        hasY = true;

        var look = Quaternion.Euler(smPitch + bobP + breath + jy + landOffset * -20f, smYaw - yaw + jx, roll + bobR + jz - leanNow * 7f);
        cameraTransform.localRotation = look;
        cameraTransform.localPosition = new Vector3(leanNow * maxLean + bobX, eyeH + (smY - py) + bobY + landOffset + breath * 0.004f, 0.05f) + look * jPos;
    }
}
