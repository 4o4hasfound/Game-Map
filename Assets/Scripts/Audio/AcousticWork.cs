using System.Collections.Generic;
using UnityEngine;

// Main-thread work scheduler. No physics, allocation or cache access takes place in audio callbacks.
public static class AcousticWork
{
    static int frame = -1, solves, rays;
    public static int SolveCount { get; private set; }
    public static int ReflectionRays { get; private set; }
    public static double SolveMilliseconds { get; private set; }
    static readonly System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
    sealed class Probe { public AcousticSpace space = new AcousticSpace(); public float next; }
    static readonly Dictionary<Vector3Int, Probe> probes = new Dictionary<Vector3Int, Probe>();
    static void BeginFrame()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount; SolveCount = solves; ReflectionRays = rays;
        solves = rays = 0; SolveMilliseconds = 0;
    }
    public static bool PermitSolve() { BeginFrame(); if (solves >= 4) return false; solves++; return true; }
    public static AcousticWorld.Path Solve(Vector3 source, Vector3 listener, bool occlusion)
    {
        timer.Restart();
        var path = occlusion && AcousticWorld.Instance ? AcousticWorld.Instance.Solve(source, listener) : AcousticWorld.Path.Direct(source, listener);
        timer.Stop(); SolveMilliseconds += timer.Elapsed.TotalMilliseconds; return path;
    }
    public static AcousticSpace SourceProbe(Vector3 p)
    {
        BeginFrame();
        // Fine spatial cells share samples, independent of room IDs. A moving source requests its new cell.
        var key = new Vector3Int(Mathf.FloorToInt(p.x*0.5f),Mathf.FloorToInt(p.y),Mathf.FloorToInt(p.z*0.5f));
        if (!probes.TryGetValue(key,out var probe))
        {
            if (probes.Count >= 256) probes.Clear();
            probe = new Probe(); probes.Add(key,probe);
        }
        // Door/moving-obstacle state is resampled, while stationary geometry is amortized across frames.
        if (Time.time >= probe.next && rays < 12)
        {
            int n = Mathf.Min(4,12-rays); rays += n;
            probe.space.Trace(p,n); probe.space.Evaluate(null,0.15f,0.4f);
            probe.next = Time.time+0.15f;
        }
        return probe.space;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { probes.Clear(); frame=-1; solves=rays=0; }
    public static void ClearGeometryCache() => Reset();
}
