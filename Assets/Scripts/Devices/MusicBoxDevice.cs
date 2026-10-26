using UnityEngine;

// Music box: E winds the spring (ratchet clicks); the tune plays and slows down as the spring runs out.
public class MusicBoxDevice : Interactable
{
    public string tune = "lullaby";
    SpatialVoice voice;
    float spring;

    public override string Prompt => "WIND THE MUSIC BOX";

    void Start()
    {
        voice = MakeVoice("MusicBox", 0.9f, 0.5f, 18f, 0f);
        // found wound up and playing; the spring runs down over a couple of minutes, E winds it again
        spring = tune == "lullaby_reverse" ? 0.35f : Random.Range(0.5f, 1f);
        if (spring > 0f) voice.Play(SoundBank.Get("musicbox_" + tune), true, Random.value * 10f, 0.8f);
    }

    public override void Use(PlayerMotor p)
    {
        OneShot("musicbox_wind", transform.position, 0.6f, 1f, 0.4f, 8f);
        spring = Mathf.Min(1f, spring + 0.5f);
        if (!voice.IsPlaying) voice.Play(SoundBank.Get("musicbox_" + tune), true, 0f, 1f);
    }

    void Update()
    {
        if (!voice || !voice.IsPlaying) return;
        spring -= Time.deltaTime / 120f;
        voice.Source.pitch = Mathf.Lerp(0.72f, 1f, Mathf.Sqrt(Mathf.Clamp01(spring)));
        if (spring <= 0f)
        {
            voice.Stop();
            spring = 0f;
        }
    }
}
