using UnityEngine;

// A person built from simple shapes (stand-in art): torso, hips, arms, legs, head. The head carries either the
// glitched tape blob (the listener: no face, so it cannot see) or your face from the webcam (your avatar on
// monitors and in mirrors).
// Animation is sampled at 12 frames a second, like worn tape, and stops completely while frozen.
public class Figure : MonoBehaviour
{
    public Transform head, torso, legL, legR, armL, armR;
    public Renderer headRenderer;
    public float phase;
    public bool Animate = true;
    float held, heldPhase;

    public static Figure Build(Transform parent, string name, Color coat, Color trousers, Material skinMat, Material headMat,
                               Material clothMat, int layer, float height = 1.75f)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        var f = root.gameObject.AddComponent<Figure>();
        float s = height / 1.75f;
        Material coatM = new Material(clothMat) { color = coat, name = "Fabric_Coat" };
        Material trouM = new Material(clothMat) { color = trousers, name = "Fabric_Trousers" };
        f.torso = Part(root, PrimitiveType.Capsule, new Vector3(0, 1.22f * s, 0), new Vector3(0.42f, 0.36f, 0.26f) * s, coatM, layer, "Torso");
        Part(root, PrimitiveType.Capsule, new Vector3(0, 0.95f * s, 0), new Vector3(0.36f, 0.2f, 0.24f) * s, trouM, layer, "Hips");
        f.legL = Pivot(root, new Vector3(-0.1f * s, 0.9f * s, 0), "LegL");
        f.legR = Pivot(root, new Vector3(0.1f * s, 0.9f * s, 0), "LegR");
        Part(f.legL, PrimitiveType.Capsule, new Vector3(0, -0.44f * s, 0), new Vector3(0.15f, 0.45f, 0.15f) * s, trouM, layer, "Leg");
        Part(f.legR, PrimitiveType.Capsule, new Vector3(0, -0.44f * s, 0), new Vector3(0.15f, 0.45f, 0.15f) * s, trouM, layer, "Leg");
        f.armL = Pivot(root, new Vector3(-0.26f * s, 1.45f * s, 0), "ArmL");
        f.armR = Pivot(root, new Vector3(0.26f * s, 1.45f * s, 0), "ArmR");
        Part(f.armL, PrimitiveType.Capsule, new Vector3(0, -0.33f * s, 0), new Vector3(0.11f, 0.34f, 0.11f) * s, coatM, layer, "Arm");
        Part(f.armR, PrimitiveType.Capsule, new Vector3(0, -0.33f * s, 0), new Vector3(0.11f, 0.34f, 0.11f) * s, coatM, layer, "Arm");
        Part(root, PrimitiveType.Cylinder, new Vector3(0, 1.55f * s, 0), new Vector3(0.1f, 0.06f, 0.1f) * s, skinMat, layer, "Neck");
        f.head = Pivot(root, new Vector3(0, 1.62f * s, 0), "Head");
        var h = Part(f.head, PrimitiveType.Sphere, new Vector3(0, 0.1f * s, 0.0f), new Vector3(0.21f, 0.25f, 0.23f) * s, headMat, layer, "HeadMesh");
        f.headRenderer = h.GetComponent<Renderer>();
        f.phase = Random.value * 10f;
        return f;
    }

    static Transform Pivot(Transform p, Vector3 pos, string n) { var t = new GameObject(n).transform; t.SetParent(p, false); t.localPosition = pos; return t; }

    static Transform Part(Transform p, PrimitiveType type, Vector3 pos, Vector3 scale, Material m, int layer, string n)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = n;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(p, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.layer = layer;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go.transform;
    }

    // speed in m/s; frozen figures don't move at all
    public void Tick(float speed, float dt)
    {
        if (!Animate) return;
        phase += speed * dt * 3.2f;
        held -= dt;
        if (held > 0f) return;
        held = 1f / 12f;               // held frames
        heldPhase = phase;
        float a = Mathf.Sin(heldPhase) * Mathf.Clamp01(speed / 1.5f) * 32f;
        if (legL) legL.localRotation = Quaternion.Euler(a, 0, 0);
        if (legR) legR.localRotation = Quaternion.Euler(-a, 0, 0);
        if (armL) armL.localRotation = Quaternion.Euler(-a * 0.6f, 0, 4f);
        if (armR) armR.localRotation = Quaternion.Euler(a * 0.6f, 0, -4f);
    }

    public void LookAt(Vector3 p, float maxYaw = 70f)
    {
        if (!head) return;
        var local = transform.InverseTransformPoint(p);
        float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -maxYaw, maxYaw);
        float pitch = Mathf.Clamp(-Mathf.Atan2(local.y - 1.65f, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -30f, 30f);
        head.localRotation = Quaternion.Euler(pitch, yaw, 0);
    }
}
