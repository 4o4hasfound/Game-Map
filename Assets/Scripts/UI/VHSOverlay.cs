using System.Text;
using UnityEngine;
using UnityEngine.UI;

// The VCR / camcorder on-screen display, drawn crisp on top of the degraded picture like a real VCR overlay.
//   PLAY ► / ►► FF / ❚❚ PAUSE, SP, tape counter, camcorder date and time, TRACKING, floor and room names,
//   interaction prompts, help (H) and the audio debug view (F1).
public class VHSOverlay : MonoBehaviour
{
    public static VHSOverlay Instance { get; private set; }

    public Interactor interactor;
    public Footsteps footsteps;

    Text tl, tr, bl, br, centre, prompt, help, debug, track;
    Font font;
    float tapeStart;
    float centreUntil;
    string lastRoom = "";
    bool showHelp, showDebug;
    float helpUntil;
    public bool Paused { get; private set; }

    void Awake()
    {
        Instance = this;
        font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Lucida Console", "Courier New" }, 32);
        var canvasGo = new GameObject("OSD");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 1f;
        tl = Make(canvasGo, new Vector2(0, 1), new Vector2(70, -46), TextAnchor.UpperLeft, 34);
        tr = Make(canvasGo, new Vector2(1, 1), new Vector2(-70, -46), TextAnchor.UpperRight, 34);
        bl = Make(canvasGo, new Vector2(0, 0), new Vector2(70, 46), TextAnchor.LowerLeft, 30);
        br = Make(canvasGo, new Vector2(1, 0), new Vector2(-70, 46), TextAnchor.LowerRight, 30);
        centre = Make(canvasGo, new Vector2(0.5f, 0.5f), new Vector2(0, 150), TextAnchor.MiddleCenter, 34);
        prompt = Make(canvasGo, new Vector2(0.5f, 0.5f), new Vector2(0, -110), TextAnchor.MiddleCenter, 24);
        track = Make(canvasGo, new Vector2(0.5f, 1), new Vector2(0, -46), TextAnchor.UpperCenter, 30);
        help = Make(canvasGo, new Vector2(0, 0.5f), new Vector2(70, 0), TextAnchor.MiddleLeft, 19);
        debug = Make(canvasGo, new Vector2(1, 0.5f), new Vector2(-40, 0), TextAnchor.MiddleRight, 15);
        tapeStart = Time.time - Random.Range(600f, 1500f);
        helpUntil = Time.time + 14f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Start()
    {
        if (HotelWorld.Instance != null)
        {
            HotelWorld.Instance.LevelChanged += lv => Announce(lv.label);
            if (HotelWorld.Instance.Current != null) Announce(HotelWorld.Instance.Current.label);
        }
    }

    Text Make(GameObject parent, Vector2 anchor, Vector2 pos, TextAnchor align, int size)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(900, 420);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.color = new Color(0.95f, 0.97f, 0.95f, 0.92f);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var sh = go.AddComponent<Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.6f);
        sh.effectDistance = new Vector2(2, -2);
        return t;
    }

    public void Announce(string text, float seconds = 4f)
    {
        centre.text = text;
        centreUntil = Time.time + seconds;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Paused = !Paused;
            Cursor.lockState = Paused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = Paused;
            AudioListener.pause = Paused;
        }
        if (Paused && Input.GetMouseButtonDown(0) && Input.mousePosition.y > 0)
        {
            // clicking anywhere in the window resumes
        }
        if (!Paused && Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (Input.GetKeyDown(KeyCode.H)) showHelp = !showHelp;
        if (Input.GetKeyDown(KeyCode.F1)) showDebug = !showDebug;

        float t = Time.time - tapeStart;
        int sec = (int)t;
        bool blink = (Time.time % 1f) < 0.6f;
        if (Paused) tl.text = blink ? "❚❚ PAUSE" : "";
        else if (VHSPostFX.FastForward > 0.2f) tl.text = "►► FF";
        else tl.text = "PLAY ►";
        tr.text = "SP";
        var eng = AudioEngine.Instance;
        string audio = eng ? (eng.Mode == AudioEngine.OutputMode.Headphones ? "HI-FI STEREO  ◖HP◗" : eng.ModeLabel) : "";
        bl.text = $"{sec / 3600}:{sec / 60 % 60:00}:{sec % 60:00}\n{audio}";
        int min = 42 + sec / 60;
        int hr = 11 + min / 60;
        br.text = $"OCT. 31 2003\n{(hr - 1) % 12 + 1}:{min % 60:00} {(hr >= 12 && hr < 24 ? "AM" : "PM")}";
        track.text = VHSPostFX.ExtraTracking > 0.35f && blink ? "TRACKING  ▮▮▮▮▮▯▯▯" : "";

        centre.color = new Color(1, 1, 1, Mathf.Clamp01((centreUntil - Time.time) / 0.8f) * 0.92f);

        // room names when entering a named room
        if (eng && eng.ListenerRoom != null && eng.ListenerRoom.name != lastRoom)
        {
            lastRoom = eng.ListenerRoom.name;
            if (eng.ListenerRoom.kind != "shaft" && Time.time > centreUntil - 3f)
            {
                prompt.text = "";
                Announce(lastRoom.ToUpperInvariant(), 2.5f);
            }
        }

        // interaction prompt
        var cur = interactor ? interactor.Current : null;
        if (cur && !Paused) prompt.text = "[E] " + cur.Prompt + (cur.Hint != null ? "\n" + cur.Hint : "");
        else prompt.text = "";

        help.text = showHelp || Time.time < helpUntil
            ? "WASD  WALK      SHIFT  HURRY\nCTRL/C  CROUCH  F  FLASHLIGHT\nE / CLICK  USE   MOUSE WHEEL  TUNE\n1-0  PLAY KEYS AT A PIANO\nF1  AUDIO DEBUG  F2  HEADPHONE TEST\nF3  AUDIO OUTPUT MODE\nH  THIS HELP   ESC  PAUSE"
            : "";
        debug.text = showDebug ? DebugText() : "";
    }

    string DebugText()
    {
        var eng = AudioEngine.Instance;
        if (!eng) return "";
        var sb = new StringBuilder();
        var room = eng.ListenerRoom;
        sb.AppendLine($"ROOM  {room?.name}  [{room?.acoustic}]");
        var ga = GeometricAcoustics.Instance;
        if (ga != null)
            sb.AppendLine($"SPACE  RT60 {ga.Space.Rt60:0.00}s (hi {ga.Space.Rt60High:0.00}s)  free path {ga.Space.MeanFree:0.0}m  absorb {ga.Space.Absorb:0.00}  open {ga.Space.Openness:0.00}");
        if (ReverbBus.Instance) sb.AppendLine(ReverbBus.Instance.Describe);
        sb.AppendLine($"OUTPUT  {eng.ModeLabel}   {AudioSettings.outputSampleRate} Hz");
        if (footsteps) sb.AppendLine($"FLOOR  {footsteps.LastSurface}");
        sb.AppendLine("-- audible voices (gain dB / az / dist / path)");
        var list = new System.Collections.Generic.List<SpatialVoice>();
        foreach (var v in AudioEngine.Voices) if (v && v.IsPlaying && v.Audible) list.Add(v);
        list.Sort((a, b) => b.LastGainDb.CompareTo(a.LastGainDb));
        for (int i = 0; i < Mathf.Min(14, list.Count); i++)
        {
            var v = list[i];
            string n = v.label ?? v.name;
            if (n.Length > 22) n = n.Substring(0, 22);
            sb.AppendLine($"{n,-22} {v.LastGainDb,6:0} {v.LastAzimuth,5:0}° {v.LastDistance,5:0.0}m  {v.LastPath}");
        }
        return sb.ToString();
    }
}
