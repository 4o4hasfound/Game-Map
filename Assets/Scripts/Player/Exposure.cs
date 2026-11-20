using UnityEngine;

// How much light falls on you, and whether that is enough for it to see you.
// It is short-sighted: in the dark and in ordinary dim light it sees nothing (it hunts by sound). It makes you out
// only in obviously strong light: right under a working lamp, next to a desk lamp, or with your camera light on
// (the light itself is a beacon; pointed at it, it sees you from much further). Crouched, you stay hidden in
// brighter light than standing. Hiding places hide you whatever the light.
//   Ambient: the lamps' light at your chest (inverse square with URP's range window, only lamps with a clear line
//            to you, spot lamps only inside their cone); worked out ten times a second.
public static class Exposure
{
    // calibrated in the built game (autotest "exposure" line): under a working ceiling bulb about 1.4, a few metres
    // from it about 0.3, a dark corridor about 0
    public const float Dim = 0.25f;               // the meter's "dim"
    public const float Bright = 0.8f;             // standing in this much light, it can see you
    public const float BrightCrouched = 1.6f;     // crouched, only in this much
    public const float SeeRange = 10f;            // how far it sees you in bright light
    public const float LightRange = 14f;          // your camera light on: seen from this far...
    public const float BeamRange = 22f;           // ...and this far when the beam points at it
    public static float? TestAmbient;              // tests: force the lamp light

    public static float Ambient { get; private set; }
    static float lastT = -1f;
    static readonly int Mask = (1 << Layers.Structure) | (1 << Layers.Ceiling) | (1 << Layers.Door);

    public static bool OwnLight => CameraLight.Instance && CameraLight.Instance.On;
    static bool Crouched => PlayerMotor.Instance && PlayerMotor.Instance.Crouching;

    public static void Tick()
    {
        if (Time.time - lastT < 0.1f && lastT >= 0f) return;
        lastT = Time.time;
        var g = Game.Instance;
        if (!g || !g.player) { Ambient = 0f; return; }
        if (TestAmbient.HasValue) { Ambient = TestAmbient.Value; return; }
        var chest = g.PlayerFeet + Vector3.up * (Crouched ? 0.7f : 1.2f);
        float sum = 0f;
        foreach (var lamp in Lamp.All)
        {
            var l = lamp ? lamp.lightSource : null;
            if (!l || !l.enabled || !l.gameObject.activeInHierarchy || l.intensity <= 0.01f) continue;
            var lp = l.transform.position;
            var d = chest - lp;
            float dist = d.magnitude;
            if (dist > l.range) continue;
            if (l.type == LightType.Spot && Vector3.Angle(l.transform.forward, d) > l.spotAngle * 0.5f) continue;
            float x = dist / l.range;
            float window = Mathf.Clamp01(1f - x * x * x * x); window *= window;
            float e = l.intensity * window / Mathf.Max(dist * dist, 0.25f);
            if (e < 0.03f) continue;
            // walls, ceilings and closed doors in between (start a little off the fitting itself)
            var dir = d / Mathf.Max(dist, 1e-4f);
            if (dist > 0.4f && Physics.Raycast(lp + dir * 0.25f, dir, dist - 0.45f, Mask, QueryTriggerInteraction.Ignore)) continue;
            sum += e;
        }
        Ambient = sum;
    }

    // for the meter: 0 dark .. 1 bright (your own light counts as bright)
    public static float Meter { get { Tick(); return OwnLight ? 1f : Mathf.Clamp01(Ambient / (Bright * 1.25f)); } }

    public enum Seen { Hidden, Dim, Visible, OwnLight }
    public static Seen State
    {
        get
        {
            Tick();
            if (HideSpot.Inside) return Seen.Hidden;
            if (OwnLight) return Seen.OwnLight;
            if (Ambient >= (Crouched ? BrightCrouched : Bright)) return Seen.Visible;
            return Ambient >= Dim ? Seen.Dim : Seen.Hidden;
        }
    }

    // could it see you from here (its eye, how far, which way your camera points)? Line of sight and its field of
    // view are the listener's business
    public static bool VisibleFrom(Vector3 eye, float dist)
    {
        Tick();
        if (HideSpot.Inside) return false;
        if (OwnLight)
        {
            if (dist <= LightRange) return true;
            var g = Game.Instance;
            if (g && g.PlayerCamera && dist <= BeamRange)
            {
                var cam = g.PlayerCamera.transform;
                return Vector3.Angle(cam.forward, eye - cam.position) < 40f;
            }
            return false;
        }
        return dist <= SeeRange && Ambient >= (Crouched ? BrightCrouched : Bright);
    }
}
