using UnityEngine;

// A television connected to the camcorder: its screen shows the finished picture (VHSPostFX renders a small copy
// of every frame into Feed while this screen is visible). A quad is laid over the set's glass for the picture.
[RequireComponent(typeof(TVDevice))]
public class LiveFeedTV : MonoBehaviour
{
    public static RenderTexture Feed { get; private set; }
    public static bool Wanted { get; private set; }

    TVDevice tv;
    Renderer quad;
    Material mat;

    void Start()
    {
        tv = GetComponent<TVDevice>();
        if (!Feed)
        {
            Feed = new RenderTexture(320, 240, 0, RenderTextureFormat.ARGB32) { name = "CamcorderFeed", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Feed.Create();
        }
        // find the glass: the "Emit_Screen" submesh of the set
        var mr = GetComponentInChildren<MeshRenderer>();
        var mf = mr ? mr.GetComponent<MeshFilter>() : null;
        var mesh = mf ? mf.sharedMesh : null;
        int sub = -1;
        if (mr) for (int i = 0; i < mr.sharedMaterials.Length; i++) if (mr.sharedMaterials[i] && mr.sharedMaterials[i].name.StartsWith("Emit_Screen")) sub = i;
        if (!mesh || !mesh.isReadable || sub < 0 || sub >= mesh.subMeshCount) { enabled = false; return; }
        var verts = mesh.vertices;
        var idx = mesh.GetIndices(sub);
        Transform t = mr.transform;
        // the glass faces the way the set faces (its Front); lay the picture just in front of the glass
        var front = transform.Find("Front");
        Vector3 normal = front ? front.forward : transform.forward;
        normal.y = 0f;
        normal.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, normal).normalized;
        float dMax = float.MinValue, rMin = float.MaxValue, rMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
        foreach (int i in idx)
        {
            Vector3 w = t.TransformPoint(verts[i]);
            dMax = Mathf.Max(dMax, Vector3.Dot(w, normal));
            float r = Vector3.Dot(w, right);
            rMin = Mathf.Min(rMin, r); rMax = Mathf.Max(rMax, r);
            yMin = Mathf.Min(yMin, w.y); yMax = Mathf.Max(yMax, w.y);
        }
        float wq = rMax - rMin, h = yMax - yMin;
        if (wq < 0.05f || h < 0.05f) { enabled = false; return; }
        Vector3 centre = normal * (dMax + 0.008f) + right * ((rMin + rMax) * 0.5f) + Vector3.up * ((yMin + yMax) * 0.5f);
        float w2 = wq;

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "LiveFeedScreen";
        Destroy(go.GetComponent<Collider>());
        go.transform.position = centre;
        go.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);   // quad faces -Z: show its front to the room
        go.transform.localScale = new Vector3(w2 * 0.92f, h * 0.92f, 1f);
        go.transform.SetParent(transform, true);
        quad = go.GetComponent<Renderer>();
        quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var template = SpawnAtTV.Instance ? SpawnAtTV.Instance.feedMaterial : null;
        if (!template) { enabled = false; Destroy(go); return; }
        mat = new Material(template) { mainTexture = Feed };
        quad.sharedMaterial = mat;
    }

    void Update()
    {
        if (!quad) return;
        bool on = tv && tv.IsOn;
        quad.enabled = on;
        // only spend the extra small pass while the screen could be in view
        Wanted = on && quad.isVisible;
    }

    void OnDestroy()
    {
        Wanted = false;
        if (mat) Destroy(mat);
    }
}
