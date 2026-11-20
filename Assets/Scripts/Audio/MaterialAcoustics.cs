using System.Collections.Generic;
using UnityEngine;

// Acoustic properties of the surfaces in the hotel (absorption from published 500 Hz / 4 kHz coefficients), looked up from the exact triangle a ray hits.
//  absorb   : energy absorption coefficient at low/mid (~500 Hz) and high (~4 kHz) frequencies (Sabine alpha)
//  tlDb     : transmission loss through the element in dB (how much quieter a sound gets passing through it)
//  tlCutoff : what is left after transmission is mostly low frequency: low-pass corner in Hz
public struct AcousticSurface
{
    public float absorbLow, absorbHigh, tlDb, tlCutoff;
    public string name;

    public AcousticSurface(string n, float aL, float aH, float tl, float cut)
    {
        name = n; absorbLow = aL; absorbHigh = aH; tlDb = tl; tlCutoff = cut;
    }
}

public static class MaterialAcoustics
{
    public const int StructureLayer = 10, DoorLayer = 11, PropLayer = 12, CeilingLayer = 13;

    static readonly AcousticSurface Plaster = new AcousticSurface("plaster", 0.10f, 0.05f, 34f, 340f);
    static readonly AcousticSurface Masonry = new AcousticSurface("masonry", 0.03f, 0.05f, 46f, 220f);
    static readonly AcousticSurface Tile = new AcousticSurface("tile", 0.02f, 0.03f, 38f, 300f);
    static readonly AcousticSurface Carpet = new AcousticSurface("carpet", 0.30f, 0.60f, 46f, 220f);
    static readonly AcousticSurface WoodFloor = new AcousticSurface("wood", 0.10f, 0.07f, 40f, 260f);
    static readonly AcousticSurface WoodPanel = new AcousticSurface("panel", 0.17f, 0.10f, 32f, 360f);
    static readonly AcousticSurface Fabric = new AcousticSurface("fabric", 0.55f, 0.70f, 3f, 6000f);
    static readonly AcousticSurface Metal = new AcousticSurface("metal", 0.04f, 0.06f, 30f, 500f);
    static readonly AcousticSurface Grating = new AcousticSurface("grating", 0.03f, 0.04f, 1.5f, 14000f);
    static readonly AcousticSurface Glass = new AcousticSurface("glass", 0.18f, 0.04f, 12f, 3500f);
    static readonly AcousticSurface AcousticTile = new AcousticSurface("ceiling tile", 0.55f, 0.70f, 18f, 900f);
    static readonly AcousticSurface Water = new AcousticSurface("water", 0.01f, 0.02f, 46f, 220f);
    static readonly AcousticSurface Rubble = new AcousticSurface("rubble", 0.25f, 0.40f, 10f, 2500f);
    static readonly AcousticSurface Books = new AcousticSurface("books", 0.35f, 0.45f, 6f, 3000f);
    static readonly AcousticSurface Furniture = new AcousticSurface("furniture", 0.30f, 0.45f, 4f, 5000f);
    static readonly AcousticSurface WoodDoor = new AcousticSurface("wood door", 0.10f, 0.08f, 20f, 900f);
    static readonly AcousticSurface MetalDoor = new AcousticSurface("metal door", 0.04f, 0.06f, 26f, 600f);
    public static readonly AcousticSurface Open = new AcousticSurface("open", 1f, 1f, 0f, 20000f);

    static readonly Dictionary<string, AcousticSurface> byName = new Dictionary<string, AcousticSurface>();
    static readonly Dictionary<Mesh, int[]> submeshStarts = new Dictionary<Mesh, int[]>();
    static readonly Dictionary<Collider, Renderer> renderers = new Dictionary<Collider, Renderer>();
    static readonly Dictionary<Renderer,string[]> materialNames = new Dictionary<Renderer,string[]>();
    static readonly Dictionary<Collider,HingeDoor> doors = new Dictionary<Collider,HingeDoor>();
    public static void ClearGeometryCache() { submeshStarts.Clear();renderers.Clear();materialNames.Clear();doors.Clear(); }

    public static AcousticSurface FromMaterialName(string m)
    {
        if (byName.TryGetValue(m, out var s)) return s;
        if (m.StartsWith("Carpet") || m.StartsWith("Rug") || m.StartsWith("Rubber")) s = Carpet;
        else if (m.StartsWith("Fabric") || m.StartsWith("Velvet") || m.StartsWith("Leather") || m.StartsWith("Felt") || m.StartsWith("Insulation")) s = Fabric;
        else if (m.StartsWith("Wood_Panel") || m.StartsWith("Plywood") || m.StartsWith("Trim_Wood")) s = WoodPanel;
        else if (m.StartsWith("Wood")) s = WoodFloor;
        else if (m.StartsWith("Tile") || m.StartsWith("Marble") || m.StartsWith("Ceramic") || m.StartsWith("Linoleum")) s = Tile;
        else if (m.StartsWith("Brick") || m.StartsWith("Stone") || m.StartsWith("Concrete") || m.StartsWith("Ceiling_Concrete")) s = Masonry;
        else if (m == "Ceiling_Tile") s = AcousticTile;
        else if (m.StartsWith("Metal_Grating")) s = Grating;
        else if (m.StartsWith("Steel") || m.StartsWith("Iron") || m.StartsWith("Brass") || m.StartsWith("Chrome") || m.StartsWith("Gold")) s = Metal;
        else if (m.StartsWith("Glass") || m.StartsWith("Mirror") || m.StartsWith("Bottle")) s = Glass;
        else if (m.StartsWith("Water")) s = Water;
        else if (m.StartsWith("Rubble") || m.StartsWith("Soot")) s = Rubble;
        else if (m.StartsWith("Book") || m.StartsWith("Paper") || m.StartsWith("Newspaper") || m.StartsWith("Cardboard")) s = Books;
        else if (m.StartsWith("Wallpaper") || m.StartsWith("Plaster") || m.StartsWith("Paint") || m.StartsWith("Ceiling")) s = Plaster;
        else s = Furniture;
        byName[m] = s;
        return s;
    }

    public static int Submesh(Mesh m, int tri)
    {
        if (!submeshStarts.TryGetValue(m, out var starts))
        {
            starts = new int[m.subMeshCount];
            for (int i = 0; i < m.subMeshCount; i++) starts[i] = (int)m.GetSubMesh(i).indexStart / 3;
            submeshStarts[m] = starts;
        }
        for (int i = starts.Length - 1; i >= 0; i--)
            if (tri >= starts[i]) return i;
        return 0;
    }

    public static string MaterialName(RaycastHit hit)
    {
        var col = hit.collider;
        if (!col) return null;
        if (!renderers.TryGetValue(col, out var r))
        {
            r = col.GetComponent<Renderer>();
            renderers[col] = r;
        }
        if (!r) return null;
        if (!materialNames.TryGetValue(r,out var mats))
        {
            var materials=r.sharedMaterials;mats=new string[materials.Length];
            for(int i=0;i<mats.Length;i++)mats[i]=materials[i]?materials[i].name:null;
            materialNames[r]=mats;
        }
        int sub = 0;
        if (col is MeshCollider mc && mc.sharedMesh && hit.triangleIndex >= 0 && mats.Length > 1) sub = Submesh(mc.sharedMesh, hit.triangleIndex);
        return sub < mats.Length ? mats[sub] : null;
    }

    public static AcousticSurface Of(RaycastHit hit)
    {
        var col = hit.collider;
        if (!col) return Open;
        int layer = col.gameObject.layer;
        if (layer == DoorLayer)
        {
            if(!doors.TryGetValue(col,out var d)){d=col.GetComponentInParent<HingeDoor>();doors[col]=d;}
            return d && d.metal ? MetalDoor : WoodDoor;
        }
        string m = MaterialName(hit);
        if (m == null) return layer == PropLayer ? Furniture : Plaster;
        var s = FromMaterialName(m);
        // a structural face made of a "soft" material is still a wall: transmission comes from the structure behind it
        if ((layer == StructureLayer || layer == CeilingLayer) && s.tlDb < 25f && s.name != "grating" && s.name != "glass")
            s = new AcousticSurface(s.name, s.absorbLow, s.absorbHigh, Plaster.tlDb, Plaster.tlCutoff);
        return s;
    }
}
