using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Procedural textures and materials for every material name used by the Blender map.
// UVs are world metres, so each material sets its texture size in metres; a large-scale grime detail map
// (7 m tiles) is laid over walls and floors so repetition never shows.
public static partial class HotelBuilder
{
    const string TexDir = "Assets/Generated/Textures";
    const string MatDir = "Assets/Generated/Materials";

    enum Surf { Opaque, Cutout, Transparent }

    class Spec
    {
        public string pattern = "noise";
        public Color a = Color.gray, b = Color.gray, c = Color.black;
        public float tile = 1f, smooth = 0.25f, metal = 0f, bump = 0.6f, grime = 0.5f;
        public Color emission = Color.black;
        public Surf surf = Surf.Opaque;
        public float alpha = 1f;
        public int size = 512;
    }

    static Color C(float r, float g, float b) => new Color(r, g, b);

    static Spec Describe(string n)
    {
        var s = new Spec();
        Color hex(string h) { ColorUtility.TryParseHtmlString(h, out var col); return col; }
        void set(string pat, Color a, Color b, float tile, float smooth = 0.2f, float bump = 0.6f, float grime = 0.5f)
        {
            s.pattern = pat; s.a = a; s.b = b; s.tile = tile; s.smooth = smooth; s.bump = bump; s.grime = grime;
        }
        switch (n)
        {
            case "Wallpaper_Damask_Red": set("damask", hex("#5a1414"), hex("#7a2a22"), 0.6f, 0.15f, 0.2f, 0.8f); break;
            case "Wallpaper_Damask_Blue": set("damask", hex("#1d2a44"), hex("#34466a"), 0.6f, 0.15f, 0.2f, 0.8f); break;
            case "Wallpaper_Stripe_Green": set("stripe", hex("#2f4330"), hex("#4a6243"), 0.5f, 0.12f, 0.15f, 0.9f); break;
            case "Wallpaper_Floral_Cream": set("floral", hex("#b5a47f"), hex("#8b5a4a"), 0.45f, 0.12f, 0.15f, 1f); s.c = hex("#5d6b45"); break;
            case "Wallpaper_Geo_Teal": set("geo", hex("#21494a"), hex("#b59a5a"), 0.5f, 0.15f, 0.2f, 0.8f); break;
            case "Wallpaper_Plain_Mauve": set("plain", hex("#6d5260"), hex("#5c4250"), 1f, 0.15f, 0.2f, 0.9f); break;
            case "Wallpaper_Nursery": set("nursery", hex("#9aa6b8"), hex("#d7c58a"), 0.5f, 0.15f, 0.2f, 0.8f); s.c = hex("#c88a8a"); break;
            case "Wallpaper_Gold": set("geo", hex("#7a6233"), hex("#c9a95c"), 0.45f, 0.3f, 0.3f, 0.6f); break;
            case "Wood_Panel_Dark": set("panel", hex("#3a2010"), hex("#24130a"), 1.2f, 0.35f, 0.8f, 0.5f); break;
            case "Wood_Panel_Light": set("panel", hex("#7a5532"), hex("#5a3c22"), 1.2f, 0.3f, 0.8f, 0.5f); break;
            case "Plaster_Stained": set("plaster", hex("#9a9384"), hex("#6d6656"), 2f, 0.1f, 0.5f, 1.2f); break;
            case "Plaster_Yellowed": set("plaster", hex("#a69560"), hex("#7a6a40"), 2f, 0.1f, 0.5f, 1.2f); break;
            case "Plaster_Burnt": set("burnt", hex("#1a1612"), hex("#3a3229"), 2f, 0.05f, 1f, 0.6f); break;
            case "Paint_Peeling_Blue": set("peel", hex("#56707e"), hex("#8f8a80"), 1.5f, 0.2f, 0.8f, 1f); break;
            case "Plywood": set("plywood", hex("#9c7a4e"), hex("#7a5a36"), 1.2f, 0.15f, 0.4f, 0.5f); break;
            case "Tile_White": set("tile", hex("#d4d2c8"), hex("#8a877c"), 0.6f, 0.7f, 0.9f, 0.9f); s.c = C(0.15f, 0, 0); break;
            case "Tile_Green_Old": set("tile", hex("#5c7a62"), hex("#6b6a5c"), 0.45f, 0.65f, 0.9f, 1f); s.c = C(0.15f, 0, 0); break;
            case "Tile_Kitchen": set("tile", hex("#cfcabb"), hex("#555248"), 0.3f, 0.6f, 0.8f, 1f); s.c = C(0.15f, 0, 0); break;
            case "Brick_Red": set("brick", hex("#6b3322"), hex("#8a7d6a"), 1f, 0.12f, 1.2f, 0.8f); break;
            case "Brick_Dark": set("brick", hex("#3a2a22"), hex("#504a40"), 1f, 0.12f, 1.2f, 0.9f); break;
            case "Concrete_Wall": set("concrete", hex("#6b6a64"), hex("#4a4944"), 2f, 0.15f, 0.6f, 1f); break;
            case "Marble_Wall": set("marble", hex("#cfc9bc"), hex("#7d776a"), 2f, 0.75f, 0.2f, 0.6f); break;
            case "Stone_Block": set("stone", hex("#5e5a52"), hex("#3e3b35"), 2f, 0.2f, 1.2f, 0.9f); break;
            case "Carpet_Red": set("carpet", hex("#5a1212"), hex("#b58a3a"), 1.5f, 0.05f, 0.4f, 0.9f); s.c = hex("#2a0808"); break;
            case "Carpet_Blue": set("carpet", hex("#1a2442"), hex("#9a8a5a"), 1.5f, 0.05f, 0.4f, 0.9f); s.c = hex("#0d1224"); break;
            case "Carpet_Green": set("carpet", hex("#1e3a24"), hex("#a08848"), 1.5f, 0.05f, 0.4f, 1f); s.c = hex("#0e1d12"); break;
            case "Carpet_Gold": set("carpet", hex("#7a5a22"), hex("#3a1a12"), 1.5f, 0.05f, 0.4f, 0.9f); s.c = hex("#4a3010"); break;
            case "Carpet_Brown": set("carpet", hex("#3c2a18"), hex("#6a5236"), 1.2f, 0.05f, 0.4f, 1.1f); s.c = hex("#241a10"); break;
            case "Carpet_Purple": set("carpet", hex("#3a1a44"), hex("#a08a5a"), 1.5f, 0.05f, 0.4f, 0.9f); s.c = hex("#1d0d22"); break;
            case "Carpet_Pink": set("carpet", hex("#8a5664"), hex("#d0b08a"), 1.5f, 0.05f, 0.4f, 0.9f); s.c = hex("#5a3440"); break;
            case "Wood_Parquet": set("parquet", hex("#6a4220"), hex("#4a2c14"), 1.2f, 0.45f, 0.6f, 0.7f); break;
            case "Wood_Plank": set("plank", hex("#5a3a1e"), hex("#3e2812"), 2f, 0.3f, 0.7f, 0.8f); break;
            case "Marble_Checker": set("checker", hex("#d8d2c4"), hex("#2a2826"), 1f, 0.8f, 0.3f, 0.6f); break;
            case "Marble_Floor": set("marble", hex("#c8c0b0"), hex("#8a8274"), 2f, 0.8f, 0.2f, 0.6f); break;
            case "Tile_Hex": set("hex", hex("#d8d8d2"), hex("#5a5852"), 0.4f, 0.55f, 0.8f, 1f); break;
            case "Linoleum": set("lino", hex("#7a7660"), hex("#5a5646"), 0.6f, 0.4f, 0.3f, 1f); break;
            case "Concrete_Floor": set("concrete", hex("#4a4842"), hex("#33322e"), 2f, 0.2f, 0.7f, 1.1f); break;
            case "Metal_Grating": set("grating", hex("#4a4a48"), hex("#2a2a28"), 0.5f, 0.45f, 0.8f, 0.6f); s.metal = 0.6f; s.surf = Surf.Cutout; break;
            case "Water_Shallow": set("water", hex("#0a1110"), hex("#121c1a"), 2f, 0.95f, 0.3f, 0f); break;
            case "Stone_Floor": set("stone", hex("#4a463e"), hex("#2e2b26"), 1.5f, 0.55f, 1f, 1f); break;
            case "Rubber_Mat": set("noise", hex("#1a1a1a"), hex("#262626"), 1f, 0.1f, 0.5f, 0.5f); break;
            case "Ceiling_Tile": set("ceiltile", hex("#b0ab9c"), hex("#7a7466"), 1.2f, 0.05f, 0.6f, 1.2f); break;
            case "Ceiling_Plaster": set("plaster", hex("#b8b2a2"), hex("#8a8270"), 2.5f, 0.08f, 0.3f, 1.2f); break;
            case "Ceiling_Ornate": set("coffer", hex("#b0a280"), hex("#7a6234"), 2f, 0.25f, 0.8f, 0.8f); break;
            case "Ceiling_Concrete": set("concrete", hex("#55544e"), hex("#3a3934"), 2f, 0.1f, 0.6f, 1f); break;
            case "Ceiling_Burnt": set("burnt", hex("#121010"), hex("#2a2420"), 2f, 0.05f, 0.8f, 0.6f); break;
            case "Skylight": set("skylight", hex("#0c1018"), hex("#202838"), 2f, 0.9f, 0.2f, 0f); s.emission = C(0.03f, 0.04f, 0.07f); break;
            case "Trim_Wood": set("grain", hex("#2c160a"), hex("#1a0c05"), 0.8f, 0.45f, 0.3f, 0.3f); break;
            case "Trim_White": set("noise", hex("#bab4a6"), hex("#9e9888"), 1f, 0.4f, 0.2f, 0.5f); break;
            case "Brass": set("metal", hex("#a67d3a"), hex("#5a4420"), 0.5f, 0.6f, 0.2f, 0.3f); s.metal = 0.9f; break;
            case "Gold": set("metal", hex("#c49a42"), hex("#7a5a24"), 0.5f, 0.6f, 0.2f, 0.3f); s.metal = 0.9f; break;
            case "Iron": set("metal", hex("#2a2a2a"), hex("#1a1a1a"), 0.5f, 0.35f, 0.3f, 0.5f); s.metal = 0.8f; break;
            case "Iron_Rust": set("rust", hex("#5a2a12"), hex("#2a1a12"), 0.8f, 0.2f, 0.9f, 0.4f); s.metal = 0.4f; break;
            case "Steel": set("brushed", hex("#8a8c8e"), hex("#5a5c5e"), 0.6f, 0.55f, 0.2f, 0.5f); s.metal = 0.85f; break;
            case "Chrome": set("brushed", hex("#cfd0d2"), hex("#9a9a9c"), 0.6f, 0.85f, 0.1f, 0.3f); s.metal = 1f; break;
            case "Glass": set("glass", hex("#7f9494"), hex("#5d6e6c"), 1.5f, 0.93f, 0.15f, 0f); s.surf = Surf.Transparent; s.alpha = 0.28f; break;
            case "Bottle_Glass": set("plain", hex("#1e4a24"), hex("#1e4a24"), 1f, 0.95f, 0f, 0f); s.surf = Surf.Transparent; s.alpha = 0.6f; break;
            case "Mirror": set("plain", hex("#d8dcdc"), hex("#c8cccc"), 1f, 0.97f, 0f, 0f); s.metal = 1f; break;
            case "Plastic_Sheet": set("plain", hex("#c8ccc8"), hex("#b0b4b0"), 1f, 0.6f, 0.1f, 0.3f); s.surf = Surf.Transparent; s.alpha = 0.45f; break;
            case "Ivy": set("leaf", hex("#1e3a10"), hex("#3a5a1a"), 0.3f, 0.3f, 0.4f, 0f); s.surf = Surf.Cutout; break;
            case "Water_Puddle": set("water", hex("#0a1110"), hex("#121c1a"), 2f, 0.95f, 0.3f, 0f); break;
            default:
                if (n.StartsWith("Decal_"))
                {
                    s.surf = Surf.Transparent;
                    s.grime = 0f;
                    s.bump = 0.2f;
                    s.smooth = 0.1f;
                    s.tile = 1f;
                    s.pattern = "decal";
                    switch (n)
                    {
                        case "Decal_WaterStain": s.a = hex("#4a3a20"); s.b = hex("#2a1e10"); s.alpha = 0.55f; break;
                        case "Decal_Mold": s.a = hex("#1c2414"); s.b = hex("#0c1008"); s.alpha = 0.8f; s.c = C(1, 0, 0); break;
                        case "Decal_Rust": s.a = hex("#5a2a10"); s.b = hex("#2a1206"); s.alpha = 0.6f; s.c = C(0, 1, 0); break;
                        case "Decal_Grime": s.a = hex("#2a2620"); s.b = hex("#14120e"); s.alpha = 0.45f; s.c = C(0, 1, 0); break;
                        case "Decal_Soot": s.a = hex("#0a0808"); s.b = hex("#000000"); s.alpha = 0.85f; break;
                        case "Decal_Oil": s.a = hex("#0c0a08"); s.b = hex("#050403"); s.alpha = 0.7f; s.smooth = 0.8f; break;
                        case "Decal_Dirt": s.a = hex("#2a2218"); s.b = hex("#15110b"); s.alpha = 0.5f; break;
                        case "Decal_WetPatch": s.a = hex("#0a0c0c"); s.b = hex("#050606"); s.alpha = 0.55f; s.smooth = 0.9f; break;
                        case "Decal_Crack": s.a = hex("#0c0b0a"); s.b = hex("#000000"); s.alpha = 0.9f; s.c = C(0, 0, 1); break;
                        case "Decal_Torn": s.a = hex("#8a8270"); s.b = hex("#5e584a"); s.alpha = 0.95f; s.smooth = 0.05f; s.bump = 0.6f; break;
                        case "Decal_Void": s.a = hex("#020202"); s.b = hex("#000000"); s.alpha = 0.97f; break;
                    }
                }
                else if (n.StartsWith("Fabric_")) set("fabric", Palette(n), Palette(n) * 0.75f, 0.3f, 0.08f, 0.4f, 0.6f);
                else if (n.StartsWith("Velvet")) set("velvet", Palette(n), Palette(n) * 0.6f, 0.3f, 0.15f, 0.4f, 0.6f);
                else if (n.StartsWith("Leather")) set("leather", Palette(n), Palette(n) * 0.7f, 0.4f, 0.35f, 0.5f, 0.5f);
                else if (n.StartsWith("Wood")) set("grain", Palette(n), Palette(n) * 0.7f, 1f, 0.35f, 0.4f, 0.5f);
                else if (n.StartsWith("Emit_"))
                {
                    set("plain", Palette(n), Palette(n), 1f, 0.5f, 0f, 0f);
                    float k = n == "Emit_Night" ? 0.12f : n == "Emit_Screen" ? 0.8f : n == "Emit_Red" ? 1.2f : n == "Emit_Vending" ? 0.9f : 1.1f;
                    s.emission = Palette(n) * k;
                }
                else set("noise", Palette(n), Palette(n) * 0.8f, 0.5f, 0.25f, 0.3f, 0.5f);
                if (n.StartsWith("Plastic") || n.StartsWith("Ceramic") || n.StartsWith("Lacquer")) s.smooth = 0.75f;
                if (n.StartsWith("Paint_")) s.smooth = 0.3f;
                break;
        }
        return s;
    }

    // colours of the Blender preview palette (same names)
    static readonly Dictionary<string, Color> palette = new Dictionary<string, Color>
    {
        { "Fabric_White", C(0.82f, 0.8f, 0.74f) }, { "Fabric_Red", C(0.5f, 0.08f, 0.08f) }, { "Fabric_Blue", C(0.12f, 0.18f, 0.4f) },
        { "Fabric_Green", C(0.15f, 0.3f, 0.16f) }, { "Fabric_Gold", C(0.6f, 0.46f, 0.16f) }, { "Fabric_Grey", C(0.4f, 0.4f, 0.4f) },
        { "Fabric_Pink", C(0.7f, 0.42f, 0.5f) }, { "Fabric_Brown", C(0.3f, 0.2f, 0.12f) }, { "Velvet_Red", C(0.35f, 0.03f, 0.05f) },
        { "Leather_Brown", C(0.25f, 0.13f, 0.07f) }, { "Leather_Black", C(0.05f, 0.05f, 0.05f) }, { "Felt_Green", C(0.05f, 0.3f, 0.12f) },
        { "Wood_Dark", C(0.18f, 0.1f, 0.05f) }, { "Wood_Light", C(0.5f, 0.36f, 0.2f) }, { "Wood_Painted_White", C(0.78f, 0.76f, 0.7f) },
        { "Wood_Charred", C(0.05f, 0.04f, 0.03f) }, { "Lacquer_Black", C(0.03f, 0.03f, 0.03f) }, { "Ceramic_White", C(0.88f, 0.88f, 0.85f) },
        { "Plastic_White", C(0.8f, 0.8f, 0.78f) }, { "Plastic_Red", C(0.6f, 0.08f, 0.06f) }, { "Plastic_Yellow", C(0.8f, 0.65f, 0.1f) },
        { "Paper", C(0.82f, 0.8f, 0.72f) }, { "Newspaper", C(0.62f, 0.6f, 0.52f) }, { "Cardboard", C(0.5f, 0.36f, 0.2f) },
        { "Tape_Yellow", C(0.9f, 0.75f, 0.05f) }, { "Chalk", C(0.9f, 0.9f, 0.9f) }, { "Plant_Dead", C(0.25f, 0.2f, 0.1f) },
        { "Book_Mix", C(0.4f, 0.2f, 0.15f) }, { "Rubber_Black", C(0.04f, 0.04f, 0.04f) }, { "Paint_Red", C(0.6f, 0.05f, 0.04f) },
        { "Paint_Green", C(0.15f, 0.35f, 0.2f) }, { "Wax", C(0.85f, 0.8f, 0.65f) }, { "Food_Rot", C(0.3f, 0.28f, 0.12f) },
        { "Emit_Warm", C(1f, 0.75f, 0.45f) }, { "Emit_Cold", C(0.85f, 1f, 0.88f) }, { "Emit_Red", C(1f, 0.1f, 0.05f) },
        { "Emit_Screen", C(0.5f, 0.6f, 0.9f) }, { "Emit_Vending", C(0.8f, 0.9f, 1f) }, { "Emit_Night", C(0.1f, 0.13f, 0.22f) },
        { "Rubble", C(0.35f, 0.33f, 0.3f) }, { "Insulation", C(0.75f, 0.6f, 0.35f) }, { "Soot", C(0.04f, 0.04f, 0.04f) },
        { "Exterior", C(0.3f, 0.3f, 0.3f) },
    };

    static Color Palette(string n) => palette.TryGetValue(n, out var c) ? c : new Color(0.5f, 0.5f, 0.5f);

    // ------------------------------------------------------------------ noise
    static float Tileable(float u, float v, float freq, float seed)
    {
        // seamless value noise: blend four offset samples of Unity's Perlin noise
        float x = u * freq, y = v * freq;
        float a = Mathf.PerlinNoise(x + seed, y + seed);
        float b = Mathf.PerlinNoise(x - freq + seed, y + seed);
        float c = Mathf.PerlinNoise(x + seed, y - freq + seed);
        float d = Mathf.PerlinNoise(x - freq + seed, y - freq + seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    static float Fbm(float u, float v, float freq, float seed, int oct = 4)
    {
        float s = 0, amp = 0.5f, f = freq;
        for (int i = 0; i < oct; i++)
        {
            s += amp * Tileable(u, v, f, seed + i * 17.3f);
            amp *= 0.5f;
            f *= 2f;
        }
        return s / (1 - Mathf.Pow(0.5f, oct));
    }

    static float Hash(int x, int y, int s = 0)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + s * 2147483647);
        h = (h ^ (h >> 13)) * 1274126177;
        return (h ^ (h >> 16)) / 4294967295f;
    }

    static float Fract(float x) => x - Mathf.Floor(x);
    static float SStep(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

    // ------------------------------------------------------------------ patterns: returns albedo + height
    static void Pattern(Spec s, string name, float u, float v, out Color col, out float h, out float alphaOut)
    {
        float seed = (name.GetHashCode() & 0xFFFF) * 0.01f;
        float n1 = Fbm(u, v, 8, seed), n2 = Fbm(u, v, 32, seed + 3, 3);
        alphaOut = 1f;
        h = 0.5f;
        col = Color.Lerp(s.a, s.b, n1 * 0.5f);
        switch (s.pattern)
        {
            case "noise":
                col = Color.Lerp(s.a, s.b, n1);
                h = n2;
                break;
            case "plain":
                col = Color.Lerp(s.a, s.b, n1 * 0.3f);
                h = 0.5f + (n2 - 0.5f) * 0.2f;
                break;
            case "damask":
            {
                // mirrored ornamental motif on an offset lattice
                float gu = Fract(u * 2f), gv = Fract(v * 2f);
                int row = Mathf.FloorToInt(v * 2f);
                if ((row & 1) == 1) gu = Fract(gu + 0.5f);
                float x = Mathf.Abs(gu - 0.5f) * 2f, y = gv * 2f - 1f;
                float r = Mathf.Sqrt(x * x + y * y), th = Mathf.Atan2(y, x);
                float petal = 0.55f + 0.3f * Mathf.Cos(th * 4f) + 0.12f * Mathf.Cos(th * 9f);
                float m = SStep(0.03f, 0.0f, r - petal * 0.75f) * SStep(0.0f, 0.06f, r - 0.12f);
                float scroll = SStep(0.05f, 0f, Mathf.Abs(Mathf.Sin(th * 3f + r * 9f)) * r - 0.04f) * SStep(0.95f, 0.7f, r);
                float mm = Mathf.Max(m, scroll * 0.7f);
                col = Color.Lerp(s.a, s.b, mm);
                col *= 0.92f + 0.08f * n2;
                h = 0.5f + mm * 0.15f;
                break;
            }
            case "stripe":
            {
                float x = Fract(u * 4f);
                float band = x < 0.5f ? 0f : 1f;
                float pin = SStep(0.02f, 0f, Mathf.Abs(x - 0.25f)) + SStep(0.015f, 0f, Mathf.Abs(x - 0.75f));
                col = Color.Lerp(s.a, s.b, band * 0.55f);
                col = Color.Lerp(col, new Color(0.62f, 0.55f, 0.38f), pin * 0.6f);
                col *= 0.93f + 0.07f * n2;
                h = 0.5f + pin * 0.1f;
                break;
            }
            case "floral":
            {
                float best = 0;
                for (int k = 0; k < 6; k++)
                {
                    float cx = Hash(k, 1, 7), cy = Hash(k, 2, 7);
                    float dx = Mathf.Abs(u - cx), dy = Mathf.Abs(v - cy);
                    dx = Mathf.Min(dx, 1 - dx);
                    dy = Mathf.Min(dy, 1 - dy);
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 9f, th = Mathf.Atan2(dy, dx) * 5f;
                    float f = SStep(0.12f, 0f, r - (0.6f + 0.35f * Mathf.Cos(th)));
                    best = Mathf.Max(best, f);
                }
                float leaf = SStep(0.62f, 0.7f, Fbm(u, v, 6, seed + 9));
                col = Color.Lerp(s.a, s.c, leaf * 0.5f);
                col = Color.Lerp(col, s.b, best * 0.8f);
                col *= 0.94f + 0.06f * n2;
                break;
            }
            case "geo":
            {
                float x = Fract(u * 3f) - 0.5f, y = Fract(v * 3f) - 0.5f;
                float dia = Mathf.Abs(x) + Mathf.Abs(y);
                float line = SStep(0.025f, 0f, Mathf.Abs(dia - 0.42f)) + SStep(0.02f, 0f, Mathf.Abs(dia - 0.3f));
                float fan = SStep(0.02f, 0f, Mathf.Abs(Fract(Mathf.Atan2(y, x) * 2.5f) - 0.5f) * (0.42f - dia)) * (dia < 0.28f ? 1 : 0);
                float m = Mathf.Clamp01(line + fan * 0.6f);
                col = Color.Lerp(s.a, s.b, m);
                col *= 0.92f + 0.08f * n2;
                h = 0.5f + m * 0.1f;
                break;
            }
            case "nursery":
            {
                float x = Fract(u * 4f), y = Fract(v * 4f);
                int cx = Mathf.FloorToInt(u * 4f), cy = Mathf.FloorToInt(v * 4f);
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f));
                bool star = ((cx + cy) & 1) == 0;
                float shape = star ? SStep(0.02f, 0f, d - (0.17f + 0.08f * Mathf.Cos(Mathf.Atan2(y - 0.5f, x - 0.5f) * 5f)))
                                   : SStep(0.02f, 0f, d - 0.12f);
                col = Color.Lerp(s.a, star ? s.b : s.c, shape);
                col *= 0.93f + 0.07f * n2;
                break;
            }
            case "panel":
            {
                // raised wooden panels with stiles and rails
                float px = Fract(u * 2f), py = v;
                float frame = Mathf.Min(Mathf.Min(px, 1 - px), Mathf.Min(Fract(py * 1.25f), 1 - Fract(py * 1.25f)));
                float raised = SStep(0.06f, 0.12f, frame);
                float grain = Mathf.Sin((u * 40f + Fbm(u, v, 4, seed) * 6f) * Mathf.PI) * 0.5f + 0.5f;
                col = Color.Lerp(s.a, s.b, grain * 0.35f + (1 - raised) * 0.25f);
                h = 0.35f + raised * 0.4f + grain * 0.05f;
                break;
            }
            case "grain":
            {
                float g = Mathf.Sin((v * 30f + Fbm(u, v, 3, seed) * 8f) * Mathf.PI) * 0.5f + 0.5f;
                col = Color.Lerp(s.a, s.b, g * 0.5f + n2 * 0.2f);
                h = 0.5f + g * 0.1f;
                break;
            }
            case "plank":
            {
                float row = Mathf.Floor(v * 10f);
                float off = Hash((int)row, 3) * 3f;
                float pu = Fract(u * 1.5f + off);
                float seam = SStep(0.0f, 0.01f, Fract(v * 10f)) * SStep(1f, 0.99f, Fract(v * 10f)) * SStep(0f, 0.006f, pu) * SStep(1f, 0.994f, pu);
                float g = Mathf.Sin((v * 10f * 6f + Fbm(u, v, 3, seed + row) * 10f) * Mathf.PI) * 0.5f + 0.5f;
                float tint = Hash((int)row, Mathf.FloorToInt(u * 1.5f + off), 5);
                col = Color.Lerp(s.a, s.b, g * 0.4f + tint * 0.4f);
                col *= Mathf.Lerp(0.55f, 1f, seam);
                h = 0.3f + seam * 0.5f + g * 0.05f;
                break;
            }
            case "parquet":
            {
                // herringbone
                float x = u * 8f, y = v * 8f;
                int bx = Mathf.FloorToInt((x + y) / 2f), by = Mathf.FloorToInt((x - y) / 2f);
                bool dir = ((bx + by) & 1) == 0;
                float lx = Fract(dir ? x : y), ly = Fract((dir ? y : x) * 0.5f);
                float edge = Mathf.Min(Mathf.Min(lx, 1 - lx), Mathf.Min(ly, 1 - ly) * 2f);
                float seam = SStep(0f, 0.05f, edge);
                float tint = Hash(bx, by, 11);
                float g = Mathf.Sin(((dir ? y : x) * 20f + n1 * 4f) * Mathf.PI) * 0.5f + 0.5f;
                col = Color.Lerp(s.a, s.b, tint * 0.6f + g * 0.25f) * Mathf.Lerp(0.6f, 1f, seam);
                h = 0.3f + seam * 0.5f;
                break;
            }
            case "plaster":
            case "peel":
            {
                float stain = SStep(0.55f, 0.75f, Fbm(u, v, 3, seed + 5));
                float ring = SStep(0.03f, 0f, Mathf.Abs(Fbm(u, v, 3, seed + 5) - 0.55f));
                col = Color.Lerp(s.a, s.b, n1 * 0.4f + stain * 0.4f);
                col = Color.Lerp(col, s.b * 0.7f, ring * 0.5f);
                h = 0.5f + (n2 - 0.5f) * 0.3f;
                if (s.pattern == "peel")
                {
                    float p = SStep(0.58f, 0.6f, Fbm(u, v, 5, seed + 2));
                    col = Color.Lerp(col, new Color(0.62f, 0.6f, 0.55f) * (0.8f + 0.2f * n2), p);
                    h -= p * 0.2f;
                }
                // hairline cracks
                float cr = Mathf.Abs(Fbm(u, v, 6, seed + 13) - 0.5f);
                float crack = SStep(0.012f, 0f, cr) * SStep(0.45f, 0.6f, Fbm(u, v, 2, seed + 21));
                col *= 1 - crack * 0.5f;
                h -= crack * 0.3f;
                break;
            }
            case "burnt":
            {
                float ash = SStep(0.5f, 0.7f, n1);
                col = Color.Lerp(s.a, s.b, ash);
                float blister = SStep(0.6f, 0.8f, Fbm(u, v, 16, seed + 4));
                h = 0.4f + blister * 0.4f + n2 * 0.2f;
                break;
            }
            case "plywood":
            {
                float g = Mathf.Sin((v * 20f + Fbm(u, v, 2, seed) * 14f) * Mathf.PI) * 0.5f + 0.5f;
                float knot = SStep(0.08f, 0f, Vector2.Distance(new Vector2(u, v), new Vector2(0.3f, 0.6f))) * 0.6f;
                col = Color.Lerp(s.a, s.b, g * 0.4f + knot);
                float seam = SStep(0f, 0.01f, Fract(u * 0.83f)) * SStep(1f, 0.99f, Fract(u * 0.83f));
                col *= Mathf.Lerp(0.6f, 1f, seam);
                h = 0.4f + seam * 0.3f;
                break;
            }
            case "tile":
            case "ceiltile":
            {
                float grout = s.pattern == "tile" ? 0.04f : 0.02f;
                float x = Fract(u * 2f), y = Fract(v * 2f);
                float e = Mathf.Min(Mathf.Min(x, 1 - x), Mathf.Min(y, 1 - y));
                float g = SStep(grout * 0.5f, grout, e);
                float tint = Hash(Mathf.FloorToInt(u * 2f), Mathf.FloorToInt(v * 2f), 9);
                Color tileCol = Color.Lerp(s.a, s.a * 0.85f, tint * 0.6f + n1 * 0.2f);
                if (s.pattern == "ceiltile") tileCol *= 0.85f + 0.15f * SStep(0.4f, 0.6f, n2);
                col = Color.Lerp(s.b, tileCol, g);
                h = 0.3f + g * 0.5f + (s.pattern == "ceiltile" ? n2 * 0.2f : 0f);
                break;
            }
            case "hex":
            {
                float sx = u * 10f, sy = v * 10f * 1.1547f;
                var hexc = HexCell(sx, sy, out float edge);
                float g = SStep(0.04f, 0.1f, edge);
                float tint = Hash((int)hexc.x, (int)hexc.y, 3);
                Color tc = tint > 0.93f ? new Color(0.25f, 0.3f, 0.35f) : s.a * (0.92f + 0.08f * tint);
                col = Color.Lerp(s.b, tc, g);
                h = 0.3f + g * 0.5f;
                break;
            }
            case "checker":
            {
                int cx = Mathf.FloorToInt(u * 2f), cy = Mathf.FloorToInt(v * 2f);
                bool dark = ((cx + cy) & 1) == 1;
                float vein = Marble(u, v, seed + (dark ? 3 : 0));
                col = dark ? Color.Lerp(s.b, s.b * 1.8f, vein) : Color.Lerp(s.a, s.a * 0.65f, vein);
                float x = Fract(u * 2f), y = Fract(v * 2f);
                float e = Mathf.Min(Mathf.Min(x, 1 - x), Mathf.Min(y, 1 - y));
                col *= Mathf.Lerp(0.6f, 1f, SStep(0f, 0.01f, e));
                h = 0.45f + SStep(0f, 0.01f, e) * 0.1f;
                break;
            }
            case "marble":
            {
                float vein = Marble(u, v, seed);
                col = Color.Lerp(s.a, s.b, vein);
                float x = Fract(u), y = Fract(v * 2f);
                float e = Mathf.Min(Mathf.Min(x, 1 - x), Mathf.Min(y, 1 - y));
                col *= Mathf.Lerp(0.75f, 1f, SStep(0f, 0.006f, e));
                h = 0.5f;
                break;
            }
            case "lino":
            {
                float fleck = Hash(Mathf.FloorToInt(u * 256), Mathf.FloorToInt(v * 256), 2);
                col = Color.Lerp(s.a, s.b, n1 * 0.6f) * (fleck > 0.9f ? 1.2f : fleck < 0.1f ? 0.8f : 1f);
                float x = Fract(u * 3.3f), y = Fract(v * 3.3f);
                float e = Mathf.Min(Mathf.Min(x, 1 - x), Mathf.Min(y, 1 - y));
                col *= Mathf.Lerp(0.8f, 1f, SStep(0f, 0.008f, e));
                break;
            }
            case "brick":
            {
                float bw = 0.25f, bh = 0.085f;
                float row = Mathf.Floor(v / bh);
                float x = u / bw + ((int)row % 2 == 0 ? 0f : 0.5f);
                float fx = Fract(x), fy = Fract(v / bh);
                float mort = Mathf.Min(SStep(0f, 0.06f, Mathf.Min(fx, 1 - fx) * bw / bh), SStep(0f, 0.12f, Mathf.Min(fy, 1 - fy)));
                float tint = Hash(Mathf.FloorToInt(x), (int)row, 4);
                Color brick = s.a * (0.78f + 0.45f * tint) * (0.85f + 0.3f * n2);
                if (tint > 0.92f) brick *= 0.6f;
                col = Color.Lerp(s.b * (0.8f + 0.3f * n1), brick, mort);
                h = 0.2f + mort * 0.6f + n2 * 0.15f;
                break;
            }
            case "stone":
            {
                float row = Mathf.Floor(v * 2f);
                float x = u * 1.5f + Hash((int)row, 1) * 0.7f;
                float fx = Fract(x), fy = Fract(v * 2f);
                float m = SStep(0f, 0.03f, Mathf.Min(Mathf.Min(fx, 1 - fx), Mathf.Min(fy, 1 - fy)));
                col = Color.Lerp(s.b * 0.7f, Color.Lerp(s.a, s.b, n1) * (0.85f + 0.3f * Hash(Mathf.FloorToInt(x), (int)row, 8)), m);
                h = 0.25f + m * 0.5f + n2 * 0.25f;
                break;
            }
            case "concrete":
            {
                float pit = Hash(Mathf.FloorToInt(u * 300), Mathf.FloorToInt(v * 300), 6) > 0.985f ? 1 : 0;
                float stain = SStep(0.55f, 0.8f, Fbm(u, v, 2, seed + 8));
                col = Color.Lerp(s.a, s.b, n1 * 0.6f + stain * 0.5f) * (1 - pit * 0.4f);
                h = 0.5f + (n2 - 0.5f) * 0.4f - pit * 0.3f;
                break;
            }
            case "carpet":
            {
                // classic hotel carpet: medallions on a lattice, a vine scroll between them, wear in the middle
                float gu = Fract(u * 2f) - 0.5f, gv = Fract(v * 2f) - 0.5f;
                float r = Mathf.Sqrt(gu * gu + gv * gv), th = Mathf.Atan2(gv, gu);
                float med = SStep(0.03f, 0f, Mathf.Abs(r - (0.22f + 0.05f * Mathf.Cos(th * 8f))));
                float core = SStep(0.02f, 0f, r - 0.08f - 0.03f * Mathf.Cos(th * 4f));
                float diag = SStep(0.015f, 0f, Mathf.Abs(Fract((u + v) * 4f) - 0.5f) - 0.47f) * SStep(0.3f, 0.36f, r);
                float vine = SStep(0.02f, 0f, Mathf.Abs(Mathf.Sin(u * 25f + Mathf.Sin(v * 12f) * 2f) * 0.1f - (gv * 0.2f))) * SStep(0.3f, 0.4f, r) * 0.6f;
                float m = Mathf.Clamp01(med + core + diag * 0.8f + vine);
                col = Color.Lerp(s.a, s.b, m * 0.85f);
                col = Color.Lerp(col, s.c, SStep(0.35f, 0.5f, r) * 0.25f);
                float fiber = Hash(Mathf.FloorToInt(u * 512), Mathf.FloorToInt(v * 512), 1);
                col *= 0.86f + 0.2f * fiber;
                h = 0.4f + fiber * 0.3f + m * 0.1f;
                break;
            }
            case "grating":
            {
                float x = Fract(u * 7f), y = Fract(v * 7f);
                float bar = Mathf.Max(SStep(0.62f, 0.66f, x), SStep(0.62f, 0.66f, y));
                alphaOut = bar > 0.5f ? 1f : 0f;
                col = Color.Lerp(s.a, s.b, n1) * (0.8f + 0.4f * n2);
                h = bar;
                break;
            }
            case "glass":
            {
                // dust and finger-smudges: thicker, greyer patches
                float dirt = SStep(0.55f, 0.8f, Fbm(u, v, 3, seed));
                col = Color.Lerp(s.a, s.b, dirt);
                alphaOut = s.alpha + dirt * 0.25f;
                h = 0.5f + (n2 - 0.5f) * 0.05f;
                break;
            }
            case "decal":
            {
                // blob with a ragged edge; c.r = clustered spots, c.g = vertical streaks, c.b = thin crack lines
                float x = u - 0.5f, y = v - 0.5f;
                float r = Mathf.Sqrt(x * x + y * y) * 2f;
                float edge = Fbm(u, v, 4, seed + 7) * 0.5f;
                float blob = SStep(1.0f, 0.55f, r + edge - 0.25f);
                float inner = Fbm(u, v, 10, seed + 3);
                float a = blob * (0.45f + 0.55f * inner);
                if (s.c.g > 0.5f)                        // streaks running down
                {
                    float st = Fbm(u * 6f, v * 0.6f, 3, seed + 11);
                    a = SStep(0.45f, 0.75f, st) * SStep(0.5f, 0.0f, Mathf.Abs(x)) * (0.4f + 0.6f * v);
                }
                if (s.c.r > 0.5f)                        // mould colonies
                    a = blob * SStep(0.45f, 0.65f, Fbm(u, v, 18, seed + 5));
                if (s.c.b > 0.5f)                        // branching crack
                {
                    float cr = Mathf.Abs(Fbm(u, v, 3, seed + 13) - 0.5f) + Mathf.Abs(Fbm(u, v, 7, seed + 17) - 0.5f) * 0.4f;
                    a = SStep(0.03f, 0.0f, cr) * blob;
                }
                if (name == "Decal_WaterStain")             // dark tide-line at the edge of the dried stain
                    a = Mathf.Max(a * 0.6f, SStep(0.08f, 0.0f, Mathf.Abs(r + edge - 0.8f)) * 0.9f * SStep(1.2f, 0.9f, r));
                if (name == "Decal_Torn")
                    a = SStep(0.62f, 0.58f, r + edge * 1.6f - 0.2f);
                col = Color.Lerp(s.a, s.b, inner);
                alphaOut = Mathf.Clamp01(a) * s.alpha;
                h = 0.5f - a * 0.1f;
                break;
            }
            case "water":
                col = Color.Lerp(s.a, s.b, n1);
                h = 0.5f + (Fbm(u, v, 6, seed) - 0.5f) * 0.4f;
                break;
            case "coffer":
            {
                float x = Fract(u * 1f), y = Fract(v * 1f);
                float e = Mathf.Min(Mathf.Min(x, 1 - x), Mathf.Min(y, 1 - y));
                float step1 = SStep(0.08f, 0.1f, e), step2 = SStep(0.16f, 0.18f, e);
                float rosette = SStep(0.06f, 0.0f, Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) - 0.08f);
                col = Color.Lerp(s.b, s.a, step1 * 0.6f + step2 * 0.4f);
                col = Color.Lerp(col, new Color(0.72f, 0.56f, 0.25f), rosette + (1 - step1) * 0.3f);
                h = 0.2f + step1 * 0.3f + step2 * 0.3f + rosette * 0.2f;
                break;
            }
            case "skylight":
            {
                float x = Fract(u * 2f), y = Fract(v * 2f);
                float frame = Mathf.Max(SStep(0.93f, 0.95f, Mathf.Max(x, 1 - x)), SStep(0.93f, 0.95f, Mathf.Max(y, 1 - y)));
                col = Color.Lerp(Color.Lerp(s.a, s.b, n1), new Color(0.05f, 0.05f, 0.05f), frame);
                h = 0.5f - frame * 0.3f;
                break;
            }
            case "fabric":
            case "velvet":
            {
                float wx = Mathf.Sin(u * 512f * Mathf.PI) * 0.5f + 0.5f, wy = Mathf.Sin(v * 512f * Mathf.PI) * 0.5f + 0.5f;
                float weave = (wx * wy);
                col = Color.Lerp(s.a, s.b, n1 * 0.5f + weave * (s.pattern == "fabric" ? 0.2f : 0.05f));
                h = 0.5f + weave * 0.2f;
                break;
            }
            case "leather":
            {
                float cr = SStep(0.03f, 0f, Mathf.Abs(Fbm(u, v, 20, seed) - 0.5f));
                col = Color.Lerp(s.a, s.b, n1 * 0.6f) * (1 - cr * 0.3f);
                h = 0.5f - cr * 0.3f + n2 * 0.1f;
                break;
            }
            case "metal":
            {
                float tarn = SStep(0.5f, 0.75f, n1);
                col = Color.Lerp(s.a, s.b, tarn);
                h = 0.5f + (n2 - 0.5f) * 0.1f;
                break;
            }
            case "brushed":
            {
                float br = Fbm(u * 0.05f, v * 8f, 16, seed, 3);
                col = Color.Lerp(s.a, s.b, br * 0.5f + n1 * 0.3f);
                h = 0.5f + (br - 0.5f) * 0.1f;
                break;
            }
            case "rust":
            {
                float r = SStep(0.4f, 0.7f, n1);
                col = Color.Lerp(new Color(0.18f, 0.17f, 0.16f), s.a * (0.8f + 0.4f * n2), r);
                col = Color.Lerp(col, new Color(0.45f, 0.22f, 0.08f), SStep(0.7f, 0.85f, n2) * 0.6f);
                h = 0.4f + r * 0.3f + n2 * 0.3f;
                break;
            }
            case "leaf":
            {
                float x = Fract(u * 6f) - 0.5f, y = Fract(v * 6f) - 0.5f;
                float leaf = SStep(0.03f, 0f, Mathf.Abs(x) * 1.6f + Mathf.Abs(y) - 0.42f);
                alphaOut = leaf;
                col = Color.Lerp(s.a, s.b, n1) * (0.8f + 0.3f * (0.5f - y));
                h = leaf;
                break;
            }
        }
    }

    static float Marble(float u, float v, float seed)
    {
        float t = Fbm(u, v, 3, seed, 5);
        float vein = Mathf.Abs(Mathf.Sin((u * 3f + v * 2f + t * 5f) * Mathf.PI));
        return Mathf.Pow(1f - vein, 7f) * 0.9f + Fbm(u, v, 12, seed + 2, 3) * 0.15f;
    }

    static Vector2 HexCell(float x, float y, out float edge)
    {
        // axial hex lattice; edge = distance to the cell border (0 at grout)
        Vector2 p = new Vector2(x, y * 0.8660254f);
        Vector2 a = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y / 0.8660254f));
        Vector2 best = a;
        float bd = 99f;
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
            {
                float cy = (a.y + j);
                float cx = a.x + i + ((int)cy % 2 == 0 ? 0f : 0.5f);
                float d = Vector2.Distance(new Vector2(x, y * 0.8660254f), new Vector2(cx, cy * 0.8660254f));
                if (d < bd) { bd = d; best = new Vector2(a.x + i, cy); }
            }
        edge = Mathf.Clamp01(0.5f - bd);
        return best;
    }

    // ------------------------------------------------------------------ assets
    static Texture2D grimeTex;

    static string SaveTex(string name, Color[] px, int size, bool normal)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, true, !normal ? false : true);
        t.SetPixels(px);
        t.Apply();
        string path = $"{TexDir}/{name}.png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(t);
        return path;
    }

    static void MakeGrime()
    {
        int N = 512;
        var px = new Color[N * N];
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                float u = (float)i / N, v = (float)j / N;
                float big = Fbm(u, v, 3, 91.3f, 5);
                float streak = Fbm(u * 0.2f, v, 9, 33f, 3);
                float g = 0.5f * (0.78f + 0.5f * big) * (0.9f + 0.2f * streak);
                float damp = SStep(0.62f, 0.72f, Fbm(u, v, 4, 51f, 4));
                g *= 1f - damp * 0.35f;
                px[j * N + i] = new Color(g, g * 0.99f, g * 0.95f, 1f);
            }
        SaveTex("Grime", px, N, false);
    }

    static void ConfigureTexture(string path, bool normal, bool alpha)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = !normal;
        ti.alphaIsTransparency = alpha;
        ti.mipMapsPreserveCoverage = alpha;      // cut-out grating/leaves keep their coverage at a distance
        ti.alphaTestReferenceValue = 0.5f;
        ti.mipmapEnabled = true;
        ti.anisoLevel = 8;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.maxTextureSize = 1024;
        ti.SaveAndReimport();
    }

    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    static bool rebuildMaterials;
    [MenuItem("Hotel/Rebuild Material Library and Scenes")]
    public static void RebuildMaterials()
    {
        rebuildMaterials=true;
        try { BuildBothScenes(); } finally { rebuildMaterials=false; }
    }

    static Material MakeMaterial(string name)
    {
        if (materials.TryGetValue(name, out var existing)) return existing;
        if(!rebuildMaterials)
        {
            existing=AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");
            if(existing){materials[name]=existing;return existing;}
        }
        var s = Describe(name);
        int N = s.pattern == "plain" || s.pattern == "metal" ? 128 : s.size;
        var albedo = new Color[N * N];
        var height = new float[N * N];
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                Pattern(s, name, (float)i / N, (float)j / N, out var col, out float h, out float a);
                col.a = s.surf == Surf.Transparent ? (s.pattern == "glass" || s.pattern == "decal" ? a : s.alpha) : a;
                albedo[j * N + i] = col;
                height[j * N + i] = h;
            }
        if (name.StartsWith("Wallpaper") || name.StartsWith("Paint_") || name.StartsWith("Plaster"))
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float u = (float)i / N, v = (float)j / N;
                    float sd = (name.GetHashCode() & 0xFF) * 0.37f;
                    float seam = SStep(0.012f, 0.0f, Mathf.Abs(Fract(u * 2f) - 0.5f) - 0.488f) * SStep(0.6f, 0.75f, Fbm(u, v, 3, sd + 41));
                    float fade = 0.88f + 0.12f * Fbm(u, v, 2, sd + 43);
                    var c0 = albedo[j * N + i];
                    var faded = Color.Lerp(c0, new Color(0.55f, 0.5f, 0.42f), 0.18f) * fade;
                    faded = Color.Lerp(faded, new Color(0.62f, 0.6f, 0.54f), seam * 0.6f);
                    faded.a = c0.a;
                    albedo[j * N + i] = faded;
                    height[j * N + i] += seam * 0.15f;
                }
        if (name.StartsWith("Carpet"))
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float u = (float)i / N, v = (float)j / N;
                    float worn = SStep(0.5f, 0.8f, Fbm(u, v, 2, 77f));
                    float stain = SStep(0.66f, 0.7f, Fbm(u, v, 5, 79f));
                    var c0 = albedo[j * N + i];
                    var d = Color.Lerp(c0, c0 * 0.55f + new Color(0.06f, 0.05f, 0.03f), worn * 0.6f);
                    d = Color.Lerp(d, new Color(0.12f, 0.09f, 0.05f), stain * 0.7f);
                    d.a = c0.a;
                    albedo[j * N + i] = d;
                }
        var normal = new Color[N * N];
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                float hl = height[j * N + (i + N - 1) % N], hr = height[j * N + (i + 1) % N];
                float hd = height[((j + N - 1) % N) * N + i], hu = height[((j + 1) % N) * N + i];
                var n = new Vector3((hl - hr) * s.bump * 4f, (hd - hu) * s.bump * 4f, 1f).normalized;
                normal[j * N + i] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        string ap = SaveTex(name + "_A", albedo, N, false);
        string np = SaveTex(name + "_N", normal, N, true);
        ConfigureTexture(ap, false, s.surf != Surf.Opaque);
        ConfigureTexture(np, true, false);

        var m = new Material(Shader.Find("Standard")) { name = name };
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(ap));
        m.mainTextureScale = new Vector2(1f / s.tile, 1f / s.tile);
        m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(np));
        m.SetFloat("_BumpScale", 1f);
        m.EnableKeyword("_NORMALMAP");
        m.SetFloat("_Glossiness", s.smooth);
        m.SetFloat("_Metallic", s.metal);
        if (s.grime > 0f && grimeTex)
        {
            m.SetTexture("_DetailAlbedoMap", grimeTex);
            m.SetTextureScale("_DetailAlbedoMap", new Vector2(1f / 7f, 1f / 7f));
            m.EnableKeyword("_DETAIL_MULX2");
        }
        if (s.emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", s.emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else m.SetColor("_EmissionColor", Color.black);
        if (s.surf == Surf.Cutout)
        {
            m.SetFloat("_Mode", 1);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = 2450;
        }
        else if (s.surf == Surf.Transparent)
        {
            m.SetFloat("_Mode", 3);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
        }
        AssetDatabase.CreateAsset(m, $"{MatDir}/{name}.mat");
        materials[name] = m;
        return m;
    }
}
