using System.Collections;
using UnityEngine;

// The prologue (Game runs the beats): three knocks, an envelope pushed under your door, the tape inside. Put it in
// the VCR (E): snow, then a rolling picture you steady with the arrow keys (the same pointer as easing a door). It
// plays the hotel lobby, filmed on a camcorder on a tripod: something walks up to the lens, and then it pulls you in. The VCR has a big aim
// zone, and E works whenever you roughly face it. E / Enter / Space skips the walk on the tape.
public class BedroomVcr : Interactable
{
    public static bool HasTape, Playing;
    public static float PlayingSince;
    public Transform tapeOnDesk;
    public override string Prompt => Playing ? "" : HasTape ? "Put the tape in" : "VCR (no tape)";
    public override bool Usable => !Playing && Game.Instance && Game.Instance.State == Game.Phase.Prologue;

    void Update()
    {
        var g = Game.Instance;
        if (!g || g.State != Game.Phase.Prologue || Playing || !HasTape || Game.InMenu || !Interactor.PressDown || !g.PlayerCamera) return;
        var cam = g.PlayerCamera.transform;
        var to = transform.position - cam.position;
        if (to.magnitude < 3f && Vector3.Angle(cam.forward, to) < 40f) Tap();
    }

    // a cassette in your hand, pushed into the slot
    IEnumerator TapeGoesIn()
    {
        var tape = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(tape.GetComponent<Collider>());
        tape.name = "Tape going in";
        var r = GetComponent<Renderer>();
        if (r) tape.GetComponent<Renderer>().sharedMaterial = r.sharedMaterial;
        tape.transform.localScale = new Vector3(0.19f, 0.025f, 0.1f);
        var front = transform.position + Vector3.forward * 0.32f + Vector3.up * 0.01f;
        var slot = transform.position + Vector3.forward * 0.12f + Vector3.up * 0.01f;
        var inside = transform.position + Vector3.up * 0.01f;
        tape.transform.SetPositionAndRotation(front + Vector3.up * 0.12f, transform.rotation);
        for (float t = 0f; t < 0.3f; t += Time.deltaTime) { tape.transform.position = Vector3.Lerp(front + Vector3.up * 0.12f, slot, t / 0.3f); yield return null; }
        for (float t = 0f; t < 0.35f; t += Time.deltaTime) { tape.transform.position = Vector3.Lerp(slot, inside, Mathf.SmoothStep(0f, 1f, t / 0.35f)); yield return null; }
        Destroy(tape);
        InteractAnim.Play(InteractAnim.Kind.Press, transform);
    }

    public override void Tap()
    {
        if (Playing) return;
        if (!HasTape) { if (Hud.Instance) Hud.Instance.Announce("NO TAPE", 1.5f); return; }
        Playing = true;
        PlayingSince = Time.time;
        StartCoroutine(TapeGoesIn());
        if (AudioEngine.Instance)
        {
            AudioEngine.Instance.PlayAt(SoundBank.Pick("vcr_insert"), transform.position, 0.7f, 1f, 0.5f, 10f);
            AudioEngine.Instance.PlayAt(SoundBank.Pick("tv_click"), transform.position + Vector3.up * 0.4f, 0.4f, 1f, 0.5f, 10f);
        }
        if (Game.Instance) Game.Instance.TapeInserted();
    }
}
