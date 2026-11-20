using System.IO;
using UnityEditor;
using UnityEngine;

// Photo-scanned CC0 materials from ambientCG (see ASSET_CREDITS.md), in Assets/Art/Photo/<id>/<id>_1K-JPG_*.jpg.
// Each becomes a URP Lit material: colour, normal (GL), smoothness made from roughness, ambient occlusion, a tint,
// and a detail layer: large-scale grime (to break the tiling) or, on wallpaper, a printed pattern. Material NAMES
// stay the same as before because the footsteps and the acoustics read them.
public static partial class TapeBuilder
{
    const string PhotoDir = "Assets/Art/Photo";

    static Texture2D PhotoMap(string id, string map, bool normal = false, bool linear = false)
    {
        string path = $"{PhotoDir}/{id}/{id}_1K-JPG_{map}.jpg";
        if (!File.Exists(path)) return null;
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (imp == null) { AssetDatabase.ImportAsset(path); imp = (TextureImporter)AssetImporter.GetAtPath(path); }
        if (imp == null) return null;
        bool dirty = false;
        if (normal && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; dirty = true; }
        if (!normal && linear && imp.sRGBTexture) { imp.sRGBTexture = false; dirty = true; }
        if (imp.maxTextureSize != 1024) { imp.maxTextureSize = 1024; dirty = true; }
        if (imp.anisoLevel != 4) { imp.anisoLevel = 4; dirty = true; }
        if (imp.wrapMode != TextureWrapMode.Repeat) { imp.wrapMode = TextureWrapMode.Repeat; dirty = true; }
        if (dirty) imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // URP Lit wants smoothness in the alpha of the metallic map: build it once from the roughness map
    static Texture2D SmoothnessMap(string id, float metal)
    {
        string rough = $"{PhotoDir}/{id}/{id}_1K-JPG_Roughness.jpg";
        string outPath = $"{PhotoDir}/{id}/{id}_MetalSmooth.png";
        if (!File.Exists(rough)) return null;
        if (!File.Exists(outPath))
        {
            var src = new Texture2D(2, 2);
            src.LoadImage(File.ReadAllBytes(rough));
            int w = Mathf.Min(src.width, 512), h = Mathf.Min(src.height, 512);
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float r = src.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h).r;
                    px[y * w + x] = new Color(metal, 0f, 0f, 1f - r);
                }
            dst.SetPixels(px);
            dst.Apply();
            File.WriteAllBytes(outPath, dst.EncodeToPNG());
            Object.DestroyImmediate(src);
            Object.DestroyImmediate(dst);
            AssetDatabase.ImportAsset(outPath);
        }
        var imp = (TextureImporter)AssetImporter.GetAtPath(outPath);
        if (imp && imp.sRGBTexture) { imp.sRGBTexture = false; imp.wrapMode = TextureWrapMode.Repeat; imp.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }

    static Texture2D grimeDetail, damaskDetail, stripeDetail;

    static void Photo(string name, string id, float tile, Color tint, float smooth = 1f, float metal = 0f, float bump = 1f,
                      Texture2D detail = null, float detailPeriod = 7f, float detailScale = 1f, float ao = 1f)
    {
        var color = PhotoMap(id, "Color");
        if (!color) { Debug.LogWarning("Photo texture missing: " + id); return; }
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        string path = $"Assets/Art/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(lit) { name = name }; AssetDatabase.CreateAsset(m, path); }
        m.shader = lit;
        m.SetTexture("_BaseMap", color);
        m.SetTextureScale("_BaseMap", Vector2.one / tile);
        m.SetColor("_BaseColor", tint);
        var n = PhotoMap(id, "NormalGL", normal: true);
        if (n) { m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", bump); m.EnableKeyword("_NORMALMAP"); }
        else m.DisableKeyword("_NORMALMAP");
        var ms = SmoothnessMap(id, metal);
        if (ms) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", smooth); m.SetFloat("_SmoothnessTextureChannel", 0f); }
        else { m.DisableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", 0.2f * smooth); }
        m.SetFloat("_Metallic", metal);
        var occ = PhotoMap(id, "AmbientOcclusion", linear: true);
        if (occ) { m.SetTexture("_OcclusionMap", occ); m.SetFloat("_OcclusionStrength", ao); m.EnableKeyword("_OCCLUSIONMAP"); }
        else m.DisableKeyword("_OCCLUSIONMAP");
        var d = detail ? detail : grimeDetail;
        if (d)
        {
            m.SetTexture("_DetailAlbedoMap", d);
            m.SetTextureScale("_DetailAlbedoMap", Vector2.one * (tile / detailPeriod));     // detail UVs ride on the base UVs
            m.SetFloat("_DetailAlbedoMapScale", detailScale);
            m.EnableKeyword("_DETAIL_MULX2");
        }
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", Color.black);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(m);
        mats[name] = m;
    }

    static void MakePhotoMaterials()
    {
        // detail layers: large stains and darkening (neutral at mid grey), a damask print, a faded stripe
        grimeDetail = Tex("grime_detail", 512, (x, y) =>
        {
            float big = T(x, y, 3f), mid = T(x, y, 9f), fine = T(x, y, 31f);
            float stain = Mathf.SmoothStep(0.55f, 0.75f, big * 0.7f + mid * 0.3f);
            float v = 0.5f - stain * 0.16f - (mid - 0.5f) * 0.08f + (fine - 0.5f) * 0.05f;
            return new Color(v * 1.02f, v, v * 0.95f);
        });
        damaskDetail = Tex("damask_detail", 256, (x, y) =>
        {
            float u = Mathf.Repeat(x * 2f, 1f) - 0.5f, w = Mathf.Repeat(y * 2f + (Mathf.Floor(x * 2f) % 2) * 0.5f, 1f) - 0.5f;
            float r = Mathf.Sqrt(u * u * 2.2f + w * w);
            float petal = Mathf.Abs(Mathf.Sin(Mathf.Atan2(w, u) * 4f)) * 0.18f;
            float motif = Mathf.SmoothStep(0.34f + petal, 0.30f + petal, r) - Mathf.SmoothStep(0.16f, 0.12f, r) * 0.6f;
            float fade = 0.75f + 0.25f * T(x, y, 4f);
            float v = 0.5f + motif * 0.13f * fade - (T(x, y, 6f) - 0.5f) * 0.07f;
            return new Color(v, v, v);
        });
        stripeDetail = Tex("stripe_detail", 256, (x, y) =>
        {
            float s = Mathf.Repeat(x * 6f, 1f) < 0.5f ? 0.06f : -0.03f;
            float v = 0.5f + s * (0.7f + 0.3f * T(x, y, 4f)) - (T(x, y, 7f) - 0.5f) * 0.06f;
            return new Color(v, v, v);
        });

        Photo("Marble_Lobby", "Tiles074", 2.4f, new Color(0.85f, 0.83f, 0.8f), 0.9f, 0f, 1f, null, 6f, 1.15f);
        // part three has its own materials: the owner's penthouse is dark walnut, black marble and parquet;
        // the roof huts are painted sheet metal
        Photo("Wood_Panel", "Wood051", 1.1f, new Color(0.44f, 0.32f, 0.24f), 0.85f, 0f, 1f, null, 3f, 1.1f);
        Photo("Wood_Parquet", "WoodFloor043", 1.5f, new Color(0.5f, 0.37f, 0.28f), 0.95f, 0f, 1f, null, 5f, 1.15f);
        Photo("Marble_Dark", "Tiles074", 2.0f, new Color(0.3f, 0.29f, 0.28f), 1.05f, 0f, 1f, null, 6f, 1.1f);
        Photo("Fabric_Wall", "Fabric061", 1.2f, new Color(0.56f, 0.52f, 0.47f), 0.45f, 0f, 1f, null, 4f, 1.1f);
        Photo("Carpet_Deco", "Carpet004", 1.3f, new Color(0.34f, 0.29f, 0.27f), 0.6f, 0f, 1f, null, 5f, 1.2f);
        Photo("Velvet_Dark", "Fabric061", 0.8f, new Color(0.3f, 0.23f, 0.21f), 0.4f, 0f, 1f, null, 2f, 1.1f);
        Photo("Metal_Corrugated", "PaintedMetal016", 0.9f, new Color(0.5f, 0.52f, 0.5f), 0.75f, 0.45f, 1.4f, null, 3f, 1f);
        Photo("Wallpaper_Lobby", "PaintedPlaster018", 2.6f, new Color(0.84f, 0.8f, 0.7f), 0.6f, 0f, 1f, damaskDetail, 0.9f, 1f);
        Photo("Wallpaper_Hall", "Wallpaper001C", 2.2f, new Color(0.8f, 0.68f, 0.6f), 0.5f, 0f, 1f, damaskDetail, 0.7f, 1f);
        Photo("Wallpaper_Room", "Wallpaper002C", 2.2f, new Color(0.95f, 0.88f, 0.76f), 0.5f, 0f, 1f, stripeDetail, 1.0f, 1f);
        Photo("Carpet_Gallery", "Carpet015", 1.6f, new Color(0.56f, 0.42f, 0.37f), 0.6f, 0f, 1f, null, 6f, 1.2f);
        Photo("Carpet_Hall", "Carpet015", 1.6f, new Color(0.48f, 0.37f, 0.33f), 0.6f, 0f, 1f, null, 5f, 1.25f);
        Photo("Carpet_Stair", "Carpet015", 1.6f, new Color(0.52f, 0.4f, 0.35f), 0.6f, 0f, 1f, null, 5f, 1.2f);
        Photo("Carpet_Room", "Carpet004", 1.6f, new Color(0.75f, 0.7f, 0.78f), 0.6f, 0f, 1f, null, 5f, 1.2f);
        Photo("Linoleum_Staff", "Tiles144", 2.2f, new Color(0.8f, 0.8f, 0.75f), 1f, 0f, 1f, null, 6f, 1.2f);
        Photo("Linoleum_Service", "Tiles144", 2.2f, new Color(0.85f, 0.85f, 0.82f), 1f, 0f, 1f, null, 6f, 1.2f);
        Photo("Paint_Staff", "Plaster007", 2.6f, new Color(0.86f, 0.88f, 0.8f), 0.7f, 0f, 1f, null, 7f, 1.1f);
        Photo("Paint_Office", "PaintedPlaster018", 2.6f, new Color(0.82f, 0.81f, 0.76f), 0.6f, 0f, 1f, null, 7f, 1.1f);
        Photo("Paint_Bedroom", "Plaster007", 2.6f, new Color(0.7f, 0.76f, 0.86f), 0.6f, 0f, 1f, null, 7f, 0.9f);
        Photo("Concrete_Stair", "Concrete040", 2.6f, new Color(0.85f, 0.85f, 0.83f), 0.8f, 0f, 1f, null, 7f, 1.1f);
        Photo("Concrete_Wall", "Concrete044D", 3.0f, new Color(1f, 1f, 0.98f), 0.8f, 0f, 1.2f, null, 8f, 1.15f);
        Photo("Brick_Wall", "Bricks037", 2.2f, new Color(0.85f, 0.8f, 0.78f), 0.8f, 0f, 1.2f, null, 7f, 1.1f);
        Photo("Wood_Floor", "WoodFloor043", 2.2f, new Color(0.8f, 0.72f, 0.65f), 0.7f, 0f, 1f, null, 6f, 1.2f);
        Photo("Wood_Door", "Wood051", 1.6f, new Color(0.8f, 0.72f, 0.66f), 0.7f, 0f, 1f, null, 3f, 1.1f);
        Photo("Wood_Furniture", "Wood049", 1.3f, new Color(0.78f, 0.7f, 0.64f), 0.8f, 0f, 1f, null, 3f, 1.1f);
        Photo("Plywood_Board", "Wood089", 1.4f, new Color(0.9f, 0.85f, 0.8f), 0.6f, 0f, 1f, null, 3f, 1.1f);
        Photo("Ceiling_Plaster", "Plaster003", 2.6f, new Color(0.8f, 0.78f, 0.72f), 0.5f, 0f, 1f, null, 6f, 1.2f);
        Photo("Ceiling_Tiles", "OfficeCeiling005", 2.4f, new Color(0.72f, 0.7f, 0.64f), 0.5f, 0f, 1f, null, 5f, 1.3f);
        Photo("Tile_Kitchen", "Tiles133D", 2.0f, new Color(0.9f, 0.9f, 0.86f), 1f, 0f, 1f, null, 6f, 1.15f);
        Photo("Rubble_Floor", "Ground107", 1.8f, new Color(0.9f, 0.88f, 0.85f), 0.7f, 0f, 1.2f, null, 5f, 1f);
        Photo("Soot_Floor", "Ground107", 1.8f, new Color(0.16f, 0.14f, 0.13f), 0.5f, 0f, 1.2f, null, 5f, 1f);
        Photo("Plaster_Burnt", "Concrete044D", 2.5f, new Color(0.22f, 0.18f, 0.15f), 0.4f, 0f, 1.2f, null, 5f, 1.2f);
        Photo("Metal_Device", "PaintedMetal016", 1.2f, new Color(0.55f, 0.56f, 0.55f), 0.8f, 0.3f, 1f, null, 3f, 1f);
        Photo("Metal_Lift", "PaintedMetal016", 1.6f, new Color(0.72f, 0.7f, 0.62f), 0.8f, 0.4f, 1f, null, 3f, 1f);
        Photo("Metal_Floor", "Rust009", 1.6f, new Color(0.8f, 0.78f, 0.76f), 0.8f, 0.5f, 1f, null, 4f, 1f);
        Photo("Fabric_Bed", "Fabric061", 0.9f, new Color(0.8f, 0.77f, 0.7f), 0.5f, 0f, 1f, null, 2f, 1.15f);
        Photo("Fabric_Cloth", "Fabric061", 0.9f, new Color(0.85f, 0.84f, 0.8f), 0.5f, 0f, 1f, null, 2f, 1.15f);
        Photo("Fabric_Curtain", "Fabric061", 0.9f, new Color(0.44f, 0.33f, 0.28f), 0.5f, 0f, 1f, null, 2f, 1.1f);
        Photo("Fabric_Cover", "Leather014", 0.9f, new Color(0.62f, 0.38f, 0.32f), 0.7f, 0f, 1f, null, 2f, 1.1f);
        // your house has surfaces of its own (Poly Haven textures), nothing shared with the hotel
        HomeMaterials();
        AssetDatabase.SaveAssets();
    }
}
