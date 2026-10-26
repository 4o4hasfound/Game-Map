using System.Collections.Generic;
using UnityEngine;

// Escalator: a chain of steps that stay level while they travel along the incline. When running it carries the
// player (adds velocity along the band) and clatters; the stopped one is just very tall stairs.
public class Escalator : MonoBehaviour
{
    public Vector3 bottom, top;        // centre line, at step level
    public float width = 1.0f;
    public float speed = 0.5f;
    public bool running = true;
    public Material stepMaterial;
    public float stepDepth = 0.4f;

    readonly List<Transform> steps = new List<Transform>();
    Vector3 fwd;
    float flat = 0.8f, lengthTotal, offset;
    BoxCollider carry;

    void Start()
    {
        fwd = top - bottom;
        fwd.y = 0f;
        fwd.Normalize();
        float incline = Vector3.Distance(bottom, top);
        lengthTotal = flat + incline + flat;
        int n = Mathf.CeilToInt(lengthTotal / stepDepth);
        var rot = Quaternion.LookRotation(fwd, Vector3.up);
        for (int i = 0; i < n; i++)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(s.GetComponent<Collider>());
            s.name = "Step";
            s.transform.SetParent(transform, true);
            s.transform.localScale = new Vector3(width, 0.18f, stepDepth * 0.96f);
            s.transform.rotation = rot;
            if (stepMaterial) s.GetComponent<Renderer>().sharedMaterial = stepMaterial;
            steps.Add(s.transform);
        }
        // trigger volume over the band that carries the player
        var go = new GameObject("CarryZone");
        go.transform.SetParent(transform, false);
        go.transform.position = (bottom + top) * 0.5f + Vector3.up * 0.8f;
        go.transform.rotation = rot;
        carry = go.AddComponent<BoxCollider>();
        carry.isTrigger = true;
        carry.size = new Vector3(width + 0.2f, Mathf.Abs(top.y - bottom.y) + 2f, lengthTotal);
        go.layer = 2;
        var z = go.AddComponent<EscalatorZone>();
        z.owner = this;
        Place();
    }

    public Vector3 CarryVelocity
    {
        get
        {
            if (!running) return Vector3.zero;
            return (top - bottom).normalized * speed;
        }
    }

    void Update()
    {
        if (running) offset = Mathf.Repeat(offset + speed * Time.deltaTime, stepDepth);
        Place();
    }

    void Place()
    {
        float incline = Vector3.Distance(bottom, top);
        for (int i = 0; i < steps.Count; i++)
        {
            float s = i * stepDepth + offset;
            if (s > lengthTotal) s -= lengthTotal;
            Vector3 p;
            if (s < flat) p = bottom - fwd * (flat - s);
            else if (s < flat + incline) p = Vector3.Lerp(bottom, top, (s - flat) / incline);
            else p = top + fwd * (s - flat - incline);
            steps[i].position = p - Vector3.up * 0.09f;
        }
    }
}

public class EscalatorZone : MonoBehaviour
{
    public Escalator owner;

    void OnTriggerStay(Collider other)
    {
        var pm = other.GetComponentInParent<PlayerMotor>();
        if (pm && pm.IsGrounded) pm.AddCarry(owner.CarryVelocity);
    }
}
