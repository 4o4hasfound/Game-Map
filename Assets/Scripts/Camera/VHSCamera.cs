using UnityEngine;

// First-person handheld camcorder, built on the HW1 body-cam model:
//  - mouse look drives the body; the camera follows through an under-damped spring (inertia, slight overshoot)
//  - every stride jolts the camera (vertical bounce, lateral sway, roll), synced with the footstep sounds
//  - landing compresses the camera on a spring
//  - slow breathing sway (stronger after running), roll into strafes and fast turns
//  - constant small handheld jitter, plus trauma shake (elevator, slams)
public class VHSCamera : MonoBehaviour
{
    public PlayerMotor player;
    public Vector3 mountOffset = new Vector3(0.04f, 0f, 0.08f);

    [Header("Look")]
    public float sensitivity = 2.0f;
    public float minPitch = -80f, maxPitch = 80f;
    public float yaw, pitch;
    public float rotationStiffness = 240f;
    [Range(0.3f, 1.2f)] public float rotationDampingRatio = 0.68f;

    [Header("Walking")]
    public float bobVertical = 0.022f;
    public float bobLateral = 0.012f;
    public float bobRoll = 0.4f;
    public float bobPitch = 0.3f;

    [Header("Feel")]
    public float strafeRoll = 0.5f;
    public float turnRoll = 0.012f;
    public float jitter = 0.22f;
    public float breathing = 0.35f;

    float smYaw, smPitch, yawVel, pitchVel;
    float bobWeight;
    float landOffset, landVel;
    float roll, trauma, noiseSeed;
    float rumble;
    float smY;
    bool hasY;

    public void Shake(float amount) => trauma = Mathf.Clamp01(trauma + amount * 0.6f);
    public void SetRumble(float amount) => rumble = amount;

    public void AddYaw(float d)
    {
        yaw += d;
        smYaw += d;
    }

    void Start()
    {
        smYaw = yaw;
        smPitch = pitch;
        noiseSeed = Random.value * 100f;
        if (player) player.Landed += i => { landVel -= Mathf.Min(i * 0.45f, 0.4f); trauma = Mathf.Max(trauma, Mathf.Min(i * 0.12f, 0.12f)); };
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

    void LateUpdate()
    {
        if (!player) return;
        float dt = Time.deltaTime;
        if (Cursor.lockState == CursorLockMode.Locked && !player.Frozen)
        {
            yaw += Input.GetAxis("Mouse X") * sensitivity;
            if (!Interactor.HoldingDoor) pitch -= Input.GetAxis("Mouse Y") * sensitivity;
        }
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        Spring(ref smYaw, ref yawVel, yaw, rotationStiffness, rotationDampingRatio, dt);
        Spring(ref smPitch, ref pitchVel, pitch, rotationStiffness, rotationDampingRatio, dt);

        Vector3 vel = player.Velocity;
        float hs = player.HorizontalSpeed;
        bobWeight = Mathf.MoveTowards(bobWeight, player.IsGrounded ? Mathf.Clamp01(hs / player.walkSpeed) : 0f, dt * 8f);
        Spring(ref landOffset, ref landVel, 0f, 140f, 0.42f, dt);

        float t = Time.time;
        float breathAmp = breathing * (1f + 0.5f * player.Exertion);
        float breath = Mathf.Sin(t * (1.4f + 1.6f * player.Exertion)) * breathAmp;

        Vector3 right = Quaternion.Euler(0f, smYaw, 0f) * Vector3.right;
        float sideways = Vector3.Dot(vel, right);
        float targetRoll = Mathf.Clamp(-sideways * strafeRoll - yawVel * turnRoll, -7f, 7f);
        roll = Mathf.Lerp(roll, targetRoll, 1f - Mathf.Exp(-dt * 6f));

        float run = player.Running ? 0.9f : player.Crouching ? 0.6f : 1f;   // longer, faster strides: less bob per step
        float amp = bobWeight * run;
        float ph = player.StepPhase;
        float s = Mathf.Sin(ph), c = Mathf.Cos(ph);
        float bobY = -Mathf.Abs(s) * bobVertical * amp + bobVertical * amp * 0.5f;
        float bobX = c * bobLateral * amp;
        float bobR = c * bobRoll * amp;
        float bobP = (Mathf.Abs(s) - 0.5f) * bobPitch * amp;

        trauma = Mathf.MoveTowards(trauma, 0f, Time.unscaledDeltaTime * 1.3f);
        float shake = Mathf.Min(trauma * trauma + rumble * 0.035f, 0.06f);
        float N(float o, float f) => (Mathf.PerlinNoise(noiseSeed + o, t * f) - 0.5f) * 2f;
        float jx = N(1f, 1.3f) * jitter + N(11f, 22f) * 6f * shake;
        float jy = N(2f, 1.1f) * jitter + N(12f, 22f) * 6f * shake;
        float jz = N(3f, 0.9f) * jitter * 0.6f + N(13f, 22f) * 4f * shake;
        Vector3 jPos = new Vector3(N(4f, 20f), N(5f, 20f), 0f) * 0.04f * shake;

        Quaternion look = Quaternion.Euler(smPitch + bobP + breath + jy + landOffset * -20f, smYaw + jx, roll + bobR + jz);
        Quaternion body = Quaternion.Euler(0f, smYaw, 0f);
        // body height is smoothed: ground-contact micro-bounces at running speed must not shake the picture
        float py = player.transform.position.y;
        smY = hasY ? Mathf.Lerp(smY, py, 1f - Mathf.Exp(-dt / 0.06f)) : py;
        hasY = true;
        Vector3 basePos = new Vector3(player.transform.position.x, smY, player.transform.position.z);
        Vector3 pos = basePos + Vector3.up * player.EyeHeight + body * mountOffset
                      + body * new Vector3(bobX, bobY + landOffset + breath * 0.004f, 0f) + look * jPos;
        transform.SetPositionAndRotation(pos, look);
    }
}
