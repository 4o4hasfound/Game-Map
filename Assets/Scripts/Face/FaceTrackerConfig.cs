using Unity.InferenceEngine;
using UnityEngine;

// Asset in Resources that points the face tracker at its model, anchors and blit material (made by the builder).
public class FaceTrackerConfig : ScriptableObject
{
    public ModelAsset model;
    public TextAsset anchors;
    public Material normMat;
}
