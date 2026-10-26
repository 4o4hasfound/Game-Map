using UnityEngine;

// Wind-up gramophone: the needle drops and the record spins up to speed; lifting it lets the record wind down in pitch.
public class GramophoneDevice : Interactable
{
    public string track = "waltz";
    SpatialVoice voice;
    bool on;
    float speed, targetSpeed;

    public override string Prompt => on ? "LIFT THE NEEDLE" : "PLAY THE RECORD";

    void Start()
    {
        voice = MakeVoice("GramophoneHorn", 1f, 1.2f, 30f, 0.5f);
        // The record is already turning when the player finds it.
        Toggle(false);
        speed = 1f;
    }

    void Toggle(bool sound = true)
    {
        on = !on;
        if (on)
        {
            if (sound) OneShot("gramophone_needle", transform.position + Vector3.up * 0.3f, 0.6f, 1f, 0.6f, 12f);
            voice.Play(SoundBank.Get("gramophone_" + track), true, Random.value * 30f, 0.4f);
            speed = 0.4f;
            targetSpeed = 1f;
        }
        else targetSpeed = 0f;
    }

    public override void Use(PlayerMotor p) => Toggle();

    void Update()
    {
        if (!voice || !voice.IsPlaying) return;
        speed = Mathf.MoveTowards(speed, targetSpeed, Time.deltaTime * (targetSpeed > speed ? 0.6f : 0.35f));
        // an off-centre, warped record: a slow pitch sway once per turn (78 rpm = 1.3 Hz) and a slower drift
        voice.Source.pitch = Mathf.Max(0.05f, speed * 0.985f * (1f + 0.006f * Mathf.Sin(Time.time * 8.2f) + 0.003f * Mathf.Sin(Time.time * 0.5f)));
        voice.volume = Mathf.Clamp01(speed * 1.4f);
        if (!on && speed <= 0.06f) voice.Stop();
    }
}
