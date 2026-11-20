using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Lamps that cast no shadow light everything in their range, through walls and floors: a sconce on a shared wall
// threw a pool of light on the floor of the room behind it. URP rendering layers stop that. Rooms joined by an
// open arch or a railing (the lobby and its galleries, 207 and 209) form one group; groups that share a wall or a
// floor get different layers; every surface and prop takes the layer(s) of the room(s) it is in, and every lamp
// lights only its own room's layer (plus layer 0, which the people made at runtime use). The storm outside and the
// camera light keep all layers: they cast shadows.
public static partial class TapeBuilder
{
    static void IsolateLights()
    {
        var rooms = LevelLayout.Rooms.Where(r => r.floor != 2).ToList();
        int n = rooms.Count;
        var idx = new Dictionary<LevelLayout.Room, int>();
        for (int i = 0; i < n; i++) idx[rooms[i]] = i;
        // rooms you can see into through an open arch or a railing: a lamp lights its own room and these
        var links = Enumerable.Range(0, n).Select(_ => new HashSet<int>()).ToArray();
        foreach (var o in LevelLayout.Openings)
        {
            if (o.kind != LevelLayout.Gap.Opening && o.kind != LevelLayout.Gap.Railing) continue;
            var c = new Vector3(o.Mid.x, LevelLayout.FloorY(o.floor) + 1f, o.Mid.y);
            var nrm = o.AlongX ? Vector3.forward : Vector3.right;
            var ra = LevelLayout.RoomAt(c + nrm * 0.6f); var rb = LevelLayout.RoomAt(c - nrm * 0.6f);
            if (ra != null && rb != null && ra != rb && idx.ContainsKey(ra) && idx.ContainsKey(rb)) { links[idx[ra]].Add(idx[rb]); links[idx[rb]].Add(idx[ra]); }
        }

        int[] Floors(LevelLayout.Room r) => r.twoStorey ? new[] { r.floor, r.floor + 1 } : new[] { r.floor };
        bool Adjacent(LevelLayout.Room a, LevelLayout.Room b)
        {
            if (LevelLayout.StageOfFloor(a.floor) != LevelLayout.StageOfFloor(b.floor)) return false;
            float ox = Mathf.Min(a.r.xMax, b.r.xMax) - Mathf.Max(a.r.xMin, b.r.xMin);
            float oz = Mathf.Min(a.r.yMax, b.r.yMax) - Mathf.Max(a.r.yMin, b.r.yMin);
            var fa = Floors(a); var fb = Floors(b);
            bool same = fa.Intersect(fb).Any();
            bool vertical = fa.Any(x => fb.Any(y => Mathf.Abs(x - y) == 1));
            if (same && ox > -0.05f && oz > -0.05f && (ox > 0.2f || oz > 0.2f)) return true;   // touching walls, or overlapping
            if (vertical && ox > 0.25f && oz > 0.25f) return true;                            // floor / ceiling between them
            return false;
        }
        var adj = Enumerable.Range(0, n).Select(_ => new HashSet<int>()).ToArray();
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (Adjacent(rooms[i], rooms[j])) { adj[i].Add(j); adj[j].Add(i); }

        // Every room gets its own layer. A lamp in room A lights A and the rooms linked to A. So a room C behind a
        // wall of A must differ from A and from every room linked to A (or A's lamp would light C through the wall).
        var conflict = Enumerable.Range(0, n).Select(_ => new HashSet<int>()).ToArray();
        void Conflict(int x, int y) { if (x != y) { conflict[x].Add(y); conflict[y].Add(x); } }
        for (int a = 0; a < n; a++)
            foreach (int c in adj[a])
            {
                if (links[a].Contains(c)) continue;          // seen through the arch: not a leak
                Conflict(a, c);
                foreach (int l in links[a]) Conflict(l, c);
            }
        var colourOf = new int[n];
        int maxColour = 0;
        foreach (int i in Enumerable.Range(0, n).OrderByDescending(i => conflict[i].Count))
        {
            var used = new HashSet<int>(conflict[i].Where(j => colourOf[j] > 0).Select(j => colourOf[j]));
            int c = 1;
            while (used.Contains(c)) c++;
            if (c > 31) { Debug.LogWarning("LIGHT LAYERS: more than 31 layers needed; wrapping"); c = 1 + (c % 31); }
            colourOf[i] = c;
            maxColour = Mathf.Max(maxColour, c);
        }
        uint Bit(LevelLayout.Room r) => r != null && idx.TryGetValue(r, out int i) ? (1u << colourOf[i]) : 0u;
        uint LampMask(LevelLayout.Room r)
        {
            if (r == null || !idx.TryGetValue(r, out int i)) return 0u;
            uint m = 1u << colourOf[i];
            foreach (int l in links[i]) m |= 1u << colourOf[l];
            return m;
        }
        int groupsCount = n;

        // Unity only keeps the rendering layers that have a name (Project Settings > Tags and Layers): unnamed bits are
        // dropped from every renderer, which once left the whole hotel unlit. Name as many as the colouring needs.
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var names = tagManager.FindProperty("m_RenderingLayers");
        while (names.arraySize <= maxColour)
        {
            names.InsertArrayElementAtIndex(names.arraySize);
            names.GetArrayElementAtIndex(names.arraySize - 1).stringValue = "Room " + (names.arraySize - 1);
        }
        tagManager.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();

        // surfaces and props: the layer of the room(s) they are in
        int renderers = 0, lights = 0;
        var offsets = new[] { Vector3.zero, new Vector3(0.3f, 0, 0), new Vector3(-0.3f, 0, 0), new Vector3(0, 0, 0.3f), new Vector3(0, 0, -0.3f) };
        foreach (var top in new[] { levelRoot, propRoot, lightRoot })
        {
            foreach (var rd in top.GetComponentsInChildren<Renderer>(true))
            {
                var b = rd.bounds;
                var c = b.center;
                if (rd.name.StartsWith("Ceiling")) c.y -= 0.5f;
                else if (rd.name.StartsWith("Floor") || rd.name.StartsWith("Landing")) c.y += 0.5f;
                else if (b.size.y > 3f) c.y = b.min.y + 0.5f;
                uint m = 0;
                foreach (var off in offsets) m |= Bit(LevelLayout.RoomAt(c + off));
                rd.renderingLayerMask = m == 0 ? 1u : m;
                renderers++;
            }
            foreach (var l in top.GetComponentsInChildren<Light>(true))
            {
                uint m = l.name == "Lightning light" ? uint.MaxValue : LampMask(LevelLayout.RoomAt(l.transform.position + Vector3.down * 1.0f));
                SetLightLayers(l, m == 0 ? uint.MaxValue : (m | 1u));
                lights++;
            }
        }
        // the camera light sees everything (it casts shadows)
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (l.name == "Camera light") SetLightLayers(l, uint.MaxValue);
        Debug.Log($"LIGHT LAYERS: {groupsCount} rooms in {maxColour} layers; {renderers} renderers, {lights} lights");
    }

    static void SetLightLayers(Light l, uint mask)
    {
        var ud = l.GetComponent<UniversalAdditionalLightData>();
        if (!ud) ud = l.gameObject.AddComponent<UniversalAdditionalLightData>();
        ud.customShadowLayers = false;
        ud.renderingLayers = mask;
        l.renderingLayerMask = unchecked((int)mask);
        EditorUtility.SetDirty(ud);
    }
}
