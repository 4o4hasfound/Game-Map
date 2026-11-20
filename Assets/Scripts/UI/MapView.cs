using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The camcorder map (M): the floor you are on, drawn from what the camera has seen. Rooms you have walked through
// are filled in; once you have filmed the floor's fire plan every room is there, with its name. A mark shows where
// you are and which way you face. Doors are not drawn: the plan on the wall shows rooms, not routes.
// The fire plans of the part you are in are marked (a small diamond, dimmer once filmed): they are hard to find, and
// a plan on another floor of this part is named under the map.
public class MapView : MonoBehaviour
{
    public static MapView Instance { get; private set; }
    public bool Open { get; private set; }
    public int PlanMarks { get; private set; }          // fire plans marked on the floor shown (tests)
    Canvas canvas;
    RectTransform panel, roomsRoot, marker;
    Text title, legend;
    Font font;
    int builtFloor = int.MinValue, builtCount = -1;
    bool builtPlan;
    Rect bounds;
    float scale;
    const float W = 760f, H = 520f;

    void Awake()
    {
        Instance = this;
        font = UiKit.Ui;
        var go = new GameObject("Map canvas");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1280, 720);
        sc.matchWidthOrHeight = 1f;
        var pim = Img("Map", go.transform, new Color(0.025f, 0.028f, 0.032f, 0.94f), Vector2.zero, new Vector2(W + 80f, H + 150f));
        pim.sprite = UiKit.Rounded; pim.type = Image.Type.Sliced; pim.pixelsPerUnitMultiplier = 1f;
        var edge = Img("Edge", pim.transform, new Color(1f, 1f, 1f, 0.1f), Vector2.zero, pim.rectTransform.sizeDelta);
        edge.sprite = UiKit.Ring; edge.type = Image.Type.Sliced;
        panel = pim.rectTransform;
        title = Txt(panel, "", 22, new Vector2(0, H / 2f + 30f), TextAnchor.MiddleCenter);
        legend = Txt(panel, "", 14, new Vector2(0, -H / 2f - 30f), TextAnchor.MiddleCenter);
        roomsRoot = new GameObject("Rooms", typeof(RectTransform)).GetComponent<RectTransform>();
        roomsRoot.SetParent(panel, false);
        roomsRoot.sizeDelta = new Vector2(W, H);
        marker = Img("You", panel, new Color(0.95f, 0.85f, 0.4f), Vector2.zero, new Vector2(12f, 18f)).rectTransform;
        panel.gameObject.SetActive(false);
    }

    Image Img(string n, Transform parent, Color c, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var im = go.AddComponent<Image>();
        im.color = c; im.raycastTarget = false;
        return im;
    }

    Text Txt(Transform parent, string s, int size, Vector2 pos, TextAnchor a, float width = 700f)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(width, 60f);
        var t = go.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.alignment = a; t.text = s; t.raycastTarget = false;
        t.color = UiKit.TextColor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static string FloorName(int f) => f switch { -1 => "BASEMENT", 0 => "GROUND FLOOR", 1 => "GUEST FLOOR", 3 => "PENTHOUSE", 4 => "ROOF", _ => "" };

    public void SetOpen(bool v)
    {
        Open = v;
        panel.gameObject.SetActive(v);
        if (v) Rebuild(true);
    }

    void Update()
    {
        var g = Game.Instance;
        bool can = g && g.State == Game.Phase.Hotel && !Game.InMenu;
        if (can && Input.GetKeyDown(KeyCode.M)) SetOpen(!Open);
        if (Open && !can) SetOpen(false);
        if (!Open || !g) return;
        Rebuild(false);
        var p = g.PlayerFeet;
        marker.anchoredPosition = ToMap(p.x, p.z);
        marker.localRotation = Quaternion.Euler(0, 0, -g.player.Yaw);
    }

    Vector2 ToMap(float x, float z) => new Vector2((x - bounds.center.x) * scale, (z - bounds.center.y) * scale);

    void Rebuild(bool force)
    {
        var g = Game.Instance;
        int f = LevelLayout.FloorOf(g.PlayerFeet);
        bool plan = g.HasPlan(f);
        if (!force && f == builtFloor && plan == builtPlan && g.VisitedCount == builtCount) return;
        builtFloor = f; builtPlan = plan; builtCount = g.VisitedCount;
        for (int i = roomsRoot.childCount - 1; i >= 0; i--) Destroy(roomsRoot.GetChild(i).gameObject);
        var rooms = new List<LevelLayout.Room>();
        foreach (var r in LevelLayout.Rooms) if (r.floor == f || (r.twoStorey && r.floor + 1 == f)) rooms.Add(r);
        if (rooms.Count == 0) { title.text = "NO MAP HERE"; legend.text = ""; return; }
        bounds = rooms[0].r;
        foreach (var r in rooms) { bounds.xMin = Mathf.Min(bounds.xMin, r.r.xMin); bounds.yMin = Mathf.Min(bounds.yMin, r.r.yMin); bounds.xMax = Mathf.Max(bounds.xMax, r.r.xMax); bounds.yMax = Mathf.Max(bounds.yMax, r.r.yMax); }
        scale = Mathf.Min(W / bounds.width, H / bounds.height);
        int shown = 0;
        foreach (var r in rooms)
        {
            bool visited = g.HasVisited(r.name);
            if (!visited && !plan) continue;
            shown++;
            var c = ToMap(r.r.center.x, r.r.center.y);
            var size = new Vector2(r.r.width * scale, r.r.height * scale);
            Img("Wall " + r.name, roomsRoot, new Color(0.75f, 0.74f, 0.68f, 0.9f), c, size);
            var fill = visited ? (r.kind == "corridor" || r.kind == "gallery" ? new Color(0.24f, 0.25f, 0.24f) : new Color(0.3f, 0.3f, 0.28f)) : new Color(0.1f, 0.1f, 0.11f);
            Img("Room " + r.name, roomsRoot, fill, c, size - new Vector2(3f, 3f));
            if (size.x > 34f && size.y > 16f)
            {
                var t = Txt(roomsRoot, Short(r.name), size.x > 90f ? 13 : 10, c, TextAnchor.MiddleCenter, size.x - 4f);
                t.color = visited ? new Color(0.92f, 0.9f, 0.84f) : new Color(0.55f, 0.55f, 0.52f);
            }
        }
        // the fire plans of this part: marked where they hang on this floor, named if on another floor
        PlanMarks = 0;
        var elsewhere = new List<string>();
        foreach (var fp in FindObjectsByType<FloorPlan>(FindObjectsSortMode.None))
        {
            int pf = LevelLayout.FloorOf(fp.transform.position);
            bool filmed = g.HasPlan(fp.floor);
            if (pf != f) { if (!filmed && !elsewhere.Contains(FloorName(pf))) elsewhere.Add(FloorName(pf)); continue; }
            PlanMarks++;
            var mp = ToMap(fp.transform.position.x, fp.transform.position.z);
            var d = Img("Plan", roomsRoot, filmed ? new Color(0.6f, 0.75f, 0.85f, 0.45f) : new Color(0.62f, 0.86f, 1f, 0.95f), mp, new Vector2(11f, 11f));
            d.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
            var lt = Txt(roomsRoot, filmed ? "plan ✓" : "fire plan", 11, mp + new Vector2(0f, -15f), TextAnchor.MiddleCenter, 120f);
            lt.color = filmed ? UiKit.Dim : new Color(0.7f, 0.9f, 1f, 0.95f);
        }
        title.text = FloorName(f) + (plan ? "   ·   fire plan filmed" : "   ·   only where you have been");
        string plans = PlanMarks > 0 ? "◆ fire plan" : "";
        if (elsewhere.Count > 0) plans += (plans.Length > 0 ? "   ·   " : "") + "a fire plan on the " + string.Join(", ", elsewhere).ToLowerInvariant();
        legend.text = (shown == 0 ? "nothing yet: walk, or film a fire plan on the wall" : "light: rooms you have been in    dark: from the fire plan    yellow: you") +
                      (plans.Length > 0 ? "\n" + plans : "") + "        M: close";
        marker.SetAsLastSibling();
    }

    public static string Short(string n)
    {
        if (n.StartsWith("Room ")) return n.Substring(5);
        if (n.StartsWith("Suite ")) return n.Substring(6);
        return n.Replace("Owner's ", "").Replace("Penthouse ", "").Replace(" Room", "").Replace("Office", "Off.").ToUpperInvariant();
    }
}
