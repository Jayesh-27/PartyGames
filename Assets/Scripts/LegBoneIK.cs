using UnityEngine;

public class LegBoneIK : MonoBehaviour
{
    [SerializeField] private Transform target;   // the pedal

    Vector3 restScale;
    float restDistance;

    void Start()
    {
        restScale = transform.localScale;
        restDistance = ToTarget().magnitude;
    }

    // Direction to the pedal, flattened onto the plane around the bone's local Z axis
    Vector3 ToTarget()
    {
        return Vector3.ProjectOnPlane(target.position - transform.position, transform.forward);
    }

    void LateUpdate()
    {
        Vector3 toTarget = ToTarget();

        // Rotate only around the bone's own local Z axis
        float angle = Vector3.SignedAngle(transform.up, toTarget, transform.forward);
        transform.Rotate(0f, 0f, angle, Space.Self);

        // Stretch along the bone so the foot reaches the pedal
        Vector3 scale = restScale;
        scale.y = restScale.y * toTarget.magnitude / restDistance;
        transform.localScale = scale;
    }
}