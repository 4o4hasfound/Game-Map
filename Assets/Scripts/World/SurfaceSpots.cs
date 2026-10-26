using UnityEngine;

// Table tops of one floor, exported by the map generator; LooseObjects puts things on them when the floor is first
// entered (it needs that floor's colliders active to find where things fit).
public class SurfaceSpots : MonoBehaviour
{
    public Vector3[] pos;
    public float[] yaw, w, d;
    public string[] kind, roomKind;
    public bool populated;
}
