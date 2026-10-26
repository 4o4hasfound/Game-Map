using UnityEngine;

// Jukebox: E selects the next record (with the changer mechanism); after the last one it stops.
public class JukeboxDevice : PoweredDevice
{
    public string[] tracks = { "juke_doowop", "juke_ballad", "juke_swing" };
    static readonly string[] Titles = { "\"MOONLIGHT ON THE BAY\"", "\"LAST DANCE AT THE MERIDIAN\"", "\"BLUE NOTE SHUFFLE\"" };

    SpatialVoice voice;
    int index = -1;
    float startAt = -1f;

    public override string Prompt => index < 0 ? "PLAY A RECORD" : index < tracks.Length - 1 ? "NEXT RECORD" : "STOP JUKEBOX";
    public override string Hint => index >= 0 ? "NOW PLAYING " + Titles[index % Titles.Length] : null;

    void Start()
    {
        voice = MakeVoice("JukeboxSpeaker", 1f, 1.2f, 35f, 0.4f);
        // already playing its first record when the player arrives
        index = 0;
        startAt = Time.time + Random.Range(0.3f, 2f);
    }

    public override void Use(PlayerMotor p)
    {
        if (!powered) return;
        voice.Stop();
        index++;
        if (index >= tracks.Length)
        {
            index = -1;
            OneShot("radio_click", transform.position + Vector3.up, 0.5f);
            return;
        }
        OneShot("juke_select", transform.position + Vector3.up * 0.8f, 0.8f, 1f, 1f, 20f);
        startAt = Time.time + 2.4f;
    }

    public override void SetPower(bool v)
    {
        base.SetPower(v);
        if (!v)
        {
            voice.Stop();
            index = -1;
        }
    }

    void Update()
    {
        if (startAt > 0f && Time.time >= startAt && index >= 0)
        {
            startAt = -1f;
            voice.Play(SoundBank.Get(tracks[index]), true, index == 0 ? Random.value * 30f : 0f);
        }
        // warped records: slow wow plus a little flutter, a touch flat
        if (voice && voice.IsPlaying)
            voice.Source.pitch = 0.988f * (1f + 0.005f * Mathf.Sin(Time.time * 2.4f) + 0.0015f * Mathf.Sin(Time.time * 11f));
    }
}
