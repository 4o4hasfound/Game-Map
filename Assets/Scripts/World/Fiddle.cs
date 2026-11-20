using System.Collections;
using UnityEngine;

// Everything in your house can be used, for the fun of it: eat the fruit, open the fridge and drink the soda, put the
// kettle on, make toast, run the microwave with nothing in it, switch the lamps, flick through the encyclopaedia,
// sink into the sofa, warm your hands at the fire, ring your own doorbell. Each thing has a prompt, a recorded sound
// (BigSoundBank, see AUDIO_CREDITS.md) and a line you think, cycled one per use. Food gets eaten (gone after `uses`),
// doors and drawers swing or slide open and shut (with what's inside appearing), lamps switch, appliances run.
public class Fiddle : Interactable
{
    public string prompt = "Look";
    [TextArea] public string[] lines = new string[0];
    public string sound; public float volume = 0.5f, pitch = 1f;
    public string sound2; public float sound2Delay; public float sound2Volume = 0.5f;   // a pop, a bell, a sip, later
    public string runSound; public float runSeconds;      // the kettle boiling, the microwave running (it stops after)
    public bool consume;                                    // eaten / drunk: gone after `uses`
    public int uses = 1;
    public Lamp lamp;                                       // switches this lamp
    public Lamp glowWhileRunning;                           // the stove ring, the microwave's light
    public Transform swing; public Vector3 swingAxis = Vector3.up; public float swingAngle = 100f;
    public Transform slide; public Vector3 slideBy;
    public string openPrompt = "Open", closePrompt = "Close";
    public string openSound, closeSound;
    public GameObject[] whileOpen = new GameObject[0];       // what's inside (the fridge's food and light)
    public MonitorScreen screen;                             // the TV: a burst of snow
    public InteractAnim.Kind anim = InteractAnim.Kind.Nudge;  // what it does when used (food: bites, then taken; drinks: sips)
    public GameObject whileRunning;                          // the kettle's steam, the tap's water: on while it runs
    public Transform[] popUp = new Transform[0];             // the toast: down in the slots while it runs, up when done
    public float popHeight = 0.07f;
    Vector3[] popDown;
    bool popped;
    public SpatialVoice music; public string musicClip;     // the radio: on and off (it starts on)
    public bool musicOn;
    static readonly System.Collections.Generic.List<Fiddle> radios = new System.Collections.Generic.List<Fiddle>();
    bool open; int used; float moveT = 1f, busyUntil;
    Quaternion r0; Vector3 p0;
    public bool IsOpen => open;
    public int Used => used;

    void Start()
    {
        if (swing) r0 = swing.localRotation;
        if (slide) p0 = slide.localPosition;
        popDown = new Vector3[popUp.Length];
        for (int i = 0; i < popUp.Length; i++) if (popUp[i]) popDown[i] = popUp[i].localPosition;
        if (whileRunning) whileRunning.SetActive(false);
        foreach (var g in whileOpen) if (g) g.SetActive(false);
        if (music) { radios.Add(this); if (musicOn) StartMusic(); }
    }
    void OnDestroy() { radios.Remove(this); }

    void StartMusic()
    {
        var clip = SoundBank.Pick(musicClip);
        if (music && clip) { music.Play(clip, true); musicOn = true; }
    }
    void StopMusic() { if (music) music.Stop(); musicOn = false; }
    // the tape pulls you in: the radio goes quiet
    public static void SilenceRadios() { foreach (var r in radios) if (r) r.StopMusic(); }
    public static void RadiosOn() { foreach (var r in radios) if (r && r.isActiveAndEnabled) r.StartMusic(); }

    public override string Prompt => music ? (musicOn ? "Switch the radio off" : "Switch the radio on") : lamp ? (lamp.on ? "Switch it off" : "Switch it on") : (swing || slide) ? (open ? closePrompt : openPrompt) : prompt;
    public override bool Usable => Time.time >= busyUntil;

    public override void Tap()
    {
        if (!Usable) return;
        var ae = AudioEngine.Instance;
        if (lamp) lamp.Set(!lamp.on);
        if (music) { if (musicOn) StopMusic(); else StartMusic(); }
        if (swing || slide)
        {
            open = !open; moveT = 0f;
            if (open) foreach (var g in whileOpen) if (g) g.SetActive(true);
            string s = open ? (openSound ?? sound) : (closeSound ?? sound);
            if (ae && !string.IsNullOrEmpty(s)) ae.PlayAt(SoundBank.Pick(s), transform.position, volume, pitch * Random.Range(0.97f, 1.03f), 0.5f, 12f);
            if (!open) StartCoroutine(HideInsideSoon());
        }
        else if (ae && !string.IsNullOrEmpty(sound)) ae.PlayAt(SoundBank.Pick(sound), transform.position, volume, pitch * Random.Range(0.97f, 1.03f), 0.5f, 12f);
        if (!string.IsNullOrEmpty(sound2)) StartCoroutine(Later());
        if (runSeconds > 0f) StartCoroutine(Run());
        if (screen && !BedroomVcr.Playing) StartCoroutine(Snow());
        if (lines != null && lines.Length > 0 && Hud.Instance && (!(swing || slide) || open))
            Hud.Instance.Subtitle(lines[used % lines.Length], transform.position, 3.4f);
        used++;
        if (Game.Instance) Game.Instance.Log("fiddled with " + name);
        bool drink = sound == "drink" || sound2 == "drink" || anim == InteractAnim.Kind.Drink;
        var mouth = drink ? InteractAnim.Kind.Drink : InteractAnim.Kind.Bite;
        if (consume && used >= uses)
        {
            // the last bite (or sip), and then it's gone
            busyUntil = float.MaxValue;
            InteractAnim.Play(mouth, transform, () => { if (this) InteractAnim.Play(InteractAnim.Kind.Take, transform, () => { if (this) gameObject.SetActive(false); }); });
        }
        else if (consume) { busyUntil = Mathf.Max(busyUntil, Time.time + InteractAnim.Duration(mouth)); InteractAnim.Play(mouth, transform); }
        else if (!(swing || slide)) { busyUntil = Mathf.Max(busyUntil, Time.time + InteractAnim.Duration(anim)); InteractAnim.Play(anim, transform); }
    }

    IEnumerator HideInsideSoon()
    {
        // what's inside disappears once the door has shut (so you can't take it through the closed door)
        yield return new WaitForSeconds(0.45f);
        if (!open) foreach (var g in whileOpen) if (g) g.SetActive(false);
    }

    IEnumerator Later()
    {
        yield return new WaitForSeconds(sound2Delay);
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick(sound2), transform.position, sound2Volume, Random.Range(0.97f, 1.03f), 0.5f, 12f);
    }

    IEnumerator Run()
    {
        busyUntil = Mathf.Max(busyUntil, Time.time + runSeconds);
        var v = AudioEngine.Instance && !string.IsNullOrEmpty(runSound) ? AudioEngine.Instance.PlayAt(SoundBank.Pick(runSound), transform.position, volume, 1f, 0.6f, 14f) : null;
        if (glowWhileRunning) glowWhileRunning.Set(true);
        ParticleSystem ps = null;
        if (whileRunning)
        {
            whileRunning.SetActive(true);
            ps = whileRunning.GetComponent<ParticleSystem>();
            if (ps) ps.Play(true);
        }
        if (popUp.Length > 0 && popped) StartCoroutine(Pop(false));
        yield return new WaitForSeconds(runSeconds);
        if (v) v.Stop();
        if (glowWhileRunning) glowWhileRunning.Set(false);
        // steam thins out and stops (what's in the air drifts away); water just stops
        if (ps) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        else if (whileRunning) whileRunning.SetActive(false);
        if (popUp.Length > 0) StartCoroutine(Pop(true));
    }

    // the toast: pushed down into the slots, or popped up out of them (a jump past the top, then settled)
    IEnumerator Pop(bool up)
    {
        float T = up ? 0.22f : 0.18f;
        for (float e = 0f; e <= T; e += Time.deltaTime)
        {
            float x = Mathf.Clamp01(e / T);
            float k = up ? (x < 0.45f ? Mathf.SmoothStep(0f, 1.35f, x / 0.45f) : Mathf.Lerp(1.35f, 1f, (x - 0.45f) / 0.55f)) : 1f - Mathf.SmoothStep(0f, 1f, x);
            for (int i = 0; i < popUp.Length; i++) if (popUp[i]) popUp[i].localPosition = popDown[i] + Vector3.up * popHeight * k;
            yield return null;
        }
        popped = up;
    }

    IEnumerator Snow()
    {
        bool wasOn = screen.on;
        screen.on = true; screen.Refresh();
        for (float t = 0; t < 0.9f; t += Time.deltaTime) { screen.Signal(1f, Random.value * 0.3f); yield return null; }
        if (!BedroomVcr.Playing) { screen.Signal(0f, 0f); screen.on = wasOn; screen.Refresh(); }
    }

    IEnumerator Gone()
    {
        yield return new WaitForSeconds(0.12f);
        gameObject.SetActive(false);
    }

    void Update()
    {
        if (moveT >= 1f) return;
        moveT = Mathf.Min(1f, moveT + Time.deltaTime / 0.45f);
        float k = Mathf.SmoothStep(0f, 1f, open ? moveT : 1f - moveT);
        if (swing) swing.localRotation = r0 * Quaternion.AngleAxis(swingAngle * k, swingAxis);
        if (slide) slide.localPosition = p0 + slideBy * k;
    }
}
