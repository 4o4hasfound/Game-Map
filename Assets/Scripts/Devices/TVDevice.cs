using UnityEngine;

// CRT television: E switches channels (static, a show, the news, off). The screen glow and the light it casts on the
// room flicker with the picture.
public class TVDevice : PoweredDevice
{
    public string channel = "static";
    public bool startOn = true;
    public bool liveFeed;          // the spawn TV: shows what the camcorder sees (LiveFeedTV); E switches it off / on
    static readonly string[] Channels = { "static", "show", "news", "off" };

    SpatialVoice voice;
    int index;
    Renderer screen;
    LightFlicker glow;
    MaterialPropertyBlock mpb;
    int screenIndex = -1;
    Color screenColor;

    public override string Prompt => Channels[index] == "off" ? "TURN ON TV" : liveFeed ? "TURN OFF TV" : "CHANGE CHANNEL";
    public bool IsOn => powered && Channels[index] != "off";

    void Start()
    {
        voice = MakeVoice("TVSpeaker", 0.8f, 1f, 22f, 0.45f);
        screen = GetComponentInChildren<Renderer>();
        mpb = new MaterialPropertyBlock();
        if (screen)
        {
            var mats = screen.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] && mats[i].name.StartsWith("Emit_Screen"))
                {
                    screenIndex = i;
                    screenColor = mats[i].GetColor("_EmissionColor");
                }
        }
        foreach (var l in LightFlicker.All)
            if (l.pattern == LightFlicker.Pattern.Tv && Vector3.Distance(l.transform.position, transform.position) < 3.5f) glow = l;
        index = System.Array.IndexOf(Channels, channel == "computer" ? "show" : channel);
        if (index < 0) index = 0;
        if (!startOn) index = 3;
        Apply(false);
    }

    void Apply(bool click)
    {
        if (click) OneShot("tv_click", transform.position + Vector3.up * 0.3f, 0.7f, 1f, 0.6f, 14f);
        string ch = powered ? Channels[index] : "off";
        if (ch == "off") voice.Stop();
        else
        {
            string clip = liveFeed || ch == "static" ? "amb_tv_static" : (channel == "computer" && ch == "show") ? "tv_computer" : "tv_" + ch;
            voice.volume = liveFeed ? 0.1f : 0.8f;     // a camera input: only the faint hiss of the set
            voice.Play(SoundBank.Get(clip), true, Time.time);
        }
        if (glow)
        {
            glow.enabled = ch != "off";
            if (ch == "off" && glow.Light) glow.Light.enabled = false;
        }
    }

    public override void Use(PlayerMotor p)
    {
        if (liveFeed) index = Channels[index] == "off" ? 0 : 3;
        else index = (index + 1) % Channels.Length;
        Apply(true);
    }

    public override void SetPower(bool v)
    {
        base.SetPower(v);
        Apply(false);
    }

    void Update()
    {
        if (screenIndex < 0) return;
        bool on = IsOn;
        float f = on && !liveFeed ? 0.6f + 0.4f * Mathf.PerlinNoise(Time.time * 9f, 0.3f) : on ? 0.08f : 0f;   // the feed quad is the picture
        screen.GetPropertyBlock(mpb, screenIndex);
        mpb.SetColor("_EmissionColor", screenColor * f);
        screen.SetPropertyBlock(mpb, screenIndex);
    }
}
