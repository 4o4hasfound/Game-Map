using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// What your hands do when you use something. Nothing just shakes: things move the way they would.
//   Inspect    picked up along an arc, held in front of you and turned to look at, put back where it was
//              (photos, clocks, the TV guide, keys, the vase, the wine)
//   PullOut    one of a row slides out (a book, a tape), comes up to be read, goes back in
//   Bite       up to your mouth, a bite (it gets smaller), back down
//   Drink      up to your mouth and tipped for a sip, then down (cans, milk, the teacup)
//   Pour       tipped forward for a moment and set upright (the teapot)
//   Take       up to you and gone (the last bite, things you keep)
//   Press      pushed in a little and let go (buttons, knobs, levers, a switch on an appliance)
//   Flip       a rocker switch flips one way, then the other next time (it stays where it was flipped)
//   FlapUp     a flap lifts on its top edge and snaps shut (the letterbox)
//   Straighten turned back to straight (a crooked picture, the plates); see StraightTarget
//   Squash     a soft thing flattens and springs back (cushions, pillows)
//   Sway       it swings and settles (coats, plants)
//   Nudge      lifted a touch, turned a little, set down (anything else you handle)
//   Lean       you lean in to look (into a basket, at your shoes, to the fire)
//   Sit        you sit for a moment and get up again
// Static (batched) objects can't move: they are left alone. One runner object plays them all.
public class InteractAnim : MonoBehaviour
{
    public enum Kind { None, Wiggle, Press, Lift, Squash, Sway, Bite, Take, Sit, Inspect, PullOut, Drink, Pour, Flip, FlapUp, Straighten, Nudge, Lean }
    static InteractAnim runner;
    static InteractAnim R
    {
        get
        {
            if (!runner) runner = new GameObject("Interaction animations").AddComponent<InteractAnim>();
            return runner;
        }
    }
    static bool busyView;
    static readonly HashSet<Transform> moving = new HashSet<Transform>();
    static readonly Dictionary<Transform, bool> flipped = new Dictionary<Transform, bool>();

    // how long a kind keeps its object busy (it can't be used again until it's back)
    public static float Duration(Kind k) => k switch
    {
        Kind.Inspect => 2.3f, Kind.PullOut => 2.4f, Kind.Bite => 0.95f, Kind.Drink => 1.6f, Kind.Pour => 1.5f, Kind.Take => 0.5f,
        Kind.Lean => 2.2f, Kind.Sit => 1.9f, Kind.Straighten => 1.4f, Kind.FlapUp => 0.9f, Kind.Sway => 1.0f, Kind.Squash => 0.5f,
        Kind.Nudge => 0.6f, Kind.Lift => 0.7f, Kind.Press => 0.3f, Kind.Flip => 0.2f, _ => 0.3f,
    };

    public static void Play(Kind k, Transform t, System.Action done = null)
    {
        if (k == Kind.Wiggle) k = Kind.Nudge;                    // nothing shakes any more
        if (!t || k == Kind.None) { done?.Invoke(); return; }
        bool viewOnly = k == Kind.Sit || k == Kind.Lean || k == Kind.PullOut;
        if (t.gameObject.isStatic && !viewOnly) { done?.Invoke(); return; }
        if (!viewOnly && moving.Contains(t)) { done?.Invoke(); return; }
        R.StartCoroutine(R.Run(k, t, done));
    }

    static Transform Cam => Game.Instance && Game.Instance.PlayerCamera ? Game.Instance.PlayerCamera.transform : null;
    static Bounds BoundsOf(Transform t)
    {
        var rs = System.Array.FindAll(t.GetComponentsInChildren<Renderer>(), r => !(r is ParticleSystemRenderer));
        if (rs.Length == 0) return new Bounds(t.position, Vector3.one * 0.1f);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
    // where you hold something to look at it: in front of your face, a little low, further for bigger things
    static Vector3 HoldPoint(Transform cam, float size) => cam.position + cam.forward * Mathf.Clamp(0.32f + size * 1.1f, 0.38f, 0.8f) - cam.up * 0.07f;
    static Vector3 Mouth(Transform cam) => cam.position + cam.forward * 0.26f - cam.up * 0.12f;
    static Vector3 Arc(Vector3 a, Vector3 b, float f, float lift)
    {
        var c = (a + b) * 0.5f + Vector3.up * lift;
        return Vector3.Lerp(Vector3.Lerp(a, c, f), Vector3.Lerp(c, b, f), f);
    }
    static float Ease(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

    IEnumerator Run(Kind k, Transform t, System.Action done)
    {
        var cam = Cam;
        var p0 = t.position; var q0 = t.rotation; var s0 = t.localScale;
        bool restore = true;
        moving.Add(t);
        switch (k)
        {
            case Kind.Inspect:
            {
                if (!cam) break;
                var b = BoundsOf(t);
                float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
                var off = p0 - b.center;                                         // hold it by its middle
                var right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
                // up
                for (float e = 0f; e < 0.45f && t; e += Time.deltaTime)
                {
                    float f = Ease(e / 0.45f);
                    var hold = HoldPoint(cam, size) + off;
                    t.position = Arc(p0, hold, f, 0.12f);
                    t.rotation = Quaternion.AngleAxis(-18f * f, right) * q0;
                    yield return null;
                }
                // looked at: turned slowly one way and back, tilted towards you
                for (float e = 0f; e < 1.35f && t; e += Time.deltaTime)
                {
                    var hold = HoldPoint(cam, size) + off;
                    t.position = Vector3.Lerp(t.position, hold, 1f - Mathf.Exp(-Time.deltaTime * 12f));
                    float turn = Mathf.Sin(e / 1.35f * Mathf.PI * 2f) * 28f;
                    t.rotation = Quaternion.AngleAxis(turn, Vector3.up) * Quaternion.AngleAxis(-18f, right) * q0;
                    yield return null;
                }
                // put back
                var from = t ? t.position : p0; var fromQ = t ? t.rotation : q0;
                for (float e = 0f; e < 0.45f && t; e += Time.deltaTime)
                {
                    float f = Ease(e / 0.45f);
                    t.position = Arc(from, p0, f, 0.08f);
                    t.rotation = Quaternion.Slerp(fromQ, q0, f);
                    yield return null;
                }
                break;
            }
            case Kind.PullOut:
            {
                // one from the row: a slice of the row's own surface (a book, a tape) slides out, is read, goes back
                if (!cam) break;
                restore = false;
                var b = BoundsOf(t);
                var r = t.GetComponentInChildren<Renderer>();
                bool alongX = b.size.x >= b.size.z;
                float thick = Mathf.Clamp((alongX ? b.size.x : b.size.z) / 12f, 0.025f, 0.06f);
                var size = alongX ? new Vector3(thick, b.size.y * 0.92f, b.size.z * 0.85f) : new Vector3(b.size.x * 0.85f, b.size.y * 0.92f, thick);
                var one = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(one.GetComponent<Collider>());
                one.name = "Pulled out";
                if (r) one.GetComponent<Renderer>().sharedMaterial = r.sharedMaterial;
                one.transform.localScale = size;
                var toCam = Vector3.ProjectOnPlane(cam.position - b.center, Vector3.up).normalized;
                var start = b.center + toCam * (alongX ? b.extents.z : b.extents.x) * 0.2f;
                var outOf = start + toCam * 0.16f;
                one.transform.SetPositionAndRotation(start, Quaternion.identity);
                for (float e = 0f; e < 0.35f; e += Time.deltaTime) { one.transform.position = Vector3.Lerp(start, outOf, Ease(e / 0.35f)); yield return null; }
                var face = Quaternion.LookRotation(-toCam) * Quaternion.Euler(0f, alongX ? 90f : 0f, 0f);
                for (float e = 0f; e < 0.4f; e += Time.deltaTime)
                {
                    float f = Ease(e / 0.4f);
                    one.transform.position = Arc(outOf, HoldPoint(cam, 0.25f), f, 0.06f);
                    one.transform.rotation = Quaternion.Slerp(Quaternion.identity, face * Quaternion.Euler(-20f, 0f, 0f), f);
                    yield return null;
                }
                for (float e = 0f; e < 1.0f; e += Time.deltaTime)
                {
                    one.transform.position = Vector3.Lerp(one.transform.position, HoldPoint(cam, 0.25f), 1f - Mathf.Exp(-Time.deltaTime * 12f));
                    one.transform.rotation = face * Quaternion.Euler(-20f + Mathf.Sin(e * 3f) * 4f, Mathf.Sin(e * 2.2f) * 10f, 0f);
                    yield return null;
                }
                var at = one.transform.position; var aq = one.transform.rotation;
                for (float e = 0f; e < 0.4f; e += Time.deltaTime)
                {
                    float f = Ease(e / 0.4f);
                    one.transform.position = Arc(at, outOf, f, 0.04f);
                    one.transform.rotation = Quaternion.Slerp(aq, Quaternion.identity, f);
                    yield return null;
                }
                for (float e = 0f; e < 0.25f; e += Time.deltaTime) { one.transform.position = Vector3.Lerp(outOf, start, Ease(e / 0.25f)); yield return null; }
                Destroy(one);
                break;
            }
            case Kind.Bite:
            case Kind.Drink:
            {
                if (!cam) break;
                bool drink = k == Kind.Drink;
                var right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
                for (float e = 0f; e < 0.32f && t; e += Time.deltaTime)
                {
                    float f = Ease(e / 0.32f);
                    t.position = Arc(p0, Mouth(cam), f, 0.1f);
                    if (drink) t.rotation = Quaternion.AngleAxis(-25f * f, right) * q0;
                    yield return null;
                }
                if (drink)
                {
                    // tipped up for a sip
                    for (float e = 0f; e < 0.25f && t; e += Time.deltaTime) { t.position = Mouth(cam); t.rotation = Quaternion.AngleAxis(Mathf.Lerp(-25f, -70f, Ease(e / 0.25f)), right) * q0; yield return null; }
                    for (float e = 0f; e < 0.55f && t; e += Time.deltaTime) { t.position = Mouth(cam) + cam.up * Mathf.Sin(e * 9f) * 0.004f; yield return null; }
                    for (float e = 0f; e < 0.2f && t; e += Time.deltaTime) { t.rotation = Quaternion.AngleAxis(Mathf.Lerp(-70f, -20f, Ease(e / 0.2f)), right) * q0; yield return null; }
                }
                else
                {
                    // the bite: in, a tug back, and it's a little smaller from now on
                    for (float e = 0f; e < 0.18f && t; e += Time.deltaTime)
                    {
                        float f = Mathf.Sin(e / 0.18f * Mathf.PI);
                        t.position = Mouth(cam) + cam.forward * -0.03f * f;
                        yield return null;
                    }
                    if (t) { s0 *= 0.86f; t.localScale = s0; }
                }
                var from = t ? t.position : p0; var fromQ = t ? t.rotation : q0;
                for (float e = 0f; e < 0.3f && t; e += Time.deltaTime)
                {
                    float f = Ease(e / 0.3f);
                    t.position = Arc(from, p0, f, 0.06f);
                    t.rotation = Quaternion.Slerp(fromQ, q0, f);
                    yield return null;
                }
                break;
            }
            case Kind.Pour:
            {
                var axis = cam ? Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized : t.right;
                for (float e = 0f; e < 0.35f && t; e += Time.deltaTime) { float f = Ease(e / 0.35f); t.position = p0 + Vector3.up * 0.06f * f; t.rotation = Quaternion.AngleAxis(42f * f, axis) * q0; yield return null; }
                for (float e = 0f; e < 0.8f && t; e += Time.deltaTime) { t.rotation = Quaternion.AngleAxis(42f + Mathf.Sin(e * 6f) * 2f, axis) * q0; yield return null; }
                for (float e = 0f; e < 0.35f && t; e += Time.deltaTime) { float f = 1f - Ease(e / 0.35f); t.position = p0 + Vector3.up * 0.06f * f; t.rotation = Quaternion.AngleAxis(42f * f, axis) * q0; yield return null; }
                break;
            }
            case Kind.Take:
            {
                if (!cam) break;
                for (float e = 0f; e < 0.36f && t; e += Time.deltaTime)
                {
                    float f = Ease(e / 0.36f);
                    t.position = Arc(p0, cam.position + cam.forward * 0.3f - cam.up * 0.2f, f, 0.12f);
                    t.localScale = s0 * Mathf.Lerp(1f, 0.6f, f * f);
                    yield return null;
                }
                for (float e = 0f; e < 0.1f && t; e += Time.deltaTime) { t.localScale = s0 * 0.6f * (1f - e / 0.1f); yield return null; }
                moving.Remove(t);
                done?.Invoke();
                if (t) { t.position = p0; t.localScale = s0; }
                yield break;
            }
            case Kind.Press:
            {
                // pushed a centimetre or so away from you, and it springs back
                var away = cam ? (p0 - cam.position).normalized : -t.forward;
                for (float e = 0f; e < 0.26f && t; e += Time.deltaTime)
                {
                    float f = e < 0.08f ? e / 0.08f : 1f - Ease((e - 0.08f) / 0.18f);
                    t.position = p0 + away * 0.014f * f;
                    yield return null;
                }
                break;
            }
            case Kind.Flip:
            {
                // a rocker: from the middle it tips one way; after that it flips right over to the other side
                restore = false;
                flipped.TryGetValue(t, out bool on);
                on = !on; flipped[t] = on;
                float delta = flippedOnce.Add(t) ? (on ? -14f : 14f) : (on ? -28f : 28f);
                if (!flipAxis.TryGetValue(t, out var axis))
                {
                    var toCam = cam ? Vector3.ProjectOnPlane(cam.position - p0, Vector3.up) : -t.forward;
                    axis = Vector3.Cross(Vector3.up, toCam.sqrMagnitude > 1e-6f ? toCam.normalized : Vector3.forward);
                    flipAxis[t] = axis;
                }
                var target = Quaternion.AngleAxis(delta, axis) * q0;
                for (float e = 0f; e < 0.09f && t; e += Time.deltaTime) { t.rotation = Quaternion.Slerp(q0, target, e / 0.09f); yield return null; }
                if (t) t.rotation = target;
                break;
            }
            case Kind.FlapUp:
            {
                restore = false;
                var b = BoundsOf(t);
                var hinge = b.center + Vector3.up * b.extents.y;
                var axis = cam ? Vector3.Cross(Vector3.up, (cam.position - b.center).normalized).normalized : t.right;
                float a = 0f;
                for (float e = 0f; e < 0.18f && t; e += Time.deltaTime) { float na = Mathf.Lerp(0f, 70f, Ease(e / 0.18f)); t.RotateAround(hinge, axis, na - a); a = na; yield return null; }
                for (float e = 0f; e < 0.45f && t; e += Time.deltaTime) yield return null;
                for (float e = 0f; e < 0.1f && t; e += Time.deltaTime) { float na = Mathf.Lerp(70f, 0f, e / 0.1f); t.RotateAround(hinge, axis, na - a); a = na; yield return null; }
                if (t) t.SetPositionAndRotation(p0, q0);
                break;
            }
            case Kind.Straighten:
            {
                restore = false;
                var st = t.GetComponent<StraightTarget>();
                if (!st) goto case Kind.Nudge;
                var crooked = t.rotation;
                for (float e = 0f; e < 0.45f && t; e += Time.deltaTime) { t.rotation = Quaternion.Slerp(crooked, st.straight, Ease(e / 0.45f)); yield return null; }
                if (st.slipBack > 0f)
                {
                    // and it slips back (it's still crooked)
                    for (float e = 0f; e < 0.5f; e += Time.deltaTime) yield return null;
                    var slip = Quaternion.AngleAxis(st.slipBack, st.slipAxis) * st.straight;
                    for (float e = 0f; e < 0.35f && t; e += Time.deltaTime) { t.rotation = Quaternion.Slerp(st.straight, slip, Mathf.Sqrt(Mathf.Clamp01(e / 0.35f))); yield return null; }
                }
                break;
            }
            case Kind.Nudge:
            {
                restore = false;
                float turn = Random.Range(-7f, 7f);
                for (float e = 0f; e < 0.25f && t; e += Time.deltaTime) { float f = Ease(e / 0.25f); t.position = p0 + Vector3.up * 0.025f * f; t.rotation = Quaternion.AngleAxis(turn * f, Vector3.up) * q0; yield return null; }
                for (float e = 0f; e < 0.25f && t; e += Time.deltaTime) { float f = 1f - Ease(e / 0.25f); t.position = p0 + Vector3.up * 0.025f * f; yield return null; }
                if (t) t.position = p0;
                break;
            }
            case Kind.Lift:
            {
                var toward = cam ? Vector3.ProjectOnPlane(cam.position - p0, Vector3.up).normalized : Vector3.zero;
                var right = cam ? Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized : t.right;
                for (float e = 0f; e < 0.7f && t; e += Time.deltaTime)
                {
                    float f = Mathf.Sin(e / 0.7f * Mathf.PI);
                    t.position = p0 + Vector3.up * 0.1f * f + toward * 0.08f * f;
                    t.rotation = Quaternion.AngleAxis(-22f * f, right) * q0;
                    yield return null;
                }
                break;
            }
            case Kind.Squash:
            {
                // pressed flat, then plumped up a bit past where it was, and settled
                for (float e = 0f; e < 0.5f && t; e += Time.deltaTime)
                {
                    float x = e / 0.5f;
                    float f = x < 0.35f ? Ease(x / 0.35f) : 1f - Ease((x - 0.35f) / 0.65f) * 1.15f;
                    f = x > 0.85f ? Mathf.Lerp(-0.15f, 0f, (x - 0.85f) / 0.15f) : f;
                    t.localScale = Vector3.Scale(s0, new Vector3(1f + 0.12f * f, 1f - 0.3f * f, 1f + 0.12f * f));
                    yield return null;
                }
                break;
            }
            case Kind.Sway:
                for (float e = 0f; e < 1.0f && t; e += Time.deltaTime)
                {
                    float f = 1f - e;
                    t.rotation = q0 * Quaternion.Euler(Mathf.Sin(e * 9f) * 4f * f, 0f, Mathf.Sin(e * 7f) * 7f * f);
                    yield return null;
                }
                break;
            case Kind.Lean:
            case Kind.Sit:
            {
                restore = false;
                var g = Game.Instance;
                if (!g || !g.player || busyView || g.player.HideView) break;
                busyView = true;
                var view = new GameObject(k == Kind.Sit ? "Sit view" : "Lean view").transform;
                var c = g.PlayerCamera.transform;
                view.SetPositionAndRotation(c.position, Quaternion.Euler(c.eulerAngles.x, c.eulerAngles.y, 0f));
                var start = view.position; var startQ = view.rotation;
                var target = BoundsOf(t).center;
                var toward = Vector3.ProjectOnPlane(target - start, Vector3.up);
                float reach = Mathf.Min(toward.magnitude * 0.4f, 0.55f);
                var leanPos = k == Kind.Sit ? start + Vector3.down * 0.5f : start + toward.normalized * reach + Vector3.down * 0.22f;
                var leanQ = k == Kind.Sit ? startQ * Quaternion.Euler(-5f, 0f, 0f) : Quaternion.LookRotation(target - leanPos);
                float hold = k == Kind.Sit ? 0.9f : 1.2f;
                g.player.HideView = view;
                g.player.Locked = true;
                for (float e = 0f; e < 0.45f; e += Time.deltaTime) { float f = Ease(e / 0.45f); view.SetPositionAndRotation(Vector3.Lerp(start, leanPos, f), Quaternion.Slerp(startQ, leanQ, f)); yield return null; }
                for (float e = 0f; e < hold; e += Time.deltaTime) { view.position = leanPos + Vector3.up * Mathf.Sin(e * 2.4f) * 0.006f; yield return null; }
                for (float e = 0f; e < 0.5f; e += Time.deltaTime) { float f = Ease(e / 0.5f); view.SetPositionAndRotation(Vector3.Lerp(leanPos, start, f), Quaternion.Slerp(leanQ, startQ, f)); yield return null; }
                if (g.player.HideView == view) { g.player.HideView = null; g.player.Locked = false; }
                Destroy(view.gameObject);
                busyView = false;
                break;
            }
        }
        if (t && restore) { t.SetPositionAndRotation(p0, q0); t.localScale = s0; }
        moving.Remove(t);
        done?.Invoke();
    }
    static readonly HashSet<Transform> flippedOnce = new HashSet<Transform>();
    static readonly Dictionary<Transform, Vector3> flipAxis = new Dictionary<Transform, Vector3>();
}
