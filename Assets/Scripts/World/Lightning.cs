using System.Collections.Generic;
using UnityEngine;

// The storm outside. Every 20-50 s lightning flashes through the boarded windows: spot lights outside each window
// (shadow casting, so the planks throw stripes across the floor) stutter on for a fraction of a second, and the
// thunder rolls in a moment later, closer or further. In the basement you only hear it.
public class Lightning : MonoBehaviour
{
    public static Lightning Instance { get; private set; }
    readonly List<Light> lights = new List<Light>();
    float next, flashUntil, nextFlicker, thunderAt = -1f, thunderVol;
    bool anyOn;
    public bool Flashing => Time.time < flashUntil;

    void Awake() => Instance = this;
    public int LightCount => lights.Count;
    public bool AnyLit => lights.Exists(l => l && l.enabled);

    // the screenshot tour: hold the flash lit for a moment so a frame can be captured
    float holdUntil;
    public void Hold(float seconds) { holdUntil = Time.time + seconds; flashUntil = holdUntil; anyOn = true; foreach (var l in lights) if (l) { l.enabled = true; l.intensity = 70f; } }

    // tests / the screenshot tour: flash now
    public void Flash(float seconds)
    {
        flashUntil = Time.time + seconds;
        nextFlicker = 0f;
        thunderAt = Time.time + 0.8f;
        thunderVol = 0.6f;
        next = Time.time + seconds + 30f;
    }

    void Start()
    {
        foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include))
            if (l.name == "Lightning light") { lights.Add(l); l.enabled = false; }
        next = Time.time + Random.Range(12f, 25f);
    }

    void Update()
    {
        var g = Game.Instance;
        bool play = g && (g.State == Game.Phase.Hotel || g.State == Game.Phase.Prologue);
        if (play && Time.time >= next)
        {
            flashUntil = Time.time + Random.Range(0.25f, 0.6f);
            next = flashUntil + Random.Range(20f, 50f);
            float distance = Random.Range(0.3f, 1f);           // 0 close, 1 far
            thunderAt = Time.time + Mathf.Lerp(0.3f, 3f, distance);
            thunderVol = Mathf.Lerp(0.7f, 0.25f, distance);
        }
        bool on = Time.time < flashUntil;
        if (Time.time < holdUntil) return;
        if (on && Time.time >= nextFlicker)
        {
            nextFlicker = Time.time + Random.Range(0.03f, 0.09f);
            bool lit = Random.value < 0.6f;
            float k = Random.Range(0.6f, 1.2f);
            // only the windows near you light up (the rest are dark or in another part of the building)
            var gp = g ? g.PlayerFeet : Vector3.zero;
            foreach (var l in lights) if (l) { l.enabled = lit && (gp - l.transform.position).sqrMagnitude < 40f * 40f; l.intensity = 70f * k; }
            anyOn = lit;
        }
        if (!on && anyOn) { foreach (var l in lights) if (l) l.enabled = false; anyOn = false; }
        if (thunderAt > 0f && Time.time >= thunderAt)
        {
            thunderAt = -1f;
            var ae = AudioEngine.Instance;
            if (ae && g)
            {
                // outside, above you; muffled through the building
                var p = g.PlayerFeet + new Vector3(Random.Range(-8f, 8f), 9f, Random.Range(-8f, 8f));
                int pfl = LevelLayout.FloorOf(g.PlayerFeet);
                float underground = pfl == -1 ? 0.4f : pfl == 4 ? 1.35f : 1f;      // muffled underground, right overhead on the roof
                // a real recording (CC0, see AUDIO_CREDITS.md): a close strike is the short sharp crack, a far one the long roll
                var takes = SoundBank.Variants("thunder");
                var clip = takes.Length > 1 ? (thunderVol > 0.5f ? takes[1] : takes[0]) : SoundBank.Pick("thunder");
                if (clip) ae.PlayAt(clip, p, thunderVol * underground, Random.Range(0.93f, 1.04f), 4f, 80f);
                else ae.PlayAt(SoundBank.Pick("amb_door_slam_far"), p, thunderVol * underground, Random.Range(0.32f, 0.42f), 4f, 60f);
            }
        }
    }
}
