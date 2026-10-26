using UnityEngine;

// Tube radio. E switches it on/off, the mouse wheel (or Q / R) tunes across the dial (FM 88-108). Between stations
// you get static; near a station it fades in with a little drift. Stations keep "broadcasting" while you are away
// from them: tuning back joins the programme where it would be now.
public class RadioDevice : PoweredDevice
{
    public int station;
    public float frequency = 94.7f;
    static readonly float[] Freqs = { 89.3f, 92.1f, 95.6f, 99.9f, 103.5f, 106.7f };
    static readonly string[] Names = { "ARCHIVE PIANO I", "SERVICE CHANNEL", "ARCHIVE PIANO II", "NIGHT PIANO", "COUNTDOWN EN", "COUNTDOWN FR" };

    SpatialVoice music, noise;
    bool on;
    int playing = -1;

    public override string Prompt => on ? "TURN OFF RADIO" : "TURN ON RADIO";
    public override string Hint => on ? $"FM {frequency:0.0}  {NearestName()}   < WHEEL / Q R >" : null;

    void Start()
    {
        frequency = Freqs[Mathf.Clamp(station, 0, Freqs.Length - 1)] + Random.Range(-0.3f, 0.3f);
        music = MakeVoice("RadioProgramme", 0f, 0.8f, 22f, 0.35f);
        noise = MakeVoice("RadioStatic", 0f, 0.8f, 22f, 0.35f);
        // On from the start, somewhere on the dial; the player can switch it off.
        if (powered) Invoke(nameof(AutoOn), Random.Range(0.2f, 1.5f));
    }

    void AutoOn() => SwitchOn(true, false);

    void SwitchOn(bool v, bool click = true)
    {
        on = v && powered;
        if (click) OneShot("radio_click", transform.position + Vector3.up * 0.2f, 0.5f, 1f, 0.5f, 8f);
        if (on)
        {
            noise.Play(SoundBank.Get("radio_static"), true, Random.value * 5f);
            playing = -1;
        }
        else
        {
            noise.Stop();
            music.Stop();
            playing = -1;
        }
    }

    public override void SetPower(bool v)
    {
        base.SetPower(v);
        if (!v) SwitchOn(false, false);
    }

    public override void Use(PlayerMotor p) => SwitchOn(!on);

    public override void WhileLooking(PlayerMotor p)
    {
        if (!on) return;
        float w = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(w) > 0.001f) frequency = Mathf.Clamp(frequency + w * 4f, 88f, 108f);
        if (Input.GetKey(KeyCode.Q)) frequency = Mathf.Max(88f, frequency - Time.deltaTime * 2f);
        if (Input.GetKey(KeyCode.R)) frequency = Mathf.Min(108f, frequency + Time.deltaTime * 2f);
    }

    int Nearest(out float off)
    {
        int best = 0;
        off = 99f;
        for (int i = 0; i < Freqs.Length; i++)
        {
            float d = Mathf.Abs(frequency - Freqs[i]);
            if (d < off) { off = d; best = i; }
        }
        return best;
    }

    string NearestName()
    {
        int n = Nearest(out float off);
        return off < 0.35f ? Names[n] : "";
    }

    void Update()
    {
        if (!on || music == null) return;
        int n = Nearest(out float off);
        float clarity = 1f - Mathf.SmoothStep(0f, 1f, off / 0.6f);
        float drift = 1f + 0.08f * Mathf.Sin(Time.time * 0.7f) * (1 - clarity);
        if (n != playing && clarity > 0.02f)
        {
            var clip = SoundBank.Get("radio_station_" + n);
            if (clip) music.Play(clip, true, Time.time);   // the station has been on air all along
            playing = n;
        }
        music.volume = clarity * 0.9f * drift;
        // a worn set: the programme wavers slightly flat (wow), and drifts when off-station
        float wow = 0.985f * (1f + 0.004f * Mathf.Sin(Time.time * 2.9f) + 0.002f * Mathf.Sin(Time.time * 0.37f));
        music.Source.pitch = wow * (1f + (frequency - Freqs[n]) * 0.01f * (1 - clarity));
        noise.volume = Mathf.Lerp(0.55f, 0.07f, clarity);
    }
}
