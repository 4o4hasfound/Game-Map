using UnityEngine;

// F2: headphone direction test. A clicking beacon circles the head once (front -> right -> back -> left), then
// sweeps from below to above in front. The OSD names where it should be heard.
public class AudioTest : MonoBehaviour
{
    public Transform head;
    public static bool Running { get; private set; }
    public static string Where { get; private set; } = "";

    float t;
    float nextClick;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F2)) Begin();
        if (!Running || !head || !AudioEngine.Instance) return;
        t += Time.deltaTime;
        const float orbit = 10f, sweep = 5f;
        Vector3 local;
        if (t < orbit)
        {
            float a = t / orbit * Mathf.PI * 2f;
            local = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 1.8f;
            float deg = Mathf.Repeat(a * Mathf.Rad2Deg, 360f);
            Where = deg < 22 || deg > 338 ? "FRONT" : deg < 68 ? "FRONT-RIGHT" : deg < 112 ? "RIGHT" : deg < 158 ? "BACK-RIGHT" :
                deg < 202 ? "BEHIND" : deg < 248 ? "BACK-LEFT" : deg < 292 ? "LEFT" : "FRONT-LEFT";
        }
        else if (t < orbit + sweep)
        {
            float e = Mathf.Lerp(-60f, 85f, (t - orbit) / sweep) * Mathf.Deg2Rad;
            local = new Vector3(0f, Mathf.Sin(e), Mathf.Cos(e)) * 1.8f;
            Where = e < -0.3f ? "BELOW" : e < 0.4f ? "FRONT" : e < 1.1f ? "ABOVE-FRONT" : "OVERHEAD";
        }
        else
        {
            Running = false;
            Where = "";
            return;
        }
        if (Time.time >= nextClick)
        {
            nextClick = Time.time + 0.25f;
            // fixed to the head, so turning does not change it
            Vector3 p = head.TransformPoint(local);
            var v = AudioEngine.Instance.PlayAt(SoundBank.Get("test_beacon"), p, 0.8f, 1f, 1f, 10f);
            if (v) v.occlusion = false;
        }
        if (VHSOverlay.Instance) VHSOverlay.Instance.Announce("HEADPHONE TEST: " + Where, 0.4f);
    }

    public void Begin()
    {
        t = 0f;
        Running = true;
    }
}
