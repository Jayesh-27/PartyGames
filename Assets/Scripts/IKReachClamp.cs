using UnityEngine;

[DefaultExecutionOrder(100)]
public class ParentToController : MonoBehaviour
{
    [Header("Where the hand should go (the prefab's LeftHand / RightHand sync object)")]
    public Transform poseSource;

    [Header("IK Target (do NOT parent this to the sync object)")]
    public Transform ikTarget;

    [Header("Arm Bones (Root > Mid > Tip)")]
    public Transform root;
    public Transform mid;
    public Transform tip;

    [Header("Reach Limits")]
    [Range(0.8f, 0.999f)] public float maxStretch = 0.97f;
    public float minReach = 0.02f;

    [Header("Offset From Pose Source (local space)")]
    public Vector3 positionOffset = Vector3.zero;
    public Vector3 rotationOffset = Vector3.zero;

    void Update()
    {
        if (poseSource == null || ikTarget == null || root == null || mid == null || tip == null)
            return;

        Quaternion rot = poseSource.rotation * Quaternion.Euler(rotationOffset);
        Vector3 rawPos = poseSource.TransformPoint(positionOffset);

        float upper = Vector3.Distance(root.position, mid.position);
        float lower = Vector3.Distance(mid.position, tip.position);

        float max = (upper + lower) * maxStretch;
        float min = Mathf.Abs(upper - lower) + minReach;

        Vector3 toTarget = rawPos - root.position;
        float dist = Mathf.Clamp(toTarget.magnitude, min, max);
        Vector3 dir = toTarget.sqrMagnitude > 0.000001f ? toTarget.normalized : root.forward;

        ikTarget.SetPositionAndRotation(root.position + dir * dist, rot);
    }
}