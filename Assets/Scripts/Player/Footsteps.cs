using System.Collections.Generic;
using UnityEngine;

// Footsteps that match the floor: the material under each foot is read from the mesh triangle the ground ray hits
// (carpet, wood, tile, concrete, metal grating, rubber, debris) and puddles/flooded rooms splash.
// Each step is a real positioned sound at the stepping foot (left/right, slightly ahead), so it is spatialised,
// reverberates in the room and travels to other rooms like everything else. Plus clothing rustle and breathing.
[RequireComponent(typeof(PlayerMotor))]
public class Footsteps : MonoBehaviour
{
    public float volume = 0.38f;

    PlayerMotor motor;
    // breathing: a respiration model sets the rate; each inhalation and exhalation is its own recorded breath
    readonly SpatialVoice[] breath = new SpatialVoice[2];
    int breathVoice;
    float drive, nextCycle = 2f, secondAt = -1f, secondVol;
    AudioClip second;
    bool mouth;
    public float BreathRate { get; private set; }      // breaths per minute, for tests / the overlay
    readonly Dictionary<Mesh, int[]> submeshStarts = new Dictionary<Mesh, int[]>();
    public string LastSurface { get; private set; } = "";

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        motor.Step += OnStep;
        motor.Landed += OnLand;
    }

    void Start()
    {
        for (int i = 0; i < breath.Length; i++)
        {
            var go = new GameObject("Breath" + i);
            go.transform.SetParent(motor.cameraTransform ? motor.cameraTransform : transform, false);
            go.transform.localPosition = new Vector3(0f, -0.08f, 0.12f);
            go.AddComponent<AudioSource>();
            var v = go.AddComponent<SpatialVoice>();
            v.headLocked = true;
            v.minDistance = 0.3f;
            v.sendLevel = 0.03f;
            v.volume = 0f;
            breath[i] = v;
        }
    }

    // Respiratory drive 0..1: walking raises it a little at once; running raises it at once and keeps building
    // through the motor's exertion (oxygen debt), which decays slowly afterwards, so the breathing slows down
    // gradually while you recover. Rate ~12 /min at rest, ~20 walking, up to ~44 after a long run.
    void Update()
    {
        if (!breath[0]) return;
        float gait = Mathf.Clamp01(motor.HorizontalSpeed / Mathf.Max(motor.walkSpeed, 0.1f));
        float load = motor.Running ? 0.5f : motor.Crouching ? 0.1f * gait : 0.22f * gait;
        float target = Mathf.Max(load, motor.Exertion);
        drive = Mathf.Lerp(drive, target, 1f - Mathf.Exp(-Time.deltaTime / (target > drive ? 2.5f : 3f)));
        BreathRate = Mathf.Lerp(12f, 44f, drive);
        // switch between nose and mouth breathing with some hysteresis
        if (!mouth && drive > 0.45f) mouth = true;
        else if (mouth && drive < 0.36f) mouth = false;

        if (secondAt >= 0f && Time.time >= secondAt)
        {
            Breathe(second, secondVol);
            secondAt = -1f;
        }
        if (Time.time < nextCycle) return;
        float period = 60f / BreathRate * Random.Range(0.94f, 1.06f);
        AudioClip a, b;
        float va, vb;
        if (mouth)
        {
            // out of breath: a forced exhalation, then a quick inhalation
            float k = Mathf.InverseLerp(0.36f, 1f, drive);
            a = SoundBank.Pick("breath_run_out");
            b = SoundBank.Pick("breath_run_in");
            va = Mathf.Lerp(0.07f, 0.22f, k);
            vb = va * 0.9f;
        }
        else
        {
            // through the nose: inhalation, then a longer, softer exhalation
            float k = Mathf.InverseLerp(0f, 0.45f, drive);
            a = SoundBank.Pick("breath_calm_in");
            b = SoundBank.Pick("breath_calm_out");
            va = Mathf.Lerp(0.006f, 0.03f, k);
            vb = va * 0.85f;
        }
        if (!a || !b) { nextCycle = Time.time + period; return; }
        // the next breath cannot start before this one is finished
        float gap = a.length * (mouth ? 0.92f : 0.95f) + Random.Range(0f, 0.06f);
        period = Mathf.Max(period, gap + b.length * 0.9f);
        Breathe(a, va);
        second = b;
        secondVol = vb;
        secondAt = Time.time + gap;
        nextCycle = Time.time + period;
    }

    void Breathe(AudioClip clip, float vol)
    {
        if (!clip) return;
        var v = breath[breathVoice];
        breathVoice = (breathVoice + 1) % breath.Length;
        v.volume = vol;
        v.Play(clip, false);
    }

    public string Surface()
    {
        if (!motor.IsGrounded) return "concrete";
        var hit = motor.GroundHit;
        var room = HotelWorld.Instance ? HotelWorld.Instance.RoomAt(transform.position + Vector3.up * 0.3f) : null;
        if (room != null && room.water) return "water";
        var col = hit.collider;
        if (!col) return "concrete";
        string n = col.name;
        if (n.StartsWith("Ramp_")) return n.Contains("metal") ? "metal" : n.Contains("escalator") ? "escalator" : "tile";
        if (col is MeshCollider mc && mc.sharedMesh && hit.triangleIndex >= 0)
        {
            var mr = col.GetComponent<MeshRenderer>();
            int sub = Submesh(mc.sharedMesh, hit.triangleIndex);
            if (mr && sub >= 0 && sub < mr.sharedMaterials.Length && mr.sharedMaterials[sub])
                return FromMaterial(mr.sharedMaterials[sub].name, n);
        }
        return n.StartsWith("Props") ? "wood" : "concrete";
    }

    int Submesh(Mesh m, int tri)
    {
        if (!submeshStarts.TryGetValue(m, out var starts))
        {
            starts = new int[m.subMeshCount];
            for (int i = 0; i < m.subMeshCount; i++) starts[i] = (int)m.GetSubMesh(i).indexStart / 3;
            submeshStarts[m] = starts;
        }
        for (int i = starts.Length - 1; i >= 0; i--)
            if (tri >= starts[i]) return i;
        return 0;
    }

    public static string FromMaterial(string mat, string obj)
    {
        if (mat.StartsWith("Carpet") || mat.StartsWith("Fabric") || mat.StartsWith("Velvet") || mat.StartsWith("Rug")) return "carpet";
        if (mat.StartsWith("Wood") || mat.StartsWith("Plywood") || mat.StartsWith("Trim_Wood")) return "wood";
        if (mat.StartsWith("Marble") || mat.StartsWith("Tile") || mat.StartsWith("Ceramic") || mat.StartsWith("Linoleum")) return "tile";
        if (mat.StartsWith("Metal") || mat.StartsWith("Steel") || mat.StartsWith("Iron") || mat.StartsWith("Brass")) return "metal";
        if (mat.StartsWith("Water")) return "water";
        if (mat.StartsWith("Rubble") || mat.StartsWith("Soot") || mat.StartsWith("Plaster") || mat.StartsWith("Glass")) return "rubble";
        if (mat.StartsWith("Rubber")) return "rubber";
        return "concrete";
    }

    Vector3 FootPos(int foot)
    {
        var t = motor.cameraTransform ? motor.cameraTransform : transform;
        Vector3 right = t.right;
        right.y = 0;
        Vector3 fwd = t.forward;
        fwd.y = 0;
        return transform.position + right.normalized * (foot == 0 ? -0.12f : 0.12f) + fwd.normalized * 0.15f + Vector3.up * 0.03f;
    }

    void OnStep(int foot, float strength)
    {
        if (!AudioEngine.Instance) return;
        string s = Surface();
        LastSurface = s;
        float v = volume * Mathf.Lerp(0.45f, 1.1f, strength) * Random.Range(0.85f, 1.1f);
        string key = s=="concrete" && motor.Running ? "fs_run_concrete" : "fs_"+s;
        AudioEngine.Instance.PlayAt(SoundBank.Pick(key), FootPos(foot), v, 1f, 0.6f, 30f);
        // how much the step gives you away: splashing and metal grating carry, carpet hides you
        float surf = s == "water" ? 2.2f : s == "metal" ? 1.7f : s == "rubble" ? 1.3f : s == "tile" ? 1.1f : s == "carpet" ? 0.45f : 0.9f;
        NoiseBus.Emit(FootPos(foot), surf * Mathf.Lerp(0.3f, 1.4f, strength), true);
        if (Random.value < 0.18f)
            AudioEngine.Instance.PlayAt(SoundBank.Pick("cloth"), transform.position + Vector3.up * 1.0f, 0.025f * strength, 1f, 0.5f, 6f);
    }

    void OnLand(float impact)
    {
        if (!AudioEngine.Instance) return;
        AudioEngine.Instance.PlayAt(SoundBank.Pick("land_" + Surface()), transform.position, volume * (0.6f + impact), 1f, 0.6f, 30f);
        NoiseBus.Emit(transform.position, 2f + impact * 2f, true);
    }
}
