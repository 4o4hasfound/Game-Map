using UnityEngine;

// The opening: you come to in front of a television that is showing what your own camcorder sees (a live feed, so
// pointing the camera at it gives the familiar tunnel of screens). The flashlight is not in your hand yet: it lies
// switched on on the floor nearby and has to be picked up (FlashlightPickup).
[DefaultExecutionOrder(50)]    // after HotelWorld has loaded the levels, before any Start
public class SpawnAtTV : MonoBehaviour
{
    public PlayerMotor player;
    public VHSCamera cam;
    public CamcorderLight flashlight;
    // created by the scene builder so their shaders are part of the build
    public Material feedMaterial, flashlightBody, flashlightLens;
    public static TVDevice SpawnTV { get; private set; }
    public static SpawnAtTV Instance { get; private set; }
    public static Vector3 SpawnPos { get; private set; }
    public static float SpawnYaw { get; private set; }
    public static float SpawnPitch { get; private set; }

    void Awake()
    {
        Instance = this;
        var world = HotelWorld.Instance;
        if (!player || !world || world.levels.Count == 0 || !world.levels[0].root) return;
        Vector3 origin = player.transform.position;
        TVDevice best = null;
        float bestD = float.MaxValue;
        foreach (var tv in world.levels[0].root.GetComponentsInChildren<TVDevice>(true))
        {
            float d = Vector3.Distance(tv.transform.position, origin);
            if (d < bestD && Stand(tv, out _)) { bestD = d; best = tv; }
        }
        if (!best) return;
        SpawnTV = best;
        best.liveFeed = true;
        best.startOn = true;
        best.gameObject.AddComponent<LiveFeedTV>();
        Stand(best, out var stand);
        Vector3 look = best.transform.Find("Front") ? best.transform.Find("Front").position : best.transform.position;
        Vector3 to = look - stand;
        to.y = 0f;
        player.transform.position = stand;
        var rb = player.GetComponent<Rigidbody>();
        if (rb) rb.position = stand;
        SpawnPos = stand;
        SpawnYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        if (cam)
        {
            cam.yaw = SpawnYaw;
            // eyes on the screen (VHSCamera pitch: positive looks down)
            float dist = new Vector2(to.x, to.z).magnitude;
            cam.pitch = Mathf.Min(18f, Mathf.Atan2(stand.y + 1.62f - look.y, Mathf.Max(dist, 0.5f)) * Mathf.Rad2Deg);
            SpawnPitch = cam.pitch;
        }
        PlacePickup(stand, to.normalized);
        Debug.Log($"[SpawnAtTV] {best.name}: stand {stand}, yaw {SpawnYaw:0}, pitch {(cam ? cam.pitch : 0f):0.0}");
    }

    // a clear standing spot on the floor about 2 m in front of the screen
    static bool Stand(TVDevice tv, out Vector3 stand)
    {
        var f = tv.transform.Find("Front");
        Vector3 fwd = f ? f.forward : tv.transform.forward;
        fwd.y = 0f;
        fwd.Normalize();
        Vector3 c = f ? f.position : tv.transform.position;
        int mask = (1 << MaterialAcoustics.StructureLayer) | (1 << MaterialAcoustics.PropLayer) | (1 << MaterialAcoustics.DoorLayer);
        foreach (float dist in new[] { 2.0f, 1.7f, 2.4f, 1.4f })
            foreach (float side in new[] { 0f, 0.4f, -0.4f })
            {
                Vector3 p = c + fwd * dist + Vector3.Cross(Vector3.up, fwd) * side;
                if (!Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var hit, 3f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider.gameObject.layer != MaterialAcoustics.StructureLayer) continue;     // on the floor, not on furniture
                Vector3 foot = hit.point;
                if (Physics.CheckCapsule(foot + Vector3.up * 0.35f, foot + Vector3.up * 1.6f, 0.3f, mask, QueryTriggerInteraction.Ignore)) continue;
                // can actually see the screen from there
                if (Physics.Linecast(foot + Vector3.up * 1.55f, c, out var block, mask, QueryTriggerInteraction.Ignore) &&
                    !block.collider.transform.IsChildOf(tv.transform)) continue;
                stand = foot + Vector3.up * 0.05f;
                return true;
            }
        stand = c;
        return false;
    }

    void PlacePickup(Vector3 stand, Vector3 toTv)
    {
        int mask = 1 << MaterialAcoustics.StructureLayer;
        Vector3 side = Vector3.Cross(Vector3.up, toTv);
        foreach (var offset in new[] { toTv * 0.8f + side * 0.55f, toTv * 0.8f - side * 0.55f, side * 0.8f, -side * 0.8f, toTv * 0.5f })
        {
            Vector3 p = stand + offset;
            if (!Physics.Raycast(p + Vector3.up * 0.6f, Vector3.down, out var hit, 1.5f, mask | (1 << MaterialAcoustics.PropLayer), QueryTriggerInteraction.Ignore)) continue;
            if (hit.collider.gameObject.layer != MaterialAcoustics.StructureLayer || Mathf.Abs(hit.point.y - stand.y) > 0.2f) continue;
            FlashlightPickup.Create(hit.point, Quaternion.LookRotation(Quaternion.Euler(0f, 55f, 0f) * toTv), flashlight);
            return;
        }
        FlashlightPickup.Create(stand + toTv * 0.6f - Vector3.up * 0.05f, Quaternion.LookRotation(toTv), flashlight);
    }
}
