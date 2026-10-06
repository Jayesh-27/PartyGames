using UnityEngine;

[DefaultExecutionOrder(100)]
public class ParentToController : MonoBehaviour
{
    public enum Hand { Left, Right }

    [Header("Controller")]
    public Hand hand;

    [Header("IK Target (do NOT parent this to the controller)")]
    public Transform ikTarget;

    [Header("Arm Bones (Root > Mid > Tip)")]
    public Transform root;   // upper arm / shoulder
    public Transform mid;    // elbow
    public Transform tip;    // hand

    [Header("Reach Limits")]
    [Range(0.8f, 0.999f)] public float maxStretch = 0.97f;
    public float minReach = 0.02f;

    [Header("Offset From Controller (controller local space)")]
    public Vector3 positionOffset = Vector3.zero;
    public Vector3 rotationOffset = Vector3.zero;

    [Header("Editor Testing (no headset)")]
    public bool useDebugSource;
    public Transform debugSource;

    private OVRCameraRig cameraRig;
    private Transform controllerAnchor;

    private void Start()
    {
        if (ikTarget == null || root == null || mid == null || tip == null)
        {
            Debug.LogError("IK Target, Root, Mid and Tip must all be assigned.", this);
            enabled = false;
            return;
        }

        FindAnchor();
    }

    private void FindAnchor()
    {
        if (cameraRig == null)
            cameraRig = FindFirstObjectByType<OVRCameraRig>();

        if (cameraRig == null)
            return;

        controllerAnchor = (hand == Hand.Left)
            ? cameraRig.leftControllerAnchor
            : cameraRig.rightControllerAnchor;
    }

    private void Update()
    {
        Quaternion rot;
        Vector3 rawPos;

        // Editor-only fake controller. Ignored in builds on the headset.
        if (useDebugSource && debugSource != null && Application.isEditor)
        {
            rot = debugSource.rotation;
            rawPos = debugSource.position;
        }
        else
        {
            if (controllerAnchor == null)
            {
                FindAnchor();
                if (controllerAnchor == null)
                    return;
            }

            rot = controllerAnchor.rotation * Quaternion.Euler(rotationOffset);
            rawPos = controllerAnchor.TransformPoint(positionOffset);
        }
        // Bone lengths in world space, measured every frame so scaling the character is safe
        float upper = Vector3.Distance(root.position, mid.position);
        float lower = Vector3.Distance(mid.position, tip.position);

        float max = (upper + lower) * maxStretch;
        float min = Mathf.Abs(upper - lower) + minReach;

        Vector3 toTarget = rawPos - root.position;
        float dist = Mathf.Clamp(toTarget.magnitude, min, max);

        Vector3 dir = toTarget.sqrMagnitude > 0.000001f
            ? toTarget.normalized
            : root.forward;

        ikTarget.SetPositionAndRotation(root.position + dir * dist, rot);
    }
}