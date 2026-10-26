using System.Collections.Generic;
using UnityEngine;

// Keeps hundreds of lamps affordable: only the nearest few cast (soft) shadows, far lamps are switched off.
// On a GPU that cannot keep up (measured GPU frame time over budget for a while) the number of shadow casters and
// the lamp distance step down, and come back when there is headroom again.
public class LightManager : MonoBehaviour
{
    public int shadowCasters = 4;
    public float cullDistance = 38f;
    float next;
    int casters = -1;
    float cull, gpuMs, overFor, underFor;
    readonly FrameTiming[] timing = new FrameTiming[1];
    public static string QualityReport { get; private set; } = "";
    readonly List<(LightFlicker l, float d)> sorted = new List<(LightFlicker, float)>();

    void Update()
    {
        AdaptToGpu();
        if (Time.time < next) return;
        next = Time.time + 0.25f;
        var cam = Camera.main;
        if (!cam) return;
        Vector3 p = cam.transform.position;
        sorted.Clear();
        foreach (var l in LightFlicker.All)
        {
            if (!l || !l.Light) continue;
            float d = Vector3.Distance(p, l.transform.position);
            l.Culled = d > cull + l.Light.range;
            sorted.Add((l, d - (l.shadowsWanted ? 6f : 0f)));
        }
        sorted.Sort((a, b) => a.d.CompareTo(b.d));
        for (int i = 0; i < sorted.Count; i++)
        {
            var l = sorted[i].l;
            var want = i < casters && sorted[i].d < 18f ? LightShadows.Soft : LightShadows.None;
            if (l.Light.shadows != want) l.Light.shadows = want;
        }
    }

    void AdaptToGpu()
    {
        if (casters < 0) { casters = shadowCasters; cull = cullDistance; }
        FrameTimingManager.CaptureFrameTimings();
        float ms = FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].gpuFrameTime > 0 ? (float)timing[0].gpuFrameTime
                 : Time.unscaledDeltaTime * 1000f;     // no GPU timer: whole frame time
        gpuMs = Mathf.Lerp(gpuMs, ms, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.5f));
        float budget = 1000f / (Application.targetFrameRate > 0 ? Application.targetFrameRate : 60);
        if (gpuMs > budget * 0.95f) { overFor += Time.unscaledDeltaTime; underFor = 0f; }
        else if (gpuMs < budget * 0.75f) { underFor += Time.unscaledDeltaTime; overFor = 0f; }
        else { overFor = 0f; underFor = 0f; }
        if (overFor > 1f)
        {
            overFor = 0f;
            if (casters > 1) casters--;
            else cull = Mathf.Max(cullDistance * 0.6f, cull - 4f);
        }
        else if (underFor > 4f)
        {
            underFor = 0f;
            if (cull < cullDistance) cull = Mathf.Min(cullDistance, cull + 4f);
            else if (casters < shadowCasters) casters++;
        }
        QualityReport = $"gpu {gpuMs:0.0} ms  shadows {casters}/{shadowCasters}  lamps {cull:0} m";
    }
}
