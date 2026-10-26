using UnityEngine;

// A loose object (bottle, cup, tin, book). E picks it up and it is carried in front of the camera; E again puts
// it down, right mouse button or G throws it. Anything that hits something hard makes a sound the monster hears,
// so a thrown bottle can lure it away.
[RequireComponent(typeof(Rigidbody))]
public class Carryable : Interactable
{
    public enum Material { Glass, Ceramic, Tin, Book }
    public Material material;
    Rigidbody body;
    float lastHit;
    public bool Carried { get; private set; }

    public override string Prompt => "PICK UP";

    void Awake() => body = GetComponent<Rigidbody>();

    public override void Use(PlayerMotor player) => Interactor.Pick(this);

    public void Hold(bool on)
    {
        Carried = on;
        body.useGravity = !on;
        body.linearDamping = on ? 12f : 0.05f;
        body.angularDamping = on ? 8f : 0.2f;
        body.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        foreach (var c in GetComponents<Collider>()) if (!c.isTrigger) c.enabled = true;
    }

    // pull it towards the hold point every physics step while carried
    public void MoveTowards(Vector3 target, Quaternion rot)
    {
        Vector3 v = (target - body.position) / Time.fixedDeltaTime;
        body.linearVelocity = Vector3.ClampMagnitude(v * 0.35f, 6f);
        body.MoveRotation(Quaternion.Slerp(body.rotation, rot, 0.2f));
    }

    public void Throw(Vector3 velocity)
    {
        Hold(false);
        body.linearVelocity = velocity;
        body.angularVelocity = Random.insideUnitSphere * 6f;
    }

    void OnCollisionEnter(Collision c)
    {
        float v = c.relativeVelocity.magnitude;
        if (v < 1.1f || Time.time - lastHit < 0.12f || Carried) return;
        lastHit = Time.time;
        float k = Mathf.Clamp01((v - 1f) / 6f);
        string clip = material == Material.Tin ? "amb_pipe_knock" : material == Material.Book ? "land_wood" : "amb_glass";
        float vol = material == Material.Glass ? 0.15f + 0.6f * k : material == Material.Ceramic ? 0.1f + 0.45f * k :
                    material == Material.Tin ? 0.08f + 0.4f * k : 0.15f + 0.4f * k;
        float pitch = material == Material.Ceramic ? 1.15f : material == Material.Book ? 0.9f : 1f;
        OneShot(clip, transform.position, vol, pitch, 0.8f, 30f);
        NoiseBus.Emit(transform.position, 1f + 3.5f * k, true);
    }
}
