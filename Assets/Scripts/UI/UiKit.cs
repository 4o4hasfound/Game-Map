using UnityEngine;
using UnityEngine.UI;

// The look of the interface: quiet and modern, so it stays out of the picture. A light sans-serif, small and half
// transparent. In play, the bars are thin grey strokes that fade out at both ends. Menus and the map are crisp:
// dark glass with rounded corners and a hairline edge, text in sentence case.
// (The VCR's own display, PLAY and the date, keeps its blocky camcorder type: that belongs to the tape.)
public static class UiKit
{
    static Font ui;
    public static Font Ui => ui ? ui : ui = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Semilight", "Segoe UI", "Bahnschrift", "Arial" }, 32);

    public static readonly Color TextColor = new Color(0.93f, 0.93f, 0.9f, 0.85f);
    public static readonly Color Dim = new Color(0.93f, 0.93f, 0.9f, 0.5f);
    public static readonly Color Track = new Color(0.78f, 0.78f, 0.76f, 0.2f);
    public static readonly Color Fill = new Color(0.94f, 0.94f, 0.92f, 0.62f);
    public static readonly Color Warn = new Color(1f, 0.72f, 0.58f, 0.75f);

    static Sprite faded, soft;
    // a horizontal stroke: full in the middle, fading to nothing over the last fifth at each end
    public static Sprite Faded
    {
        get
        {
            if (faded) return faded;
            const int w = 256, h = 16;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float vy = 1f - Mathf.Pow(Mathf.Abs((y + 0.5f) / h * 2f - 1f), 6f);
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float vx = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.2f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - u) / 0.2f));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(255 * vx * vy));
                }
            }
            t.SetPixels32(px); t.Apply();
            return faded = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }
    }

    // a panel: solid inside, soft at every edge (sliced, so it scales to any size)
    public static Sprite Soft
    {
        get
        {
            if (soft) return soft;
            const int n = 64, edge = 20;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Min(x + 0.5f, n - x - 0.5f) / edge, dy = Mathf.Min(y + 0.5f, n - y - 0.5f) / edge;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(dx)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(dy));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * a));
                }
            t.SetPixels32(px); t.Apply();
            return soft = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(edge, edge, edge, edge));
        }
    }

    // crisp rounded rectangles for menus and panels: a fill, and a hairline outline (both sliced)
    static Sprite rounded, ring;
    public static Sprite Rounded => rounded ? rounded : rounded = RoundRect(false);
    public static Sprite Ring => ring ? ring : ring = RoundRect(true);
    static Sprite RoundRect(bool outline)
    {
        const int n = 48; const float r = 9f, line = 1.4f;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // signed distance to a rounded square (negative inside)
                float qx = Mathf.Abs(x + 0.5f - n / 2f) - (n / 2f - r), qy = Mathf.Abs(y + 0.5f - n / 2f) - (n / 2f - r);
                float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                float a = outline ? Mathf.Clamp01(0.5f - Mathf.Abs(d + line * 0.5f) + line * 0.5f) : Mathf.Clamp01(0.5f - d);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * a));
            }
        t.SetPixels32(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16));
    }

    // menu text in sentence case ("START OVER  (R)" -> "Start over"), with the acronyms kept
    public static string Sentence(string s)
    {
        if (string.IsNullOrEmpty(s) || s != s.ToUpperInvariant()) return s;
        var low = s.ToLowerInvariant();
        var words = low.Split(' ');
        string[] keep = { "vcr", "tv", "pa", "cctv", "ok", "hrtf", "3d", "fps" };
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i], core = w.Trim('(', ')', ',', ':');
            if (System.Array.IndexOf(keep, core) >= 0) words[i] = w.Replace(core, core.ToUpperInvariant());
        }
        low = string.Join(" ", words);
        int first = 0;
        while (first < low.Length && !char.IsLetter(low[first])) first++;
        return first < low.Length ? low.Substring(0, first) + char.ToUpperInvariant(low[first]) + low.Substring(first + 1) : low;
    }

    public static Text Label(Transform parent, Vector2 anchor, Vector2 pos, int size, TextAnchor align, Color? color = null, float width = 600f)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(width, size * 1.6f);
        var t = go.AddComponent<Text>();
        t.font = Ui; t.fontSize = size; t.alignment = align; t.raycastTarget = false;
        t.color = color ?? TextColor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        var sh = go.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.45f);
        sh.effectDistance = new Vector2(1f, -1f);
        return t;
    }

    public static Image Stroke(Transform parent, Vector2 pos, Vector2 size, Color c, bool filled = false)
    {
        var go = new GameObject("Stroke", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var im = go.AddComponent<Image>();
        im.sprite = Faded; im.color = c; im.raycastTarget = false;
        if (filled) { im.type = Image.Type.Filled; im.fillMethod = Image.FillMethod.Horizontal; im.fillOrigin = 0; }
        return im;
    }

    // a bar: a faded grey track, a lighter fill over it, a small caption above (left) and a value (right).
    // It fades in when it has something to say and out when it hasn't.
    public class Bar
    {
        public RectTransform root;
        public CanvasGroup group;
        public Image track, fill;
        public Text caption, value;
        float alpha, want;

        public Bar(Transform parent, Vector2 anchor, Vector2 pos, float width, float height = 4f, bool captions = true)
        {
            var go = new GameObject("Bar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            root = go.GetComponent<RectTransform>();
            root.anchorMin = root.anchorMax = anchor; root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = pos; root.sizeDelta = new Vector2(width, height);
            group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false;
            track = Stroke(root, Vector2.zero, new Vector2(width, height), Track);
            fill = Stroke(root, Vector2.zero, new Vector2(width, height), Fill, true);
            if (captions)
            {
                caption = Label(root, new Vector2(0f, 0.5f), new Vector2(width * 0.08f, height * 0.5f + 11f), 13, TextAnchor.LowerLeft, Dim, width);
                value = Label(root, new Vector2(1f, 0.5f), new Vector2(-width * 0.08f, height * 0.5f + 11f), 13, TextAnchor.LowerRight, Dim, width);
            }
        }

        public void Set(float v, string cap = null, string val = null, bool warn = false)
        {
            fill.fillAmount = Mathf.Clamp01(v);
            fill.color = warn ? Warn : Fill;
            if (caption && cap != null) caption.text = cap;
            if (value && val != null) value.text = val;
            if (value) value.color = warn ? Warn : TextColor;
        }

        public void Show(bool on) => want = on ? 1f : 0f;
        public void Tick(float dt)
        {
            alpha = Mathf.MoveTowards(alpha, want, dt * (want > alpha ? 5f : 1.6f));
            group.alpha = alpha;
            root.gameObject.SetActive(alpha > 0.001f || want > 0f);
        }
        public bool Visible => alpha > 0.01f;
    }
}
