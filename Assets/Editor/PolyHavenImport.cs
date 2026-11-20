using System.IO;
using UnityEditor;

// Import settings for the Poly Haven models (Assets/Art/PolyHaven/<id>/, CC0): the FBX's materials come in through
// the URP material description (base colour and normal map found next to the model); normal maps are imported as
// normal maps, roughness / ambient occlusion / packed maps as linear data; no animation, cameras or lights.
public class PolyHavenImport : AssetPostprocessor
{
    static bool Ours(string path) => path.Replace('\\', '/').Contains("/Art/PolyHaven/");

    void OnPreprocessTexture()
    {
        if (!Ours(assetPath)) return;
        var ti = (TextureImporter)assetImporter;
        string n = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
        if (n.Contains("_nor_gl") || n.Contains("_nor_dx") || n.Contains("_normal")) ti.textureType = TextureImporterType.NormalMap;
        else if (n.Contains("_rough") || n.Contains("_arm") || n.Contains("_metal") || n.Contains("_ao") || n.Contains("_spec") || n.Contains("_disp"))
            ti.sRGBTexture = false;
        ti.maxTextureSize = 1024;
        ti.mipmapEnabled = true;
    }

    void OnPreprocessModel()
    {
        if (!Ours(assetPath)) return;
        var mi = (ModelImporter)assetImporter;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        mi.importAnimation = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.importCameras = false;
        mi.importLights = false;
        mi.importBlendShapes = false;
        mi.useFileScale = true;
        mi.isReadable = false;
    }
}
