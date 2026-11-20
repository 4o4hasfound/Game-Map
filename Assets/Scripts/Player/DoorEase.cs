using UnityEngine;

// Easing a door (hold E): a pointer drifts along a bar and you keep it inside a band with the arrow keys (left /
// right push it, it carries on a little after you let go). The band is narrow, wanders to random places along the
// bar, and gets narrower the longer you hold (and changes width a little as it goes). The hinge fights you too: the
// drift wanders, and now and then the door catches and jolts the pointer. While the pointer is inside the band the
// door eases quietly; the moment it slips out, the door swings the rest of the way (HingeDoor.Slip) and the noise is
// as big as what was left. Letting go of E stops the door where it is; the next hold starts again, wide and centred.
public class DoorEase : MonoBehaviour
{
    public static DoorEase Instance { get; private set; }
    public const float StartBand = 0.12f;    // half-width of the band when you start (the bar runs 0..1)
    public const float EndBand = 0.065f;     // ...and after ShrinkTime of holding
    public const float ShrinkTime = 4.5f;
    public const float Wander = 0.18f;       // how far the band's middle wanders from the bar's middle
    public const float Push = 3.4f;          // the arrow keys' push (bar widths per second squared)
    public const float Drift = 0.9f;         // the hinge's pull at its strongest
    public static bool NoDrift;              // tests: the hinge never pulls and the band stays put, wide

    public HingeDoor Door { get; private set; }
    public bool Free { get; private set; }               // no door: the VCR's tracking in the prologue
    public string Hint { get; private set; } = "keep it in the middle";
    public bool Active => Door || Free;
    public float Pointer { get; private set; } = 0.5f;
    public float BandCentre { get; private set; } = 0.5f;
    public float BandHalf { get; private set; } = StartBand;
    public float LastSlipDb { get; private set; }
    public float LastSlipLeft { get; private set; }
    public bool InBand => Mathf.Abs(Pointer - BandCentre) <= BandHalf;
    float vel, t0, seed, nextJolt, nextHop, hopFrom, hopTo, hopAt;

    void Awake() => Instance = this;

    public void Begin(HingeDoor d) { Door = d; Free = false; Hint = "keep it in the middle"; Reset(); }
    // the same pointer without a door: the VCR's tracking (it never slips; it only says whether you're in)
    public void BeginFree(string hint) { Door = null; Free = true; Hint = hint; Reset(); }

    void Reset()
    {
        Pointer = 0.5f;
        BandCentre = 0.5f;
        BandHalf = StartBand;
        vel = 0f;
        t0 = Time.time;
        seed = Random.value * 100f;
        nextJolt = Time.time + Random.Range(1.2f, 2.4f);
        hopFrom = hopTo = 0.5f; hopAt = Time.time; nextHop = Time.time + Random.Range(0.7f, 1.3f);
    }

    public void End() { Door = null; Free = false; }

    // one frame; input -1..1 (left / right arrow). Returns false when the pointer slipped out (the door has swung).
    public bool Tick(float dt, float input)
    {
        if (!Door) return false;
        Step(dt, input, EndBand);
        if (!InBand) { ForceSlip(); return false; }
        Door.Hold(dt, Vector2.zero);
        return true;
    }

    public bool TickFree(float dt, float input)
    {
        if (!Free) return false;
        Step(dt, input, 0.07f);
        return InBand;
    }

    void Step(float dt, float input, float endBand)
    {
        float age = Time.time - t0;
        float drift = 0f;
        if (!NoDrift)
        {
            // the band: narrower and narrower, breathing a little; every second or so it slides to a new place
            float k = Mathf.Clamp01(age / ShrinkTime);
            BandHalf = Mathf.Lerp(StartBand, endBand, k) * (0.85f + 0.3f * Mathf.PerlinNoise(seed + 3.1f, Time.time * 0.9f));
            if (Time.time >= nextHop)
            {
                hopFrom = BandCentre;
                hopTo = Mathf.Clamp(BandCentre + Random.Range(-0.11f, 0.11f), 0.5f - Wander, 0.5f + Wander);
                hopAt = Time.time;
                nextHop = Time.time + Random.Range(1.0f, 1.8f) * Mathf.Lerp(1f, 0.75f, k);
            }
            BandCentre = Mathf.Lerp(hopFrom, hopTo, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - hopAt) / 0.6f)));
            // the hinge's pull builds over the first half second, wanders slowly, and the door catches now and then
            float ramp = Mathf.Clamp01(age / 0.5f);
            drift = (Mathf.PerlinNoise(seed, Time.time * 0.8f) - 0.5f) * 2f * Drift * ramp;
            if (Time.time >= nextJolt)
            {
                nextJolt = Time.time + Random.Range(1.6f, 3.2f);
                vel += (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.12f, 0.22f);
            }
        }
        else { BandCentre = 0.5f; BandHalf = StartBand; }
        vel += (drift + Mathf.Clamp(input, -1f, 1f) * Push) * dt;
        vel *= Mathf.Exp(-2.4f * dt);
        Pointer = Mathf.Clamp01(Pointer + vel * dt);
    }

    // the pointer left the band (or a test says so): the door goes the rest of the way
    public void ForceSlip()
    {
        if (!Door) return;
        LastSlipLeft = Door.EaseLeft;
        LastSlipDb = Door.Slip();
        Door = null;
    }

    public static float ArrowInput => (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
}
