using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Elevators are separate machines, each joining exactly two floors at its own place in the building
// (A: 7F service elevator <-> lobby dock, B: lobby freight elevator <-> B1). Every car waits with its doors open;
// pressing its one button closes the doors, runs the trip (the tape fast-forwards), and opens at the other end.
public class ElevatorSystem : MonoBehaviour
{
    public static ElevatorSystem Instance { get; private set; }

    public ElevatorStop[] stops;          // all working stops
    public string[] links;                // link id per stop
    public PlayerMotor player;

    bool busy;
    readonly Dictionary<ElevatorStop, SpatialVoice> muzak = new Dictionary<ElevatorStop, SpatialVoice>();
    SpatialVoice motor, wind;

    void Awake() => Instance = this;

    void Start()
    {
        var rig = new GameObject("ElevatorRideAudio").transform;
        motor = Voice(rig, "ElevatorMotor", 0f, 1.5f, 30f);
        wind = Voice(rig, "ShaftWind", 0f, 1f, 18f);
        foreach (var s in stops)
        {
            s.SetOpen(1f);
            var m = Voice(s.audioAnchor, "ElevatorMuzak", 0.045f, 0.6f, 7f);
            muzak[s] = m;
            m.Play(SoundBank.Get("elev_muzak"), true, Random.value * 20f);
        }
        // landing doors are the acoustic portal between the car and the floor
        var world = HotelWorld.Instance;
        if (world != null)
            foreach (var s in stops)
            {
                var lv = world.levels[s.levelIndex];
                var stopRef = s;
                float best = 3f;
                PortalInfo bestP = null;
                foreach (var p in lv.portals)
                {
                    if (p.kind != "elevator") continue;
                    float d = Vector3.Distance(p.pos, s.transform.position);
                    if (d < best) { best = d; bestP = p; }
                }
                if (bestP != null) bestP.openFunc = () => Mathf.Max(0.04f, stopRef.Openness);
            }
    }

    SpatialVoice Voice(Transform parent, string name, float vol, float minD, float maxD)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<AudioSource>();
        var v = go.AddComponent<SpatialVoice>();
        v.volume = vol;
        v.minDistance = minD;
        v.maxDistance = maxD;
        v.label = name;
        return v;
    }

    ElevatorStop Partner(ElevatorStop s)
    {
        int i = System.Array.IndexOf(stops, s);
        for (int k = 0; k < stops.Length; k++)
            if (k != i && links[k] == links[i]) return stops[k];
        return null;
    }

    public void Ride(ElevatorStop from)
    {
        if (busy) return;
        var to = Partner(from);
        if (to) StartCoroutine(RideRoutine(from, to));
    }

    IEnumerator Doors(ElevatorStop s, bool open)
    {
        float from = s.Openness, target = open ? 1f : 0f;
        if (Mathf.Abs(from - target) < 0.01f) yield break;
        PlayAt("elev_door", s.transform.position + Vector3.up * 1.2f, 0.7f);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 1.6f)
        {
            s.SetOpen(Mathf.Lerp(from, target, t));
            yield return null;
        }
        s.SetOpen(target);
    }

    IEnumerator RideRoutine(ElevatorStop from, ElevatorStop to)
    {
        busy = true;
        yield return Doors(from, false);
        bool carrying = player && from.Contains(player.transform.position);
        PlayAt("elev_start", from.car.position + Vector3.up * 1.2f, 0.8f);
        yield return new WaitForSeconds(0.6f);
        motor.transform.parent.SetParent(from.audioAnchor, false);
        motor.Play(SoundBank.Get("elev_motor"), true, 0f);
        wind.Play(SoundBank.Get("elev_shaft_wind"), true, 0f);
        var cam = player && player.cameraTransform ? player.cameraTransform.GetComponent<VHSCamera>() : null;
        const float dur = 7f;
        bool moved = false;
        float nextCreak = 1.5f;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            float ramp = Mathf.Clamp01(t / 1.2f) * Mathf.Clamp01((dur - t) / 1.2f);
            motor.volume = 0.45f * ramp + 0.1f;
            wind.volume = 0.25f * ramp;
            if (carrying)
            {
                VHSPostFX.FastForward = ramp;
                if (cam) cam.SetRumble(ramp);
            }
            if (t > nextCreak)
            {
                nextCreak += Random.Range(1.5f, 3f);
                PlayAt("elev_creak", motor.transform.position + Vector3.up * 0.8f, 0.3f);
            }
            if (!moved && t > dur * 0.5f)
            {
                moved = true;
                to.SetOpen(0f);
                if (carrying)
                {
                    Vector3 local = from.car.InverseTransformPoint(player.transform.position);
                    float yawDelta = to.car.eulerAngles.y - from.car.eulerAngles.y;
                    HotelWorld.Instance.SetLevel(to.levelIndex);
                    motor.transform.parent.SetParent(to.audioAnchor, false);
                    player.Teleport(to.car.TransformPoint(local) + Vector3.up * 0.02f, yawDelta);
                    motor.Play(SoundBank.Get("elev_motor"), true, 0f);
                    wind.Play(SoundBank.Get("elev_shaft_wind"), true, 0f);
                }
            }
            yield return null;
        }
        motor.Stop();
        wind.Stop();
        VHSPostFX.FastForward = 0f;
        if (cam)
        {
            cam.SetRumble(0f);
            cam.Shake(0.2f);
        }
        PlayAt("elev_stop", to.car.position + Vector3.up * 1.2f, 0.8f);
        yield return new WaitForSeconds(1.0f);
        PlayAt("elev_chime", to.car.position + Vector3.up * 2.3f, 0.5f);
        if (carrying && VHSOverlay.Instance) VHSOverlay.Instance.Announce(HotelWorld.Instance.Current.label);
        yield return Doors(to, true);
        from.SetOpen(1f);     // the car you left is waiting there again
        busy = false;
    }

    static void PlayAt(string clip, Vector3 p, float vol)
    {
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick(clip), p, vol, 1f, 1f, 30f);
    }
}
