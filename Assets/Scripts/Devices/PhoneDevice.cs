using UnityEngine;

// Hotel telephone / payphone. It rings now and then; pick up with E and somebody talks to you (the voice plays from
// the handset at your right ear), then the line goes dead. Pick it up when it is not ringing and you get a dial tone.
public class PhoneDevice : Interactable
{
    public bool payphone;
    public bool rings = true;
    public float firstRing = 30f;
    public string voiceClip = "phone_voice_1";

    SpatialVoice bell, handset;
    bool ringing, offHook;
    float nextRing, ringUntil;

    public override string Prompt => offHook ? "HANG UP" : ringing ? "ANSWER THE PHONE" : "PICK UP THE PHONE";

    void Start()
    {
        bell = MakeVoice("PhoneBell", 0.9f, 1f, 35f, 0f);
        var go = new GameObject("Handset");
        go.AddComponent<AudioSource>();
        handset = go.AddComponent<SpatialVoice>();
        handset.headLocked = true;
        handset.minDistance = 0.05f;
        handset.sendLevel = 0.05f;
        handset.label = "Handset";
        go.SetActive(false);
        nextRing = rings ? Time.time + firstRing + Random.Range(0f, 10f) : float.MaxValue;
    }

    public override void Use(PlayerMotor p)
    {
        if (offHook)
        {
            offHook = false;
            handset.Stop();
            handset.gameObject.SetActive(false);
            OneShot("phone_hangup", transform.position, 0.8f, 1f, 0.5f, 12f);
            return;
        }
        offHook = true;
        OneShot("phone_pickup", transform.position, 0.8f, 1f, 0.5f, 12f);
        handset.transform.SetParent(Camera.main.transform, false);
        handset.transform.localPosition = new Vector3(0.11f, -0.04f, 0.02f);     // at the right ear
        handset.gameObject.SetActive(true);
        if (ringing)
        {
            ringing = false;
            bell.Stop();
            handset.volume = 0.7f;
            var clip = SoundBank.Get(voiceClip);
            handset.Play(clip, false, 0f);
            Invoke(nameof(DeadLine), clip ? clip.length + 0.3f : 6f);
        }
        else DeadLine();
        nextRing = Time.time + Random.Range(60f, 140f);
    }

    void DeadLine()
    {
        if (!offHook) return;
        handset.volume = 0.25f;
        handset.Play(SoundBank.Get("phone_dial_tone"), true, 0f);
    }

    void Update()
    {
        if (!offHook && !ringing && Time.time >= nextRing)
        {
            ringing = true;
            ringUntil = Time.time + Random.Range(18f, 30f);
            bell.Play(SoundBank.Get(payphone ? "payphone_ring" : "phone_ring"), true, 0f);
        }
        if (ringing && Time.time > ringUntil)
        {
            ringing = false;
            bell.Stop();
            nextRing = Time.time + Random.Range(50f, 120f);
        }
    }
}
