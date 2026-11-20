using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Shadows are the most expensive thing on the GPU here: a lamp's shadow is six renders of the room around it. Of the
// room lamps that cast shadows (the flickering ones: their shadows jump), only the few nearest you do at any moment,
// with soft edges and a sharper map than before; the rest light without shadows. Your camera light always casts its
// own, and brief flashes (lightning, a live cable's arc) keep theirs. Checked four times a second.
public class ShadowBudget : MonoBehaviour
{
    public int budget = 3;
    public float maxDistance = 14f;
    readonly List<Light> casters = new List<Light>();
    readonly List<UniversalAdditionalLightData> extra = new List<UniversalAdditionalLightData>();
    readonly List<(float d, int i)> order = new List<(float, int)>();
    float next;
    public static int Casting { get; private set; }      // how many room lamps cast shadows right now (the autotest reads it)
    public static int Managed { get; private set; }

    void Start()
    {
        foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (l.shadows == LightShadows.None || !l.GetComponentInParent<Lamp>()) continue;
            casters.Add(l);
            extra.Add(l.GetComponent<UniversalAdditionalLightData>());
            l.shadows = LightShadows.None;
        }
        Managed = casters.Count;
    }

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.25f;
        var g = Game.Instance;
        var cam = g && g.PlayerCamera ? g.PlayerCamera.transform.position : Camera.main ? Camera.main.transform.position : Vector3.zero;
        order.Clear();
        for (int i = 0; i < casters.Count; i++)
        {
            var l = casters[i];
            if (!l || !l.isActiveAndEnabled || l.intensity < 0.05f) continue;
            float d = Vector3.Distance(cam, l.transform.position);
            if (d > Mathf.Min(maxDistance, l.range + 2f)) continue;
            order.Add((d, i));
        }
        order.Sort((a, b) => a.d.CompareTo(b.d));
        var on = new HashSet<int>();
        for (int k = 0; k < order.Count && k < budget; k++) on.Add(order[k].i);
        Casting = on.Count;
        for (int i = 0; i < casters.Count; i++)
        {
            var l = casters[i];
            if (!l) continue;
            var want = on.Contains(i) ? LightShadows.Soft : LightShadows.None;
            if (l.shadows == want) continue;
            l.shadows = want;
            if (want != LightShadows.None && extra[i]) extra[i].additionalLightsShadowResolutionTier = 1;   // medium (512)
        }
    }
}
