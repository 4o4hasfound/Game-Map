using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Everything drawn on top of the tape picture: the VCR OSD, prompts, subtitles, papers, and the menus
// (title, camera consent, calibration, pause/settings, endings). Built in code with uGUI.
// The interface keeps out of the way (UiKit): a light sans-serif, thin grey bars that fade at both ends and show
// only when they matter: stamina (bottom centre), holding your breath, the camera light's battery and how visible
// you are (bottom left), a hold's progress and the door-easing pointer (under the prompt). The VCR display (PLAY,
// the date) keeps the camcorder's own type.
public class Hud : MonoBehaviour
{
    public static Hud Instance { get; private set; }
    Font mono, serif, hand;
    Canvas canvas;
    Text tl, br, centre, prompt, subtitle, caption, eye, paperTitle, paperBody, info, obj, notice, easeHint;
    UiKit.Bar stamina, breathBar, lightBar, seenBar, holdBar;
    RectTransform easeRoot, easePointer, easeBand;
    CanvasGroup easeGroup;
    float noticeUntil, staminaFullSince;
    GameObject paper, menu;
    Image fade;
    float centreUntil, subUntil, capUntil;
    int calibStep;
    float stepUntil;
    string calibMsg = "";
    bool faceLostShown;
    float lostSince = -1f;

    void Awake()
    {
        Instance = this;
        mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Lucida Console", "Courier New" }, 32);
        if (!GetComponent<MapView>()) gameObject.AddComponent<MapView>();
        if (!GetComponent<DoorEase>()) gameObject.AddComponent<DoorEase>();
        serif = Font.CreateDynamicFontFromOSFont(new[] { "Georgia", "Times New Roman" }, 32);
        hand = Font.CreateDynamicFontFromOSFont(new[] { "Segoe Print", "Ink Free", "Comic Sans MS", "Georgia" }, 32);
        var go = new GameObject("HUD");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 1f;
        go.AddComponent<GraphicRaycaster>();
        if (!FindAnyObjectByType<EventSystem>())
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
        tl = T(new Vector2(0, 1), new Vector2(60, -40), TextAnchor.UpperLeft, 30, mono);
        br = T(new Vector2(1, 0), new Vector2(-60, 40), TextAnchor.LowerRight, 26, mono);
        eye = T(new Vector2(1, 1), new Vector2(-60, -40), TextAnchor.UpperRight, 20, mono);
        centre = T(new Vector2(0.5f, 0.5f), new Vector2(0, 140), TextAnchor.MiddleCenter, 27, UiKit.Ui);
        prompt = T(new Vector2(0.5f, 0.5f), new Vector2(0, -90), TextAnchor.MiddleCenter, 19, UiKit.Ui);
        subtitle = T(new Vector2(0.5f, 0), new Vector2(0, 122), TextAnchor.LowerCenter, 23, UiKit.Ui);
        caption = T(new Vector2(0.5f, 0), new Vector2(0, 158), TextAnchor.LowerCenter, 16, UiKit.Ui);
        info = T(new Vector2(0, 0), new Vector2(60, 118), TextAnchor.LowerLeft, 15, UiKit.Ui);
        obj = T(new Vector2(0, 1), new Vector2(60, -84), TextAnchor.UpperLeft, 17, UiKit.Ui);
        obj.horizontalOverflow = HorizontalWrapMode.Wrap;
        obj.rectTransform.sizeDelta = new Vector2(620, 120);
        notice = T(new Vector2(0.5f, 1), new Vector2(0, -34), TextAnchor.UpperCenter, 15, UiKit.Ui);
        // bars: stamina and breath at the bottom centre, the light and how visible you are at the bottom left
        var root = canvas.transform;
        stamina = new UiKit.Bar(root, new Vector2(0.5f, 0f), new Vector2(0, 44), 300f);
        breathBar = new UiKit.Bar(root, new Vector2(0.5f, 0f), new Vector2(0, 84), 220f);
        lightBar = new UiKit.Bar(root, new Vector2(0f, 0f), new Vector2(60 + 120, 44), 240f);
        seenBar = new UiKit.Bar(root, new Vector2(0f, 0f), new Vector2(60 + 120, 84), 240f);
        holdBar = new UiKit.Bar(root, new Vector2(0.5f, 0.5f), new Vector2(0, -112), 200f, 4f, false);
        // easing a door: a faded track, the band to stay in, and the pointer
        var eg = new GameObject("Door ease", typeof(RectTransform));
        eg.transform.SetParent(root, false);
        easeRoot = eg.GetComponent<RectTransform>();
        easeRoot.anchorMin = easeRoot.anchorMax = new Vector2(0.5f, 0.5f);
        easeRoot.anchoredPosition = new Vector2(0, -142); easeRoot.sizeDelta = new Vector2(320, 24);
        easeGroup = eg.AddComponent<CanvasGroup>(); easeGroup.alpha = 0f; easeGroup.blocksRaycasts = false;
        var back = new GameObject("Backing", typeof(RectTransform));
        back.transform.SetParent(easeRoot, false);
        var brt = back.GetComponent<RectTransform>(); brt.anchoredPosition = new Vector2(0f, -8f); brt.sizeDelta = new Vector2(380f, 56f);
        var bimg = back.AddComponent<Image>(); bimg.sprite = UiKit.Rounded; bimg.type = Image.Type.Sliced; bimg.color = new Color(0.02f, 0.02f, 0.025f, 0.5f); bimg.raycastTarget = false;
        UiKit.Stroke(easeRoot, Vector2.zero, new Vector2(320, 5), new Color(0.8f, 0.8f, 0.78f, 0.38f));
        // the band: a lighter segment with crisp edges (it moves and narrows as you hold)
        var bg = new GameObject("Band", typeof(RectTransform));
        bg.transform.SetParent(easeRoot, false);
        easeBand = bg.GetComponent<RectTransform>(); easeBand.sizeDelta = new Vector2(64f, 8f);
        var bim = bg.AddComponent<Image>(); bim.color = new Color(0.94f, 0.94f, 0.9f, 0.42f); bim.raycastTarget = false;
        foreach (float side in new[] { -0.5f, 0.5f })
        {
            var tg = new GameObject("Edge", typeof(RectTransform));
            tg.transform.SetParent(easeBand, false);
            var trt = tg.GetComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = new Vector2(side + 0.5f, 0.5f);
            trt.anchoredPosition = Vector2.zero; trt.sizeDelta = new Vector2(1.5f, 12f);
            var tim = tg.AddComponent<Image>(); tim.color = new Color(0.94f, 0.94f, 0.9f, 0.9f); tim.raycastTarget = false;
        }
        var pg = new GameObject("Pointer", typeof(RectTransform));
        pg.transform.SetParent(easeRoot, false);
        easePointer = pg.GetComponent<RectTransform>(); easePointer.sizeDelta = new Vector2(3f, 22f);
        var pim = pg.AddComponent<Image>(); pim.color = new Color(1f, 0.85f, 0.6f, 1f); pim.raycastTarget = false;
        easeHint = UiKit.Label(easeRoot, new Vector2(0.5f, 0.5f), new Vector2(0, -18), 13, TextAnchor.UpperCenter, UiKit.TextColor, 400f);
        easeHint.text = "←  →   keep it in the middle";
        // paper
        paper = Panel(new Color(0.88f, 0.85f, 0.76f, 0.97f), new Vector2(760, 660));
        paperTitle = T(new Vector2(0.5f, 1), new Vector2(0, -40), TextAnchor.UpperCenter, 24, serif, paper.transform);
        paperTitle.color = new Color(0.15f, 0.12f, 0.1f);
        paperBody = T(new Vector2(0.5f, 0.5f), new Vector2(0, -10), TextAnchor.MiddleCenter, 24, hand, paper.transform);
        paperBody.color = new Color(0.12f, 0.1f, 0.12f);
        paperBody.rectTransform.sizeDelta = new Vector2(690, 540);
        paperBody.verticalOverflow = VerticalWrapMode.Truncate;
        paperBody.resizeTextForBestFit = true;
        paperBody.resizeTextMinSize = 12;
        paperBody.resizeTextMaxSize = 24;
        paperBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        var close = T(new Vector2(0.5f, 0), new Vector2(0, 26), TextAnchor.LowerCenter, 16, mono, paper.transform);
        close.text = "E / CLICK / ESC: CLOSE";
        close.color = new Color(0.3f, 0.28f, 0.25f);
        paper.SetActive(false);
        // fade
        var f = new GameObject("Fade");
        f.transform.SetParent(canvas.transform, false);
        fade = f.AddComponent<Image>();
        fade.color = new Color(0, 0, 0, 0);
        fade.raycastTarget = false;
        var frt = fade.rectTransform; frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
        f.transform.SetAsFirstSibling();
    }

    Text T(Vector2 anchor, Vector2 pos, TextAnchor align, int size, Font font, Transform parent = null)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent ? parent : canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(1100, 300);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.color = new Color(0.95f, 0.96f, 0.93f, 0.95f);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var sh = go.AddComponent<Shadow>();
        bool vcr = font == mono;
        sh.effectColor = new Color(0, 0, 0, vcr ? 0.7f : 0.45f);
        sh.effectDistance = vcr ? new Vector2(2, -2) : new Vector2(1, -1);
        if (!vcr) t.color = UiKit.TextColor;
        return t;
    }

    GameObject Panel(Color c, Vector2 size, Transform parent = null)
    {
        var go = new GameObject("Panel");
        go.transform.SetParent(parent ? parent : canvas.transform, false);
        var img = go.AddComponent<Image>();
        img.color = c;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        return go;
    }

    // ------------------------------------------------------------------ public API
    public void Announce(string text, float seconds) { centre.text = text; centreUntil = Time.unscaledTime + seconds; }
    // a small line at the top (the testing skip keys)
    public void Notice(string text) { notice.text = text; noticeUntil = Time.unscaledTime + 3f; }

    public void Subtitle(string text, Vector3 from, float seconds)
    {
        var p = Game.Instance ? Game.Instance.PlayerFeet : Vector3.zero;
        if (Vector3.Distance(p, from) > 30f) return;
        subtitle.text = text;
        subUntil = Time.unscaledTime + seconds;
    }

    public void Caption(string text, Vector3 from)
    {
        if (!Settings.Captions || !Game.Instance || !Game.Instance.PlayerCamera) return;
        var cam = Game.Instance.PlayerCamera.transform;
        if (Vector3.Distance(cam.position, from) > 30f) return;
        var local = cam.InverseTransformPoint(from);
        string dir = Mathf.Abs(local.y) > 2.5f ? (local.y > 0 ? "above" : "below") : local.z < 0 && Mathf.Abs(local.x) < Mathf.Abs(local.z) ? "behind" : local.x < 0 ? "left" : "right";
        caption.text = text.TrimEnd(']') + ", " + dir + "]";
        capUntil = Time.unscaledTime + 2.5f;
    }

    public void ShowPaper(string title, string body, bool handwritten)
    {
        paperTitle.text = title;
        paperBody.text = body.Replace("<s>", "").Replace("</s>", "");
        paperBody.font = handwritten ? hand : serif;
        paper.SetActive(true);
        paperOpenedAt = Time.unscaledTime;
        if (Game.Instance) Game.Instance.OpenPaper();
    }
    float paperOpenedAt;
    public void HidePaper() { paper.SetActive(false); }

    public void Tear(float seconds, bool strong) { if (VhsScreen.Instance) VhsScreen.Instance.Tear(seconds, strong); }

    // the end of the tape: the menu stays on top, the cursor stays free, and the keys work too
    public bool EndShown { get; private set; }
    public void ShowEnd(string title, string sub)
    {
        EndShown = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        BuildMenu(m =>
        {
            Label(m, title, 64, 140, mono);
            Label(m, sub, 26, 80, serif);
            Button(m, "PLAY AGAIN FROM RECEPTION  (ENTER)", 0, () => Game.Instance.RestartAtReception());
            Button(m, "START OVER  (R)", -60, () => Game.Instance.Restart());
            Button(m, "QUIT  (ESC)", -120, QuitGame);
        }, 0.75f);
    }

    public static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;     // in the editor, quitting stops Play mode
#else
        Application.Quit();
#endif
    }

    void EndScreenTick()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (menu) menu.transform.SetAsLastSibling();
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Game.Instance.RestartAtReception();
        else if (Input.GetKeyDown(KeyCode.R)) Game.Instance.Restart();
        else if (Input.GetKeyDown(KeyCode.Escape)) QuitGame();
    }

    // ------------------------------------------------------------------ menus
    void BuildMenu(System.Action<Transform> fill, float dim = 0.85f)
    {
        if (menu) Destroy(menu);
        menu = Panel(new Color(0, 0, 0, dim), new Vector2(4000, 3000));
        fill(menu.transform);
    }
    void CloseMenu() { if (menu) Destroy(menu); menu = null; }

    Text Label(Transform m, string s, int size, float y, Font font)
    {
        if (size < 60) font = UiKit.Ui;
        var t = T(new Vector2(0.5f, 0.5f), new Vector2(0, y), TextAnchor.MiddleCenter, size, font, m);
        t.text = s;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.rectTransform.sizeDelta = new Vector2(1000, 200);
        return t;
    }

    // a menu button: dark glass with rounded corners and a hairline edge, brighter under the pointer.
    // "LABEL: VALUE" shows the setting's name on the left and its value on the right; "[X] ..." gets a check box;
    // a key in brackets at the end ("(ENTER)") is shown as a quiet hint on the right
    Button Button(Transform m, string s, float y, System.Action click, float x = 0f, float w = 520f)
    {
        var go = Panel(Color.white, new Vector2(w, 42), m);
        go.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, y);
        var img = go.GetComponent<Image>();
        img.sprite = UiKit.Rounded; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 2f;
        var b = go.AddComponent<Button>();
        var colors = b.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0.06f);
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.15f);
        colors.selectedColor = colors.normalColor;
        colors.pressedColor = new Color(1f, 1f, 1f, 0.24f);
        colors.fadeDuration = 0.08f;
        b.colors = colors;
        b.onClick.AddListener(() => click());
        var edge = Panel(new Color(1f, 1f, 1f, 0.13f), new Vector2(w, 42), go.transform).GetComponent<Image>();
        edge.sprite = UiKit.Ring; edge.type = Image.Type.Sliced; edge.pixelsPerUnitMultiplier = 2f; edge.raycastTarget = false;

        string text = s, hint = null, value = null;
        bool? check = null;
        if (text.StartsWith("[X]") || text.StartsWith("[ ]")) { check = text.StartsWith("[X]"); text = text.Substring(3).Trim(); }
        int k = text.LastIndexOf("  (");
        if (k > 0 && text.EndsWith(")") && text.Length - k <= 10) { hint = text.Substring(k + 3, text.Length - k - 4); text = text.Substring(0, k).Trim(); }
        int c = text.IndexOf(": ");
        if (c > 0) { value = text.Substring(c + 2).Trim(); text = text.Substring(0, c); }
        bool left = value != null || check != null || hint != null;
        float pad = 20f + (check != null ? 26f : 0f);
        var t = T(new Vector2(left ? 0f : 0.5f, 0.5f), new Vector2(left ? pad : 0f, 0f), left ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, 18, UiKit.Ui, go.transform);
        t.text = UiKit.Sentence(text);
        t.rectTransform.sizeDelta = new Vector2(w - pad - 20f, 42);
        if (check != null)
        {
            var box = Panel(new Color(1f, 1f, 1f, check.Value ? 0.85f : 0.0f), new Vector2(14, 14), go.transform).GetComponent<Image>();
            box.sprite = UiKit.Rounded; box.type = Image.Type.Sliced; box.pixelsPerUnitMultiplier = 6f; box.raycastTarget = false;
            var brt = box.rectTransform; brt.anchorMin = brt.anchorMax = new Vector2(0f, 0.5f); brt.anchoredPosition = new Vector2(27f, 0f);
            var ring = Panel(new Color(1f, 1f, 1f, 0.55f), new Vector2(14, 14), box.transform).GetComponent<Image>();
            ring.sprite = UiKit.Ring; ring.type = Image.Type.Sliced; ring.pixelsPerUnitMultiplier = 6f; ring.raycastTarget = false;
        }
        string right = value != null ? UiKit.Sentence(value) : hint != null ? UiKit.Sentence(hint) : null;
        if (right != null)
        {
            var r = T(new Vector2(1f, 0.5f), new Vector2(-20f, 0f), TextAnchor.MiddleRight, value != null ? 17 : 14, UiKit.Ui, go.transform);
            r.text = right;
            r.color = value != null ? new Color(1f, 0.97f, 0.9f, 0.75f) : UiKit.Dim;
            r.rectTransform.sizeDelta = new Vector2(w * 0.6f, 42);
        }
        return b;
    }

    void ShowTitle()
    {
        BuildMenu(m =>
        {
            Label(m, "THE TAPE", 72, 190, mono);
            Label(m, "Headphones recommended. About 10 minutes.", 22, 120, serif);
            Button(m, "PLAY", 30, () => { ShowBrightness(() => { CloseMenu(); Game.Instance.GoConsent(); ShowConsent(); }); });
            Button(m, "SETTINGS", -30, () => ShowSettings(ShowTitle));
            Button(m, "QUIT", -90, QuitGame);
        }, 1f);
    }

    // brightness: three dark patches drawn through the same tape brightness; the darkest should be barely visible
    void ShowBrightness(System.Action next)
    {
        BuildMenu(m =>
        {
            Label(m, "BRIGHTNESS", 34, 220, mono);
            Label(m, "Adjust until the left square is only just visible.", 22, 170, serif);
            float[] levels = { 0.035f, 0.08f, 0.18f };
            for (int i = 0; i < 3; i++)
            {
                float v = Mathf.Clamp01(levels[i] * Settings.Brightness * 1.6f);
                var p = Panel(new Color(v, v, v, 1f), new Vector2(150, 150), m);
                p.GetComponent<RectTransform>().anchoredPosition = new Vector2(-180 + i * 180, 40);
            }
            Button(m, "DARKER", -80, () => { Settings.Brightness = Settings.Brightness - 0.1f; ShowBrightness(next); }, -140, 240);
            Button(m, "BRIGHTER", -80, () => { Settings.Brightness = Settings.Brightness + 0.1f; ShowBrightness(next); }, 140, 240);
            Label(m, $"{Settings.Brightness:0.0}", 20, -130, mono);
            Button(m, "CONTINUE", -180, () => next());
        }, 1f);
    }

    bool useCam = true, showFace = false;
    void ShowConsent()
    {
        bool classroom = Settings.Classroom;
        if (classroom) showFace = false;
        BuildMenu(m =>
        {
            Label(m, "THIS GAME CAN USE YOUR CAMERA", 34, 220, mono);
            Label(m, "It follows your head. Lean to peek around corners and out of hiding places.\n" +
                     "When you hide, keep still: the thing in the hotel hears you move.\n" +
                     "Pictures are processed on this computer only. Nothing is saved or sent. The game makes no network connections.\n" +
                     "The camera turns off when you pause or quit. You can play the whole game without it.", 20, 130, serif)
                .rectTransform.sizeDelta = new Vector2(980, 160);
            Button(m, (useCam ? "[X]" : "[ ]") + "  USE MY CAMERA (HEAD TRACKING)", 20, () => { useCam = !useCam; ShowConsent(); }, 0, 640);
            Button(m, (showFace ? "[X]" : "[ ]") + "  SHOW MY FACE INSIDE THE GAME", -36, () => { showFace = !showFace; ShowConsent(); }, 0, 640);
            Label(m, "(your face appears on the hotel's CCTV monitors and in its mirrors, worn by tape damage)", 16, -74, serif);
            Button(m, "CONTINUE", -130, () => { CloseMenu(); Game.Instance.ConsentDone(useCam, useCam && showFace); if (useCam) { calibStep = 0; stepUntil = 0; } });
            Button(m, "PLAY WITHOUT CAMERA", -186, () => { CloseMenu(); Game.Instance.ConsentDone(false, false); });
        });
    }

    void ShowSettings(System.Action back)
    {
        BuildMenu(m =>
        {
            Label(m, "SETTINGS", 40, 290, mono);
            float y = 220;
            Button(m, "LOW SCARE: " + (Settings.LowScare ? "ON" : "OFF"), y, () => { Settings.LowScare = !Settings.LowScare; ShowSettings(back); }); y -= 52;
            Button(m, "CLASSROOM MODE: " + (Settings.Classroom ? "ON" : "OFF"), y, () => { Settings.Classroom = !Settings.Classroom; if (Settings.Classroom) Settings.LowScare = true; ShowSettings(back); }); y -= 52;
            Button(m, "PICTURE: " + Settings.PictureNames[Settings.Picture], y, () => { Settings.Picture = (Settings.Picture + 1) % 3; ShowSettings(back); }); y -= 52;
            Button(m, $"BRIGHTNESS: {Settings.Brightness:0.0}  (click to raise, right side lowers)", y, () => { Settings.Brightness = Settings.Brightness >= 1.75f ? 0.6f : Settings.Brightness + 0.1f; ShowSettings(back); }); y -= 52;
            Button(m, $"MOUSE SENSITIVITY: {Settings.Sensitivity:0.0}", y, () => { Settings.Sensitivity = Settings.Sensitivity >= 2.9f ? 0.4f : Settings.Sensitivity + 0.2f; ShowSettings(back); }); y -= 52;
            Button(m, "SOUND CAPTIONS: " + (Settings.Captions ? "ON" : "OFF"), y, () => { Settings.Captions = !Settings.Captions; ShowSettings(back); }); y -= 52;
            Button(m, "VCR DISPLAY: " + (Settings.Osd ? "ON" : "OFF"), y, () => { Settings.Osd = !Settings.Osd; ShowSettings(back); }); y -= 52;
            Button(m, "LEAN WITH HEAD (CAMERA): " + (Settings.HeadLean ? "ON" : "OFF"), y, () => { Settings.HeadLean = !Settings.HeadLean; ShowSettings(back); }); y -= 52;
            Button(m, "AUDIO: " + (AudioEngine.Instance ? AudioEngine.Instance.ModeLabel : "-"), y, () => { if (AudioEngine.Instance) AudioEngine.Instance.CycleMode(); ShowSettings(back); }); y -= 64;
            Button(m, "BACK", y, () => back());
        });
    }

    void ShowPause()
    {
        BuildMenu(m =>
        {
            Label(m, "❚❚ PAUSE", 46, 170, mono);
            Button(m, "RESUME", 70, () => { Game.Instance.SetPaused(false); CloseMenu(); });
            Button(m, "SETTINGS", 14, () => ShowSettings(ShowPause));
            Button(m, "RESTART FROM RECEPTION", -42, () => Game.Instance.RestartAtReception());
            Button(m, "START OVER", -98, () => Game.Instance.Restart());
            Button(m, "QUIT", -154, QuitGame);
            Label(m, "WASD move   SHIFT run   C crouch (silent, uses stamina)   SPACE hold your breath   Q / R lean" + (Settings.UseCamera ? " (or your head)" : "") + "\n" +
                     "E / click: use   tap E on a door: slam it   hold E: ease it, ← → keep the pointer in the middle   wheel: nudge a door\n" +
                     "F: camera light   M: map (fire plans marked)   RIGHT CLICK / G: throw a bottle, set the radio down   F9 / F10: skip objective / part (testing)\n" +
                     "It hunts by sound: it follows your steps. Crouched you make none; running carries far. Carpet is quiet; marble, glass and water are loud.\n" +
                     "Its eyes are poor: it sees you only in strong light, or with your light on. Keep to the dark.", 15, -250, mono)
                .rectTransform.sizeDelta = new Vector2(1180, 140);
        });
    }

    IEnumerator FaceLost()
    {
        if (faceLostShown) yield break;
        faceLostShown = true;
        Game.Instance.SetPaused(true);
        BuildMenu(m =>
        {
            Label(m, "THE CAMERA CAN'T SEE YOUR FACE", 30, 100, mono);
            Label(m, "Check the light and that you're in front of the camera.", 20, 50, serif);
            Button(m, "TRY AGAIN", -20, () => { faceLostShown = false; CloseMenu(); Game.Instance.SetPaused(false); });
            Button(m, "CONTINUE WITHOUT CAMERA", -76, () => { Settings.UseCamera = false; FaceTracker.Instance?.Stop(); CloseMenu(); Game.Instance.SetPaused(false); });
        });
    }

    // ------------------------------------------------------------------ per frame
    Game.Phase lastPhase = (Game.Phase)(-1);
    bool lastPaused;
    void Update()
    {
        var g = Game.Instance;
        if (!g) return;
        if (g.State != lastPhase)
        {
            lastPhase = g.State;
            if (g.State == Game.Phase.Title) ShowTitle();
            if (g.State == Game.Phase.Prologue || g.State == Game.Phase.Hotel) { if (menu && !g.Paused) CloseMenu(); }
        }
        if (g.Paused != lastPaused)
        {
            lastPaused = g.Paused;
            if (g.Paused && !faceLostShown) ShowPause();
            if (!g.Paused && menu && !faceLostShown) CloseMenu();
        }
        if (g.State == Game.Phase.Calibrate) Calibrate();
        if (EndShown && g.State == Game.Phase.Ending) EndScreenTick();

        bool play = g.State == Game.Phase.Prologue || g.State == Game.Phase.Hotel;
        bool tapeOn = g.State == Game.Phase.Hotel;
        tl.text = Settings.Osd && tapeOn ? (g.Paused ? "❚❚ PAUSE" : "PLAY ►") : "";
        if (Settings.Osd && tapeOn)
        {
            float t = (Time.time - g.StartedAt) / 60f;
            int min = 41 + (int)t, hr = 11 + min / 60;
            br.text = $"OCT. 31 2003\n{(hr - 1) % 12 + 1}:{min % 60:00} {(hr >= 12 ? "AM" : "PM")}";
        }
        else br.text = "";
        var ft = FaceTracker.Instance;
        bool camOn = Settings.UseCamera && ft && ft.Running;
        eye.text = camOn && play ? "◉ CAMERA" + (!ft.FacePresent ? "  NO FACE" : HideSpot.Inside ? (HideSpot.Fidget > 1f ? "  MOVING" : "  STILL") : Mathf.Abs(ft.Lean) > 0.3f ? (ft.Lean < 0 ? "  LEAN L" : "  LEAN R") : "") : "";
        if (camOn && tapeOn && !g.Paused && !ft.FacePresent) { if (lostSince < 0f) lostSince = Time.unscaledTime; else if (Time.unscaledTime - lostSince > 4f && !faceLostShown) StartCoroutine(FaceLost()); }
        else lostSince = -1f;
        // objective (bright when it changes, then dim), what you carry, holding your breath
        float since = Time.unscaledTime - g.ObjectiveChangedAt;
        obj.text = tapeOn && !string.IsNullOrEmpty(g.Objective) ? "► " + g.Objective : g.State == Game.Phase.Prologue ? PrologueLine(g.PrologueBeat) : "";
        if (g.State == Game.Phase.Prologue) since = 0f;
        obj.color = new Color(1f, 0.95f, 0.8f, since < 8f ? 0.95f : 0.5f);
        var hands = PlayerHands.Instance;
        string inv = "";
        if (tapeOn && hands)
        {
            if (hands.Bottles > 0) inv += $"bottles ×{hands.Bottles}   right click: throw\n";
            if (hands.HasRadio) inv += "radio   right click: set down\n";
            if (g.HasKey("basement")) inv += "basement key\n";
            if (g.Catches > 0) inv += $"face {(3 - g.Catches) * 33 + 1}%\n";
        }
        info.text = inv;
        Bars(g, tapeOn && !g.Paused && !(MapView.Instance && MapView.Instance.Open));
        notice.color = new Color(UiKit.Dim.r, UiKit.Dim.g, UiKit.Dim.b, Mathf.Clamp01((noticeUntil - Time.unscaledTime) / 0.5f) * 0.8f);
        float cA = Mathf.Clamp01((centreUntil - Time.unscaledTime) / 0.6f);
        centre.color = new Color(1, 1, 1, cA * 0.95f);
        subtitle.color = new Color(1, 1, 1, Mathf.Clamp01((subUntil - Time.unscaledTime) / 0.4f));
        caption.color = new Color(0.9f, 0.9f, 0.8f, Mathf.Clamp01((capUntil - Time.unscaledTime) / 0.4f));

        // prompt
        var cur = Interactor.Instance ? (Interactor.Instance.Held ? Interactor.Instance.Held : Interactor.Instance.Current) : null;
        if (cur && play && !Game.InMenu && !string.IsNullOrEmpty(cur.Prompt))
        {
            prompt.text = cur.Prompt;
            float hp = cur.HoldProgress;
            bool showHold = hp >= 0f && Interactor.Instance.Held == cur;
            if (showHold) holdBar.Set(hp);
            holdBar.Show(showHold);
        }
        else
        {
            bool inBed = g.State == Game.Phase.Prologue && g.PrologueBeat == Game.Beat.InBed;
            prompt.text = HideSpot.Inside && play ? "E  get out     SPACE  hold your breath     keep still"
                        : inBed ? "Hold E   get out of bed"
                        : g.State == Game.Phase.Prologue && g.PrologueBeat == Game.Beat.Playing ? "E  skip" : "";
            if (inBed) holdBar.Set(g.GetUpProgress);
            holdBar.Show(inBed && g.GetUpProgress > 0.01f);
        }
        // while the tape plays the middle of your sight is the TV: the skip hint goes down to the bottom edge
        bool watchingTape = g.State == Game.Phase.Prologue && (g.PrologueBeat == Game.Beat.Playing || g.PrologueBeat == Game.Beat.Pulling);
        prompt.rectTransform.anchoredPosition = new Vector2(0f, watchingTape ? -318f : -90f);
        holdBar.Tick(Time.unscaledDeltaTime);

        // E or a click closes it here; Esc is handled by Game (closing the paper, not opening the pause menu)
        if (paper.activeSelf && Time.unscaledTime - paperOpenedAt > 0.25f && (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0)))
            g.ClosePaper();

        fade.color = new Color(0, 0, 0, 0f);
    }

    static string PrologueLine(Game.Beat b) => b switch
    {
        Game.Beat.InBed => "► Get up.",
        Game.Beat.Doorbell => "► Someone's knocking at the front door.",
        Game.Beat.Letter => "► Something came through the letterbox.",
        Game.Beat.HasTape => "► Put the tape in the VCR, under the TV in the living room   (E)",
        Game.Beat.Tracking => "► The picture rolls: steady it with ← →",
        _ => "",
    };

    // the bars: each shows only while it has something to say
    void Bars(Game g, bool on)
    {
        float dt = Time.unscaledDeltaTime;
        var m = PlayerMotor.Instance;
        // stamina: running and crouching use it; it fades out a second after it is full again
        if (m)
        {
            float st = m.Stamina / Mathf.Max(0.01f, m.staminaMax);
            bool busy = m.Running || m.Crouching || st < 0.995f;
            if (busy) staminaFullSince = Time.unscaledTime;
            stamina.Set(st, m.Exhausted ? "out of breath" : m.Silent ? "crouched · silent" : m.Running ? "running" : "stamina", "", m.Exhausted);
            stamina.Show(on && Time.unscaledTime - staminaFullSince < 1.2f);
        }
        else stamina.Show(false);
        breathBar.Set(Breath.Air, Breath.Holding ? "holding your breath" : "breath", "", Breath.Air < 0.25f);
        breathBar.Show(on && (Breath.Holding || Breath.Air < 0.99f));
        var cl = CameraLight.Instance;
        if (cl) lightBar.Set(cl.Battery, "light", cl.On ? "on  ·  F" : "off  ·  F", cl.Battery < 0.15f);
        lightBar.Show(on && cl);
        // how visible you are: it sees you only in strong light (Exposure)
        var st2 = Exposure.State;
        string word = st2 == Exposure.Seen.OwnLight ? "visible · your light" : st2 == Exposure.Seen.Visible ? "visible" : st2 == Exposure.Seen.Dim ? "dim" : "hidden";
        seenBar.Set(HideSpot.Inside ? 0f : Exposure.Meter, "visibility", word, st2 == Exposure.Seen.Visible || st2 == Exposure.Seen.OwnLight);
        seenBar.Show(on);
        stamina.Tick(dt); breathBar.Tick(dt); lightBar.Tick(dt); seenBar.Tick(dt);
        // easing a door
        var ease = DoorEase.Instance;
        bool easing = (on || g.State == Game.Phase.Prologue) && ease && ease.Active;
        if (easing) easeHint.text = "←  →   " + ease.Hint;
        if (easing)
        {
            easePointer.anchoredPosition = new Vector2((ease.Pointer - 0.5f) * 320f, 0f);
            easeBand.anchoredPosition = new Vector2((ease.BandCentre - 0.5f) * 320f, 0f);
            easeBand.sizeDelta = new Vector2(ease.BandHalf * 2f * 320f, 8f);
        }
        easeGroup.alpha = Mathf.MoveTowards(easeGroup.alpha, easing ? 1f : 0f, dt * (easing ? 8f : 3f));
    }

    // ------------------------------------------------------------------ calibration
    void Calibrate()
    {
        var ft = FaceTracker.Instance;
        if (!menu) calibStep = 0;
        if (ft == null || !ft.Running)
        {
            if (calibStep != -1)
            {
                calibStep = -1;
                BuildMenu(m =>
                {
                    Label(m, "NO CAMERA", 34, 80, mono);
                    Label(m, ft != null && ft.Error != "" ? ft.Error : "The camera could not be started.", 20, 30, serif);
                    Button(m, "PLAY WITHOUT CAMERA", -50, () => { CloseMenu(); Game.Instance.CalibrationDone(false); });
                });
            }
            return;
        }
        if (calibStep == 0)
        {
            calibStep = 1;
            stepUntil = Time.unscaledTime + 3.5f;
            calibMsg = "SIT NORMALLY AND LOOK AT THE SCREEN";
            BuildCalibMenu();
            ft.StartCollect(FaceTracker.CalibStep.Open);
        }
        if (calibStep == 1 && Time.unscaledTime > stepUntil)
        {
            if (!ft.EndCollect()) { calibMsg = "I CAN'T SEE YOUR FACE. MORE LIGHT, OR SIT IN FRONT OF THE CAMERA."; stepUntil = Time.unscaledTime + 3.5f; ft.StartCollect(FaceTracker.CalibStep.Open); BuildCalibMenu(); return; }
            calibStep = 2;
            stepUntil = Time.unscaledTime + 10f;
            calibMsg = "NOW LEAN YOUR HEAD TO THE LEFT (TO PEEK)";
            BuildCalibMenu();
        }
        if (calibStep == 2 && (ft.Lean < -0.6f || Time.unscaledTime > stepUntil))
        {
            leanLeftOk = ft.Lean < -0.6f;
            if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("ui_blip"), Game.Instance.PlayerFeet + Vector3.up * 1.6f, 0.5f, 1f, 1f, 10f);
            calibStep = 3;
            stepUntil = Time.unscaledTime + 10f;
            calibMsg = "AND TO THE RIGHT";
            BuildCalibMenu();
        }
        if (calibStep == 3 && (ft.Lean > 0.6f || Time.unscaledTime > stepUntil))
        {
            bool ok = leanLeftOk && ft.Lean > 0.6f;
            if (AudioEngine.Instance) AudioEngine.Instance.PlayAt(SoundBank.Pick("ui_blip"), Game.Instance.PlayerFeet + Vector3.up * 1.6f, 0.5f, 1.2f, 1f, 10f);
            calibStep = 4;
            calibMsg = ok ? "GOT IT." : "I COULDN'T SEE YOU LEAN.";
            BuildMenu(m =>
            {
                Label(m, calibMsg, 30, 130, mono);
                Label(m, ok ? "Lean to peek around corners and out of hiding places.\nWhen you hide, keep your head still: it hears you move." : "Leaning may not work here (light, angle, distance). You can still use Q and R, try again, or play without the camera.", 20, 60, serif)
                    .rectTransform.sizeDelta = new Vector2(900, 120);
                Button(m, ok ? "START" : "START ANYWAY", -20, () => { CloseMenu(); Game.Instance.CalibrationDone(true); });
                Button(m, "CALIBRATE AGAIN", -76, () => { calibStep = 0; });
                Button(m, "PLAY WITHOUT CAMERA", -132, () => { CloseMenu(); Game.Instance.CalibrationDone(false); });
            });
        }
        if (calibStep >= 1 && calibStep <= 3 && dotsRoot) UpdateDots(ft);
    }
    bool leanLeftOk;

    RectTransform dotsRoot;
    readonly List<RectTransform> dots = new List<RectTransform>();
    Text calibStatus;
    Text camStatus;
    void BuildCalibMenu()
    {
        BuildMenu(m =>
        {
            Label(m, "CALIBRATION", 30, 270, mono);
            Label(m, calibMsg, 26, 210, mono);
            var box = Panel(new Color(0.05f, 0.06f, 0.05f, 1f), new Vector2(300, 300), m);
            box.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -10);
            dotsRoot = box.GetComponent<RectTransform>();
            dots.Clear();
            for (int i = 0; i < 6; i++)
            {
                var d = Panel(i < 2 ? new Color(0.4f, 1f, 0.5f) : new Color(0.8f, 0.8f, 0.8f), new Vector2(10, 10), box.transform);
                dots.Add(d.GetComponent<RectTransform>());
            }
            calibStatus = Label(m, "", 18, -200, mono);
            Label(m, "Only these dots are shown. Your picture is not displayed or stored.", 16, -240, serif);
            camStatus = Label(m, "", 14, -265, mono);
        });
    }

    void UpdateDots(FaceTracker ft)
    {
        if (camStatus) camStatus.text = $"camera: {ft.CameraStatus}   light {ft.PictureLight * 100f:0}% (gain x{ft.Gain:0.0})   face score {ft.Score:0.00} (needs 0.50)\n{ft.DetectStatus}";
        for (int i = 0; i < dots.Count; i++)
        {
            var k = ft.Keypoints[i];
            dots[i].gameObject.SetActive(ft.FacePresent);
            dots[i].anchoredPosition = new Vector2((k.x - 0.5f) * 300f, (0.5f - k.y) * 300f);
        }
        if (calibStatus) calibStatus.text = ft.FacePresent ? $"FACE {ft.Score:0.00}   LEAN {ft.Lean:+0.0;-0.0;0.0}   LIGHT {ft.Brightness:0.00}" : "NO FACE";
    }
}
