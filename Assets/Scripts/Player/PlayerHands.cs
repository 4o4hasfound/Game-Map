using UnityEngine;

// What you carry: up to three bottles and the portable radio. Right mouse button (or G): throw a bottle where you
// look (it smashes where it lands, 72 dB: the listener goes there), or set the radio down (it switches itself on
// five seconds later and plays until it is smashed).
public class PlayerHands : MonoBehaviour
{
    public static PlayerHands Instance { get; private set; }
    public const int MaxBottles = 3;
    public int Bottles;
    public bool HasRadio;
    public Material glassMat, radioMat;
    void Awake() => Instance = this;

    public void UseBottle() { Bottles = Mathf.Max(0, Bottles - 1); }

    void Update()
    {
        if (Game.InMenu || !Game.Instance || Game.Instance.State != Game.Phase.Hotel || HideSpot.Inside) return;
        if (PlayerMotor.Instance && PlayerMotor.Instance.Locked) return;
        if (!(Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.G))) return;
        var cam = PlayerMotor.Instance.cameraTransform;
        if (Bottles > 0) { Throw(cam.position + cam.forward * 0.35f, cam.forward * 9.5f + Vector3.up * 1.6f); return; }
        if (HasRadio) PlaceRadio(cam);
    }

    public Bottle Throw(Vector3 from, Vector3 velocity)
    {
        Bottles = Mathf.Max(0, Bottles - 1);
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "Thrown bottle";
        go.transform.position = from;
        go.transform.localScale = new Vector3(0.07f, 0.13f, 0.07f);
        go.layer = Layers.Prop;
        if (glassMat) go.GetComponent<Renderer>().sharedMaterial = glassMat;
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.4f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = velocity;
        rb.angularVelocity = Random.insideUnitSphere * 10f;
        var b = go.AddComponent<Bottle>();
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("cloth"), from, 0.25f, 1.3f, 0.5f, 6f);
        if (Game.Instance) Game.Instance.Log("threw a bottle");
        return b;
    }

    public NoiseDevice PlaceRadio(Transform cam)
    {
        var fwd = cam.forward; fwd.y = 0; fwd.Normalize();
        var p = transform.position + fwd * 0.7f;
        if (Physics.Raycast(p + Vector3.up * 1f, Vector3.down, out var hit, 2f, (1 << Layers.Structure) | (1 << Layers.Prop), QueryTriggerInteraction.Ignore)) p = hit.point;
        HasRadio = false;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Portable radio (placed)";
        go.transform.position = p + Vector3.up * 0.09f;
        go.transform.rotation = Quaternion.LookRotation(-fwd);
        go.transform.localScale = new Vector3(0.3f, 0.18f, 0.1f);
        go.layer = Layers.Interactable;
        if (radioMat) go.GetComponent<Renderer>().sharedMaterial = radioMat;
        go.GetComponent<BoxCollider>().isTrigger = true;
        var d = go.AddComponent<NoiseDevice>();
        d.label = "portable radio";
        d.clips = new[] { "radio_station_1", "radio_station_3", "radio_station_4" };
        d.db = 64f; d.volume = 0.5f; d.duration = 45f; d.startDelay = 5f; d.uses = 1;
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("radio_click"), p, 0.3f, 1f, 0.5f, 8f);
        if (Hud.Instance) Hud.Instance.Announce("RADIO SET DOWN\nit switches on in 5 s", 2f);
        if (Game.Instance) Game.Instance.Log("set the radio down");
        return d;
    }
}
