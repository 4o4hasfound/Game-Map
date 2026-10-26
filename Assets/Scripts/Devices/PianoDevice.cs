using UnityEngine;

// Neglected piano / organ. It plays a slightly out-of-tune piece by itself from the start (a player-piano roll:
// see piano_tune_* in the recorded bank); E stops or restarts it. Number keys 1-0 play single keys while you look
// at it, each from its place along the keyboard, each with that key's own mistuning.
public class PianoDevice : Interactable
{
    public string tuning = "grand";      // grand, detuned, toy, organ
    public bool autoplay;                // from the layout (the lobby grand); every piano now plays by default
    public bool RollOn => rollOn;
    SpatialVoice roll;
    bool rollOn;
    readonly float[] keyCents = new float[10];

    public override string Prompt => rollOn ? "STOP THE PIANO" : "LET THE PIANO PLAY";
    public override string Hint => "1-0  PLAY KEYS";

    void Start()
    {
        float spread = tuning == "detuned" ? 30f : tuning == "toy" ? 20f : 12f;
        for (int k = 0; k < keyCents.Length; k++) keyCents[k] = Random.Range(-spread, spread);
        roll = MakeVoice("PianoSoundboard", tuning == "toy" ? 0.7f : 0.9f, 1.5f, 40f, 0f);
        SetRoll(true, Random.value);
    }

    void SetRoll(bool on, float startFraction = 0f)
    {
        rollOn = on;
        var clip = SoundBank.Get("piano_tune_" + tuning) ?? SoundBank.Get("piano_lounge");
        if (on && clip) roll.Play(clip, true, startFraction * clip.length);
        else roll.Stop();
    }

    public override void Use(PlayerMotor p) => SetRoll(!rollOn);

    public void Note(int k)
    {
        k = Mathf.Clamp(k, 0, 9);
        var pos = Front.position + Front.right * ((k - 4.5f) * 0.09f);
        float pitch = Mathf.Pow(2f, keyCents[k] / 1200f);
        if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Get($"note_{tuning}_{k}"), pos, 0.8f, pitch, 1.2f, 35f);
    }

    public override void WhileLooking(PlayerMotor p)
    {
        for (int k = 0; k < 10; k++)
            if (Input.GetKeyDown(k == 9 ? KeyCode.Alpha0 : KeyCode.Alpha1 + k)) Note(k);
    }
}
