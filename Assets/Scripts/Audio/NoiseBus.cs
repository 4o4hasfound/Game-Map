using System.Collections.Generic;
using UnityEngine;

// Noises that something in the world can hear (footsteps, doors, splashes, devices). Listeners (the monster)
// evaluate each noise through the same propagation model the player hears with, so a splash behind a concrete
// wall is far less audible to them than one in an open doorway.
public static class NoiseBus
{
    public struct Noise
    {
        public Vector3 pos;
        public float loudness;     // ~1 = a normal footstep on wood at 1 m
        public float time;
        public bool byPlayer;
    }

    public static readonly List<Noise> Recent = new List<Noise>();
    public static int Total { get; private set; }      // every noise ever made (tests)

    public static void Emit(Vector3 pos, float loudness, bool byPlayer)
    {
        Total++;
        Recent.Add(new Noise { pos = pos, loudness = loudness, time = Time.time, byPlayer = byPlayer });
        if (Recent.Count > 64) Recent.RemoveAt(0);
    }

    // how loud a noise is at a point, using the acoustic paths through the building (0..)
    public static float Heard(Noise n, Vector3 at)
    {
        var aw = AcousticWorld.Instance;
        var p = aw ? aw.Solve(n.pos, at) : AcousticWorld.Path.Direct(n.pos, at);
        return n.loudness * p.gain / Mathf.Max(1f, p.length);
    }
}
