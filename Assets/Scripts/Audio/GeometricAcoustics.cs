using UnityEngine;

// Reverberation derived from the geometry around a point, with no rooms and no presets.
//
// Rays are cast in 64 evenly spread directions (a few per frame, so the estimate is always up to date and changes
// continuously as you move). From the hit distances and the acoustic properties of the hit surfaces:
//   mean free path    l  = mean ray length (rays that escape into open space count as 60 m, fully absorbing)
//   absorption        a  = mean absorption coefficient of what the rays hit (low and high band)
//   RT60 (Eyring)     T  = 0.161 V / (-S ln(1-a) + 4mV),  V/S = l/4  ->  T = 0.04 l / (-ln(1-a) + m l)
//   reverberant level      from the room constant R = S a / (1-a)   ->  sqrt(4/R)
//   early reflections      the nearest surfaces in different directions give first-order reflection delays,
//                          levels (1 - a) and directions (left / right of the head)
// A small furnished room, a tiled bathroom, a long corridor and a concrete hall therefore sound different because
// of what they are, and standing in a doorway naturally gives a mixture.
public class AcousticSpace
{
    public const int Dirs = 64;
    public const float MaxDist = 60f;
    static Vector3[] dirs;
    readonly float[] dist = new float[Dirs];
    readonly float[] aLow = new float[Dirs];
    readonly float[] aHigh = new float[Dirs];
    readonly bool[] valid = new bool[Dirs];
    int cursor;
    public static int Mask = (1 << MaterialAcoustics.StructureLayer) | (1 << MaterialAcoustics.DoorLayer) | (1 << MaterialAcoustics.PropLayer) | (1 << MaterialAcoustics.CeilingLayer);

    // derived (smoothed) values
    public float Rt60 = 0.5f, Rt60High = 0.4f, MeanFree = 4f, Absorb = 0.1f, Level = 0.3f, Openness;
    public readonly float[] ErDelay = new float[8], ErGain = new float[8], ErPan = new float[8];

    public static Vector3[] Directions
    {
        get
        {
            if (dirs != null) return dirs;
            dirs = new Vector3[Dirs];
            float ga = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < Dirs; i++)
            {
                float y = 1f - (i + 0.5f) / Dirs * 2f;
                float r = Mathf.Sqrt(1f - y * y);
                dirs[i] = new Vector3(Mathf.Cos(ga * i) * r, y, Mathf.Sin(ga * i) * r);
            }
            return dirs;
        }
    }

    // trace `count` more directions from `origin` (call often; the estimate is a moving window over all 64)
    public void Trace(Vector3 origin, int count)
    {
        var d = Directions;
        for (int k = 0; k < count; k++)
        {
            int i = cursor;
            cursor = (cursor + 1) % Dirs;
            if (Physics.Raycast(origin, d[i], out var hit, MaxDist, Mask, QueryTriggerInteraction.Ignore))
            {
                var s = MaterialAcoustics.Of(hit);
                dist[i] = Mathf.Max(hit.distance, 0.3f);
                aLow[i] = s.absorbLow;
                aHigh[i] = s.absorbHigh;
            }
            else
            {
                dist[i] = MaxDist;
                aLow[i] = aHigh[i] = 1f;
            }
            valid[i] = true;
        }
    }

    public void Evaluate(Transform head, float dt, float smoothing = 0.3f)
    {
        float sumD = 0f, sumAL = 0f, sumAH = 0f, esc = 0f, sumD3 = 0f;
        int n = 0;
        for (int i = 0; i < Dirs; i++)
        {
            if (!valid[i]) continue;
            n++;
            sumD += dist[i];
            sumD3 += Mathf.Min(dist[i], 30f) * Mathf.Min(dist[i], 30f) * Mathf.Min(dist[i], 30f);
            sumAL += aLow[i];
            sumAH += aHigh[i];
            if (dist[i] >= MaxDist - 0.01f) esc++;
        }
        if (n == 0) return;
        float l = Mathf.Clamp(sumD / n, 0.5f, MaxDist);
        float al = Mathf.Clamp(sumAL / n, 0.01f, 0.95f);
        float ah = Mathf.Clamp(sumAH / n, 0.01f, 0.97f);
        // Eyring with air absorption (4mV term, m ~ 0.0012 /m at 500 Hz, ~0.012 /m at 4 kHz): in big spaces the air
        // itself eats the treble and shortens the tail
        float ll = Mathf.Min(l, 25f);
        float rt = Mathf.Clamp(0.04f * ll / (-Mathf.Log(1f - al) + 0.0012f * ll), 0.08f, 5f);
        float rth = Mathf.Clamp(0.04f * ll / (-Mathf.Log(1f - ah) + 0.012f * ll), 0.05f, 5f);
        // volume from the mean cubed distance (a sphere-like estimate), surface from V/S = l/4
        float V = 4.18879f * sumD3 / n;
        float S = Mathf.Max(4f * V / l, 1f);
        float R = S * al / (1f - al);
        float level = Mathf.Clamp(Mathf.Sqrt(4f / R) * 2.2f, 0.03f, 1.1f) * (1f - 0.8f * esc / n);
        float k = 1f - Mathf.Exp(-dt / Mathf.Max(smoothing, 1e-3f));
        Rt60 += (rt - Rt60) * k;
        Rt60High += (rth - Rt60High) * k;
        MeanFree += (l - MeanFree) * k;
        Absorb += (al - Absorb) * k;
        Level += (level - Level) * k;
        Openness += (esc / n - Openness) * k;

        // early reflections: nearest surface in each of 8 horizontal-ish sectors around the head
        if (!head) return;
        for (int s = 0; s < 8; s++)
        {
            float best = MaxDist, bestA = 1f;
            Vector3 bestDir = Vector3.forward;
            for (int i = s; i < Dirs; i += 8)
            {
                if (!valid[i] || dist[i] >= best) continue;
                best = dist[i];
                bestA = aHigh[i] * 0.5f + aLow[i] * 0.5f;
                bestDir = Directions[i];
            }
            float delay = Mathf.Clamp(2f * best / 343f, 0.002f, 0.12f);
            float gain = best >= MaxDist ? 0f : (1f - bestA) / (1f + 2f * best);
            float pan = Vector3.Dot(bestDir, head.right);
            ErDelay[s] += (delay - ErDelay[s]) * k;
            ErGain[s] += (gain - ErGain[s]) * k;
            ErPan[s] += (pan - ErPan[s]) * k;
        }
    }
}

// The listener's acoustic space: traced every frame, feeds the local reverb.
public class GeometricAcoustics : MonoBehaviour
{
    public static GeometricAcoustics Instance { get; private set; }
    public int raysPerFrame = 8;
    public AcousticSpace Space { get; } = new AcousticSpace();

    void Awake()
    {
        Instance = this;
        Space.Trace(transform.position, AcousticSpace.Dirs);
        Space.Evaluate(transform, 1f, 0.001f);
    }

    void Update()
    {
        Space.Trace(transform.position, raysPerFrame);
        Space.Evaluate(transform, Time.deltaTime, 0.25f);
        if (ReverbBus.Instance) ReverbBus.Instance.SetLocal(Space);
    }
}
