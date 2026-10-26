using UnityEngine;

// The flashlight lying on the floor, still switched on: its beam rakes across the floor. E picks it up; from then
// on it is the hand-held light (CamcorderLight) and F switches it.
public class FlashlightPickup : Interactable
{
    CamcorderLight owner;
    Light beam;

    public override string Prompt => "PICK UP THE FLASHLIGHT";

    public static FlashlightPickup Create(Vector3 floor, Quaternion facing, CamcorderLight owner)
    {
        var root = new GameObject("FlashlightPickup");
        root.transform.SetPositionAndRotation(floor + Vector3.up * 0.022f, facing);
        root.layer = MaterialAcoustics.PropLayer;
        var p = root.AddComponent<FlashlightPickup>();
        p.owner = owner;
        var s = SpawnAtTV.Instance;
        Part(root.transform, new Vector3(0f, 0f, -0.02f), new Vector3(0.036f, 0.085f, 0.036f), s ? s.flashlightBody : null);
        Part(root.transform, new Vector3(0f, 0f, 0.085f), new Vector3(0.052f, 0.03f, 0.052f), s ? s.flashlightBody : null);
        Part(root.transform, new Vector3(0f, 0f, 0.116f), new Vector3(0.044f, 0.002f, 0.044f), s ? s.flashlightLens : null);
        // an easy target to grab
        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.01f, 0.03f);
        box.size = new Vector3(0.14f, 0.08f, 0.3f);
        box.isTrigger = true;
        // still lit, shining along the floor
        var lg = new GameObject("Beam");
        lg.transform.SetParent(root.transform, false);
        lg.transform.localPosition = new Vector3(0f, 0.01f, 0.12f);
        lg.transform.localRotation = Quaternion.Euler(-3f, 0f, 0f);
        p.beam = lg.AddComponent<Light>();
        if (owner && owner.lamp)
        {
            p.beam.type = LightType.Spot;
            p.beam.spotAngle = owner.lamp.spotAngle;
            p.beam.innerSpotAngle = owner.lamp.innerSpotAngle;
            p.beam.cookie = owner.lamp.cookie;
            p.beam.color = owner.lamp.color;
            p.beam.range = owner.lamp.range;
            p.beam.intensity = owner.lamp.intensity;
            p.beam.shadows = LightShadows.Soft;
        }
        return p;
    }

    static GameObject Part(Transform parent, Vector3 pos, Vector3 scale, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);     // cylinders lie along the flashlight's axis
        g.transform.localScale = scale;
        g.layer = MaterialAcoustics.PropLayer;
        if (m) g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    public override void Use(PlayerMotor player)
    {
        if (owner) owner.Grant(true);
        OneShot("cloth", transform.position + Vector3.up * 0.1f, 0.25f, 1f, 0.5f, 8f);
        Destroy(gameObject);
    }
}
