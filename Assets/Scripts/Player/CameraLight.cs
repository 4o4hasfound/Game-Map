using UnityEngine;

// The camcorder's video light (F). A warm beam forward from the camera so you can see where you are going.
// It runs on a small battery: about 30 s of light from full, and it charges back fully in about 12 s once it has
// been off for a second, so you use it in short bursts. It stutters when nearly flat. The listener's eyes are poor,
// but a light is the one thing it does see (see Exposure): switching it on in front of it is a mistake. The switch
// clicks, and it can hear that from a few metres (30 dB at 1 m).
public class CameraLight : MonoBehaviour
{
    public static CameraLight Instance { get; private set; }
    public Light spot;
    public float baseIntensity = 7f;
    // like a camcorder's auto iris: right up against a wall (or inside a wardrobe) the light dims instead of
    // burning a white disc into the picture
    public float NearDim { get; private set; } = 1f;
    public float Battery { get; private set; } = 1f;
    public bool On { get; private set; }
    public const float Drain = 1f / 30f, Recharge = 1f / 12f, RechargeDelay = 1f;
    public const float ClickDb = 30f;
    float flickerT, offSince;

    void Awake() => Instance = this;

    const float IrisDistance = 3.5f;
    static float NearHit(Vector3 from, Vector3 dir) =>
        Physics.Raycast(from, dir, out var hit, IrisDistance, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : IrisDistance;

    public void SetOn(bool v)
    {
        if (v && Battery <= 0.02f) v = false;
        if (v == On) return;
        On = v;
        if (!v) offSince = Time.time;
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("light_tick"), transform.position, 0.3f, 1.4f, 0.3f, 8f);
        NoiseBus.Emit(transform.position, ClickDb, true, "camera light click");
    }

    public void Refill(float amount = 1f) { Battery = Mathf.Min(1f, Battery + amount); }

    void Update()
    {
        var g = Game.Instance;
        bool play = g && g.State == Game.Phase.Hotel;
        if (play && !Game.InMenu && Input.GetKeyDown(KeyCode.F)) SetOn(!On);
        if (!play && On) On = false;
        float dt = Time.deltaTime;
        if (On)
        {
            Battery = Mathf.Max(0f, Battery - Drain * dt);
            if (Battery <= 0f)
            {
                On = false;
                offSince = Time.time;
                if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("power_down"), transform.position, 0.25f, 1.6f, 0.3f, 6f);
                if (Hud.Instance) Hud.Instance.Announce("CAMERA BATTERY FLAT", 2f);
            }
        }
        else if (Time.time - offSince > RechargeDelay) Battery = Mathf.Min(1f, Battery + Recharge * dt);
        if (!spot) return;
        float near = 1f;
        if (On)
        {
            // the nearest surface inside the bright core of the beam (centre and four rays 10 degrees out);
            // closer than 3.5 m the light is pulled down with the square of the distance, so a wall at 1 m
            // looks about as bright as one at 3.5 m instead of burning white
            var t = spot.transform;
            float d = NearHit(t.position, t.forward);
            d = Mathf.Min(d, NearHit(t.position, Quaternion.AngleAxis(10f, t.up) * t.forward));
            d = Mathf.Min(d, NearHit(t.position, Quaternion.AngleAxis(-10f, t.up) * t.forward));
            d = Mathf.Min(d, NearHit(t.position, Quaternion.AngleAxis(10f, t.right) * t.forward));
            d = Mathf.Min(d, NearHit(t.position, Quaternion.AngleAxis(-10f, t.right) * t.forward));
            near = Mathf.Clamp(d * d / (IrisDistance * IrisDistance), 0.04f, 1f);
            // inside a wardrobe or under a bed the panel is right in front of the lens (and the camera sits inside
            // the hiding place's own collider, which the rays can't see)
            var m = PlayerMotor.Instance;
            if (m && m.HideView) near = Mathf.Min(near, 0.05f);
        }
        NearDim = Mathf.MoveTowards(NearDim, near, dt * 2.5f);
        float k = On ? Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(Battery * 3f)) * NearDim : 0f;
        if (On && Battery < 0.15f)
        {
            flickerT -= dt;
            if (flickerT <= 0f) flickerT = Random.Range(0.03f, 0.25f);
            if (Random.value < 0.25f) k *= 0.15f;
        }
        spot.enabled = k > 0.01f;
        spot.intensity = baseIntensity * k;
    }
}
