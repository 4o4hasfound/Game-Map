using UnityEngine;

// Keeps a depth-tested text material (Hotel/TextDepth) on a TextMesh pointing at its font's current texture;
// dynamic fonts rebuild their texture when new characters are needed.
[RequireComponent(typeof(TextMesh))]
public class DepthText : MonoBehaviour
{
    TextMesh text;
    MeshRenderer mr;

    void Start()
    {
        text = GetComponent<TextMesh>();
        mr = GetComponent<MeshRenderer>();
        Refresh(text.font);
        Font.textureRebuilt += Refresh;
    }

    void OnDestroy() => Font.textureRebuilt -= Refresh;

    void Refresh(Font f)
    {
        if (!text || f != text.font || !mr || !mr.sharedMaterial || !f || !f.material) return;
        mr.material.mainTexture = f.material.mainTexture;
    }
}
