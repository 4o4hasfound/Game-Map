using UnityEngine;
using UnityEngine.Rendering;

// A door plate or a direction sign: the text is made when its part of the building is switched on (the font comes
// from the system, like the HUD's). Room numbers on the guest doors, names on the staff doors, arrows at junctions.
// The built-in font material draws on top of everything (it was made for UI), so the text showed through walls:
// the plates share a copy of it that tests depth like any other surface.
public class Plaque : MonoBehaviour
{
    [TextArea] public string text = "";
    public float charSize = 0.012f;
    public Color color = new Color(0.12f, 0.1f, 0.08f);
    public bool engraved = true;          // dark letters on brass; false: light letters on a dark sign
    public Material textMaterial;         // TheTape/Text3D (set by the builder, so the shader is in the build)
    static Font font;
    static Material depthTested;

    Material TextMaterial()
    {
        if (!font) font = Font.CreateDynamicFontFromOSFont(new[] { "Georgia", "Times New Roman", "Arial" }, 64);
        if (!depthTested)
        {
            depthTested = textMaterial ? new Material(textMaterial) : new Material(font.material);
            depthTested.name = "Plaque text (depth tested)";
            depthTested.mainTexture = font.material.mainTexture;
            // the dynamic font's glyph texture is rebuilt as new characters are needed: follow it
            Font.textureRebuilt += f => { if (f == font && depthTested) depthTested.mainTexture = font.material.mainTexture; };
        }
        return depthTested;
    }

    void Start()
    {
        var mat = TextMaterial();
        var go = new GameObject("Text");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, -0.006f);
        go.transform.localRotation = Quaternion.identity;
        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.text = text;
        tm.fontSize = 64;
        tm.characterSize = charSize;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = engraved ? color : new Color(0.86f, 0.84f, 0.78f);
        font.RequestCharactersInTexture(text, 64);
        mat.mainTexture = font.material.mainTexture;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }
}
