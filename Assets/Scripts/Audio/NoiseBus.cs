using System.Collections.Generic;
using UnityEngine;

// Noises the listener (the monster) can hear: footsteps, doors, breathing, phones, radios, bottles, the breaker, the
// VCR. Levels are dB at 1 m. It evaluates each noise through the same propagation the player's ears use
// (AcousticWorld), so a footstep behind a closed door is much quieter to it than one in an open doorway.
public static class NoiseBus
{
    public struct Noise
    {
        public Vector3 pos;
        public float db;          // level at 1 m
        public float time;
        public bool byPlayer;
        public string what;
        public Object src;        // the device / door that made it, if any
    }

    public static readonly List<Noise> Recent = new List<Noise>();
    public static int Total { get; private set; }
    public static event System.Action<Noise> Emitted;

    public static void Emit(Vector3 pos, float db, bool byPlayer, string what = "", Object src = null)
    {
        Total++;
        var n = new Noise { pos = pos, db = db, time = Time.time, byPlayer = byPlayer, what = what, src = src };
        Recent.Add(n);
        if (Recent.Count > 64) Recent.RemoveAt(0);
        Emitted?.Invoke(n);
    }

    // level (dB) of a noise arriving at a point through the building
    public static float Heard(Noise n, Vector3 at) => Arriving(n.db, n.pos, at);

    public static float Arriving(float db, Vector3 from, Vector3 at)
    {
        var aw = AcousticWorld.Instance;
        var p = aw ? aw.Solve(from, at) : AcousticWorld.Path.Direct(from, at);
        return db + 20f * Mathf.Log10(Mathf.Max(1e-5f, p.gain)) - 20f * Mathf.Log10(Mathf.Max(1f, p.length));
    }
}
