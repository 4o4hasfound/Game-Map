using UnityEngine;

// One landing of the hotel's elevator: the car that waits here, its sliding car doors, the landing doors on the
// lobby side, floor indicators and the inside volume. Geometry is built by Build() (used by the editor builder).
public class ElevatorStop : MonoBehaviour
{
    public int levelIndex;
    public string label;
    public Transform car;
    public Transform carDoorL, carDoorR, landDoorL, landDoorR;
    public Vector3 doorAxis;      // world, along the door width
    public float doorWidth = 1.8f;
    public TextMesh insideIndicator, outsideIndicator;
    public BoxCollider inside;
    public Transform audioAnchor;

    Vector3 cL, cR, lL, lR;
    bool init;

    public float Openness { get; private set; }

    void Awake()
    {
        Init();
    }

    void Init()
    {
        if (init) return;
        init = true;
        cL = carDoorL.position;
        cR = carDoorR.position;
        lL = landDoorL.position;
        lR = landDoorR.position;
    }

    public void SetOpen(float v)
    {
        Init();
        Openness = Mathf.Clamp01(v);
        float s = doorWidth * 0.5f * Mathf.SmoothStep(0f, 1f, Openness) * 0.98f;
        // landing doors lead slightly (they are pulled by the car doors' clutch)
        float s2 = doorWidth * 0.5f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Openness * 1.05f)) * 0.98f;
        carDoorL.position = cL - doorAxis * s;
        carDoorR.position = cR + doorAxis * s;
        landDoorL.position = lL - doorAxis * s2;
        landDoorR.position = lR + doorAxis * s2;
    }

    public bool Contains(Vector3 p)
    {
        if (!inside) return false;
        var local = inside.transform.InverseTransformPoint(p) - inside.center;
        var h = inside.size * 0.5f;
        return Mathf.Abs(local.x) < h.x && local.y > -h.y - 0.3f && local.y < h.y && Mathf.Abs(local.z) < h.z;
    }

    public void SetIndicator(string s)
    {
        if (insideIndicator) insideIndicator.text = s;
        if (outsideIndicator) outsideIndicator.text = s;
    }

    // ------------------------------------------------------------------ construction
    public struct Mats
    {
        public Material panel, steel, brass, floor, ceiling, mirror, glow, button, text;
    }
    static Material textMaterial;

    static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material m, bool collider = true)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = size;
        if (m) g.GetComponent<Renderer>().sharedMaterial = m;
        if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
        else g.layer = 10;
        return g;
    }

    static TextMesh Label(string name, Transform parent, Vector3 localPos, Quaternion rot, string text, float size, Color col, Font font)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = rot;
        var t = g.AddComponent<TextMesh>();
        t.text = text;
        t.font = font;
        t.fontSize = 64;
        t.characterSize = size;
        t.anchor = TextAnchor.MiddleCenter;
        t.alignment = TextAlignment.Center;
        t.color = col;
        if (font) g.GetComponent<MeshRenderer>().sharedMaterial = textMaterial ? textMaterial : font.material;
        if (textMaterial) g.AddComponent<DepthText>();
        return t;
    }

    // doorCentre: centre of the opening at floor level; inward: from the lobby into the shaft
    public static ElevatorStop Build(Transform parent, int levelIndex, string label, Vector3 doorCentre, Vector3 inward, float doorWidth,
                                     Mats m, Font font, string[] floorLabels, out ElevatorButton[] buttons)
    {
        inward.y = 0f;
        inward.Normalize();
        textMaterial = m.text;
        var rootGo = new GameObject("Elevator_" + label);
        rootGo.transform.SetParent(parent, false);
        rootGo.transform.position = doorCentre;
        rootGo.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);     // local +Z = into the car
        var root = rootGo.transform;
        var stop = rootGo.AddComponent<ElevatorStop>();
        stop.levelIndex = levelIndex;
        stop.label = label;
        stop.doorWidth = doorWidth;
        stop.doorAxis = root.right;

        float W = doorWidth + 0.7f, D = 2.1f, Hh = 2.45f, z0 = 0.14f;
        var car = new GameObject("Car").transform;
        car.SetParent(root, false);
        stop.car = car;
        Box("CarFloor", car, new Vector3(0, -0.05f, z0 + D / 2), new Vector3(W, 0.1f, D), m.floor);
        Box("CarCeiling", car, new Vector3(0, Hh + 0.05f, z0 + D / 2), new Vector3(W, 0.1f, D), m.ceiling);
        Box("CarLightPanel", car, new Vector3(0, Hh - 0.01f, z0 + D / 2), new Vector3(W * 0.6f, 0.02f, D * 0.6f), m.glow, false);
        Box("CarBack", car, new Vector3(0, Hh / 2, z0 + D + 0.05f), new Vector3(W, Hh, 0.1f), m.panel);
        Box("CarMirror", car, new Vector3(0, 1.45f, z0 + D - 0.005f), new Vector3(W * 0.7f, 1.3f, 0.02f), m.mirror, false);
        for (int s = -1; s <= 1; s += 2)
        {
            Box("CarSide", car, new Vector3(s * (W / 2 + 0.05f), Hh / 2, z0 + D / 2), new Vector3(0.1f, Hh, D), m.panel);
            Box("CarRail", car, new Vector3(s * (W / 2 - 0.05f), 0.95f, z0 + D / 2), new Vector3(0.05f, 0.05f, D * 0.9f), m.brass, false);
            Box("CarFrontPost", car, new Vector3(s * (doorWidth / 2 + 0.2f), Hh / 2, z0 + 0.03f), new Vector3(0.4f, Hh, 0.08f), m.steel);
        }
        Box("CarBackRail", car, new Vector3(0, 0.95f, z0 + D - 0.05f), new Vector3(W * 0.9f, 0.05f, 0.05f), m.brass, false);
        Box("CarHeader", car, new Vector3(0, 2.25f + (Hh - 2.25f) / 2, z0 + 0.03f), new Vector3(W, Hh - 2.25f, 0.08f), m.steel);

        // sliding doors: car panels just inside, landing panels on the lobby side
        float pw = doorWidth / 2 + 0.03f;
        stop.carDoorL = Box("CarDoorL", root, new Vector3(-pw / 2, 1.1f, 0.08f), new Vector3(pw, 2.2f, 0.04f), m.steel).transform;
        stop.carDoorR = Box("CarDoorR", root, new Vector3(pw / 2, 1.1f, 0.08f), new Vector3(pw, 2.2f, 0.04f), m.steel).transform;
        stop.landDoorL = Box("LandingDoorL", root, new Vector3(-pw / 2, 1.1f, -0.02f), new Vector3(pw, 2.2f, 0.04f), m.brass).transform;
        stop.landDoorR = Box("LandingDoorR", root, new Vector3(pw / 2, 1.1f, -0.02f), new Vector3(pw, 2.2f, 0.04f), m.brass).transform;

        // inside volume
        var vol = new GameObject("Inside");
        vol.transform.SetParent(car, false);
        vol.transform.localPosition = new Vector3(0, 1.2f, z0 + D / 2);
        var bc = vol.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.size = new Vector3(W - 0.1f, 2.4f, D - 0.1f);
        vol.layer = 2;
        stop.inside = bc;
        var anchor = new GameObject("AudioAnchor").transform;
        anchor.SetParent(car, false);
        anchor.localPosition = new Vector3(0, 2.2f, z0 + D / 2);
        stop.audioAnchor = anchor;

        // light
        var lgo = new GameObject("CarLight");
        lgo.transform.SetParent(car, false);
        lgo.transform.localPosition = new Vector3(0, Hh - 0.15f, z0 + D / 2);
        var li = lgo.AddComponent<Light>();
        li.type = LightType.Point;
        li.range = 4.5f;
        li.intensity = 1.1f;
        li.color = new Color(1f, 0.85f, 0.65f);
        var lf = lgo.AddComponent<LightFlicker>();
        lf.pattern = LightFlicker.Pattern.Flicker;
        lf.circuit = "elevator";
        lf.fixture = car.Find("CarLightPanel").GetComponent<Renderer>();

        // indicators
        stop.insideIndicator = Label("IndicatorIn", car, new Vector3(0, 2.33f, z0 + 0.09f), Quaternion.Euler(0, 180, 0), label, 0.03f,
                                     new Color(1f, 0.25f, 0.1f), font);
        stop.outsideIndicator = Label("IndicatorOut", root, new Vector3(0, 2.45f, -0.09f), Quaternion.identity, label, 0.035f,
                                      new Color(1f, 0.3f, 0.1f), font);

        // one big button inside: ride to the other floor of this elevator. Dead cars get a dark panel.
        var list = new System.Collections.Generic.List<ElevatorButton>();
        Box("ButtonPanel", car, new Vector3(-(W / 2 - 0.02f), 1.25f, z0 + 0.45f), new Vector3(0.04f, 0.5f, 0.22f), m.brass, false);
        if (floorLabels.Length > 0)
        {
            var b = Box("Button_Ride", car, new Vector3(-(W / 2 - 0.05f), 1.25f, z0 + 0.45f), new Vector3(0.04f, 0.11f, 0.11f), m.button);
            b.layer = 0;
            var eb = b.AddComponent<ElevatorButton>();
            eb.label = floorLabels[0];
            eb.stop = stop;
            list.Add(eb);
            Label("ButtonLabel", car, new Vector3(-(W / 2 - 0.07f), 1.42f, z0 + 0.45f), Quaternion.Euler(0, -90, 0), floorLabels[0], 0.014f,
                  new Color(0.9f, 0.85f, 0.7f), font);
        }
        else
        {
            Label("OutOfOrder", root, new Vector3(0, 1.5f, -0.06f), Quaternion.identity, "OUT OF ORDER", 0.012f, new Color(0.9f, 0.85f, 0.7f), font);
            var dead = stop.carDoorL.gameObject.AddComponent<ElevatorButton>();
            dead.stop = stop;
            dead.broken = true;
            var dead2 = stop.carDoorR.gameObject.AddComponent<ElevatorButton>();
            dead2.stop = stop;
            dead2.broken = true;
        }
        buttons = list.ToArray();
        return stop;
    }
}
