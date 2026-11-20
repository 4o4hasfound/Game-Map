using UnityEngine;

// The way a crooked thing is straight (set by the builder, which then leaves it crooked: the painting, the plates).
// slipBack: degrees it slips back after you straighten it (the painting that's still crooked). See InteractAnim.
public class StraightTarget : MonoBehaviour
{
    public Quaternion straight = Quaternion.identity;
    public float slipBack;
    public Vector3 slipAxis = Vector3.forward;     // turned about this (world) axis when it slips: the wall's normal
}
