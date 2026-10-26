using UnityEngine;

// The camcorder's video light (F). Like real cheap equipment:
//  - beam shape from a cookie texture: hot centre, soft spill with a faint reflector ring, slight unevenness
//  - takes a moment to come up to brightness when switched on
//  - mostly steady, with rare irregular brown-outs (a loose battery contact): a dip or a short stutter, never a
//    regular rhythm, and never long enough to leave you blind
//  - held in the right hand, below and beside the eyes: the arm lags behind quick turns and overshoots a little,
//    the beam swings with the gait (side to side once per stride, a dip on every footfall), much more when
//    running, trembles while out of breath, and drifts slightly even when standing still
public class CamcorderLight : MonoBehaviour
{
    public Light lamp;
    public Transform cam;
    public PlayerMotor motor;
    public float baseIntensity = 2.4f;
    public Vector3 handOffset = new Vector3(0.2f, -0.26f, 0.22f);   // camera space: right, down, forward
    public bool owned;                 // false until the flashlight on the floor is picked up (FlashlightPickup)

    Quaternion rot;
    Vector3 aimVel;        // arm spring: angular velocity of the aim (degrees/s, yaw / pitch / roll)
    Vector3 aimErr;        // how far the hand trails the look direction (degrees)
    float sway, aimDist = 6f;
    Vector3 handPos;
    float warm, nextFault, faultUntil, faultDepth;
    bool faulting;

    void Start()
    {
        rot = cam.rotation;
        if (!motor) motor = GetComponentInParent<PlayerMotor>() ?? FindAnyObjectByType<PlayerMotor>();
        handPos = cam.TransformPoint(handOffset);
        if (lamp)
        {
            baseIntensity = lamp.intensity;
            lamp.enabled = false;
        }
        nextFault = Time.time + Random.Range(25f, 70f);
    }

    void LateUpdate()
    {
        if (!lamp || !cam) return;
        if (owned && Input.GetKeyDown(KeyCode.F))
        {
            lamp.enabled = !lamp.enabled;
            warm = 0f;
            if (AudioEngine.Instance) AudioEngine.Instance.PlayAt("radio_click", cam.position + cam.up * 0.1f, 0.2f, 1f, 0.3f, 6f);
        }
        Hold();
        if (!lamp.enabled) return;

        warm = Mathf.MoveTowards(warm, 1f, Time.deltaTime * 4f);
        float k = Mathf.SmoothStep(0.25f, 1f, warm);
        // gentle electrical noise (1-2 %)
        k *= 0.985f + 0.015f * Mathf.PerlinNoise(Time.time * 7.3f, 0.4f);
        if (!faulting && Time.time > nextFault)
        {
            faulting = true;
            faultUntil = Time.time + Random.Range(0.06f, 0.5f);
            faultDepth = Random.Range(0.15f, 0.7f);
        }
        if (faulting)
        {
            // irregular stutter while the contact is bad
            float n = Mathf.PerlinNoise(Time.time * 37f, 3.1f);
            k *= n > 0.5f ? 1f - faultDepth : 1f - faultDepth * 0.3f;
            if (Time.time > faultUntil)
            {
                faulting = false;
                nextFault = Time.time + Random.Range(20f, 90f) * (Random.value < 0.2f ? 0.2f : 1f);
            }
        }
        lamp.intensity = baseIntensity * k;
    }

    // the hand: a damped spring chasing the look direction, plus gait sway, tremor and idle drift
    void Hold()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (dt <= 0f) return;
        float speed = motor ? motor.HorizontalSpeed : 0f;
        bool running = motor && motor.Running;
        float exertion = motor ? motor.Exertion : 0f;
        float phase = motor ? motor.StepPhase : 0f;          // pi per step, 2 pi per stride
        float gait = Mathf.Clamp01(speed / 2.1f);
        float run = running ? Mathf.Clamp01((speed - 2.1f) / 1.8f) : 0f;
        sway = Mathf.MoveTowards(sway, gait, dt * 3f);       // eases in and out when you start / stop walking

        // the arm trails the head: integrate the error between where the hand points and where the eyes look
        Quaternion delta = Quaternion.Inverse(rot) * cam.rotation;
        Vector3 e = delta.eulerAngles;
        e = new Vector3(Mathf.DeltaAngle(0f, e.x), Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z));
        float k = Mathf.Lerp(90f, 60f, run), damp = Mathf.Lerp(13f, 9f, run);   // looser grip while running
        aimVel += (e * k - aimVel * damp) * dt;
        Quaternion step = Quaternion.Euler(aimVel * dt);
        rot = rot * step;
        if (Quaternion.Angle(rot, cam.rotation) > 25f) rot = Quaternion.RotateTowards(cam.rotation, rot, 25f);

        // gait: one side-to-side swing per stride, one dip per footfall; running swings much harder
        float yawAmp = sway * Mathf.Lerp(1.3f, 5.5f, run);
        float pitchAmp = sway * Mathf.Lerp(0.9f, 3.6f, run);
        float swingYaw = Mathf.Sin(phase) * yawAmp;
        float swingPitch = (Mathf.Cos(phase * 2f) * 0.5f + 0.5f) * pitchAmp;       // beam dips as the foot lands
        float swingRoll = Mathf.Sin(phase) * yawAmp * 0.6f;
        // tremor when out of breath, and slow drift of a hand held still
        float t = Time.time;
        float tremor = 0.15f + exertion * 0.9f;
        float tYaw = (Mathf.PerlinNoise(t * 6.1f, 1.7f) - 0.5f) * tremor + (Mathf.PerlinNoise(t * 0.35f, 4.2f) - 0.5f) * 1.2f;
        float tPitch = (Mathf.PerlinNoise(2.9f, t * 6.7f) - 0.5f) * tremor + Mathf.Sin(t * 1.6f) * 0.15f * (1f + exertion * 2f);
        Quaternion swing = Quaternion.Euler(swingPitch + tPitch, swingYaw + tYaw, swingRoll);

        // aim converges on what you are looking at, so the spot stays near the centre of the picture
        float want = 9f;
        if (Physics.Raycast(cam.position, cam.forward, out var hit, 14f, AcousticSpace.Mask, QueryTriggerInteraction.Ignore))
            want = Mathf.Clamp(hit.distance, 1.2f, 14f);
        aimDist = Mathf.Lerp(aimDist, want, 1f - Mathf.Exp(-dt * 6f));

        // the hand itself bobs with the body and swings slightly across
        Vector3 bob = new Vector3(Mathf.Sin(phase) * 0.025f, -Mathf.Abs(Mathf.Cos(phase)) * 0.02f, 0f) * sway * Mathf.Lerp(1f, 2.4f, run);
        Vector3 target = cam.TransformPoint(handOffset + bob);
        handPos = Vector3.Lerp(handPos, target, 1f - Mathf.Exp(-dt * 30f));
        Vector3 focus = cam.position + (rot * Vector3.forward) * aimDist;
        Quaternion aim = Quaternion.LookRotation(focus - handPos, rot * Vector3.up);
        lamp.transform.SetPositionAndRotation(handPos, aim * swing);
    }

    // picked up from the floor: in the hand, and already shining
    public void Grant(bool lit)
    {
        owned = true;
        if (!lamp) return;
        lamp.enabled = lit;
        warm = 1f;
        rot = cam.rotation;
        aimVel = Vector3.zero;
        handPos = cam.TransformPoint(handOffset);
    }
}
