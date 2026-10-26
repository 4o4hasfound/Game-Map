using System.Collections.Generic;
using UnityEngine;

// Occasional events from real objects in the building, never from nowhere:
//  - a door somewhere drifts in the draught (its own hinge creak) or swings shut (its own latch thud)
//  - plaster and grit falling from a damaged ceiling (only in rooms with holes in the ceiling)
// Placed loops (drips at leaking joints, machinery, buzzing tubes) are separate AmbientEmitters at their sources.
// Silence between events is intentional: most of the time nothing happens.
public class AmbienceDirector : MonoBehaviour
{
    public Vector2 interval = new Vector2(65, 150);
    float next;
    readonly List<HingeDoor> doors = new List<HingeDoor>();
    readonly List<Vector3> ceilings = new List<Vector3>();

    void Start()
    {
        next = Time.time + Random.Range(45f, 90f);
        foreach (var d in FindObjectsByType<HingeDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!d.locked) doors.Add(d);
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (r.name.StartsWith("Decals_"))
            {
                // the "void" decals mark missing ceiling tiles
                var mats = r.sharedMaterials;
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                if (!mesh || !mesh.isReadable) continue; // defensively handle a batched decal
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i] || mats[i].name != "Decal_Void") continue;
                    var sm = mesh.GetSubMesh(i);
                    var verts = mesh.vertices;
                    var idx = mesh.GetIndices(i);
                    for (int k = 0; k + 5 < idx.Length; k += 12)
                        ceilings.Add(r.transform.TransformPoint(verts[idx[k]]));
                }
            }
    }

    void Update()
    {
        if (Time.time < next) return;
        next = Time.time + Random.Range(interval.x, interval.y);
        var eng = AudioEngine.Instance;
        if (!eng) return;
        Vector3 lis = eng.Listener.position;
        if (Random.value < 0.65f && doors.Count > 0)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var d = doors[Random.Range(0, doors.Count)];
                if (!d || !d.isActiveAndEnabled || d.Held) continue;
                float dist = Vector3.Distance(d.transform.position, lis);
                if (dist < 7f || dist > 35f) continue;
                if (d.Openness > 0.25f && Random.value < 0.35f) d.Slam();
                else d.Nudge(Random.Range(-25f, 25f));
                return;
            }
        }
        for (int attempt = 0; attempt < 20 && ceilings.Count > 0; attempt++)
        {
            var p = ceilings[Random.Range(0, ceilings.Count)];
            float dist = Vector3.Distance(p, lis);
            if (dist < 5f || dist > 30f) continue;
            eng.PlayAt(SoundBank.Pick("amb_debris"), p - Vector3.up * 0.3f, 0.12f, Random.Range(0.97f, 1.03f), 0.5f, 15f);
            return;
        }
    }
}
