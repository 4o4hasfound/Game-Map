using UnityEngine;

// Holding your breath (hold SPACE). Breathing is a noise like any other: calm breathing is barely audible, panting
// after a run carries several metres. While you hold your breath you make no breathing sound; hold it too long
// (10 s) and you gasp, loudly. Footsteps.cs plays the breaths and emits their noise.
public class Breath : MonoBehaviour
{
    public static Breath Instance { get; private set; }
    public static bool Holding { get; private set; }
    public static float Air { get; private set; } = 1f;          // 0..1
    public static bool? Override;                                // automated checks
    public const float HoldMax = 10f, Recover = 5f;
    float cooldown;
    public static event System.Action Gasped;

    void Awake() { Instance = this; Air = 1f; Holding = false; }

    void Update()
    {
        var g = Game.Instance;
        bool play = g && (g.State == Game.Phase.Hotel || g.State == Game.Phase.Prologue) && !Game.InMenu;
        bool want = Override ?? (play && Input.GetKey(KeyCode.Space));
        cooldown -= Time.deltaTime;
        if (cooldown > 0f) want = false;
        if (want && Air > 0f) Air = Mathf.Max(0f, Air - Time.deltaTime / HoldMax);
        else Air = Mathf.Min(1f, Air + Time.deltaTime / Recover);
        bool was = Holding;
        Holding = want && Air > 0f;
        if (was && want && Air <= 0f) Gasp();
    }

    void Gasp()
    {
        Holding = false;
        cooldown = 2.5f;
        var head = PlayerMotor.Instance ? PlayerMotor.Instance.cameraTransform.position : transform.position;
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("breath_run_in"), head + Vector3.down * 0.1f, 0.5f, 1f, 0.5f, 15f);
        NoiseBus.Emit(head, 42f, true, "gasp");
        if (Hud.Instance) Hud.Instance.Caption("[you gasp]", head);
        Gasped?.Invoke();
    }
}
