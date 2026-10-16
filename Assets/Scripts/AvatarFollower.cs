using Unity.Netcode;
using UnityEngine;

public class AvatarFollower : NetworkBehaviour
{
    [Header("Avatar References")]
    public Transform root;        // Body root: follows head height and turns horizontally
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;

    [Header("Body Position")]
    [Tooltip("Offset of the root in the body's own space (x = right, y = up, z = forward)")]
    public Vector3 rootOffset = Vector3.zero;

    [Tooltip("Height of the avatar's eyes above its feet, in metres")]
    public float avatarEyeHeight = 1.6f;

    [Tooltip("Height of the root's pivot above the avatar's feet, in metres")]
    public float rootHeightAboveFeet = 0.9f;

    [Tooltip("Stops the feet sinking below the floor when you crouch or sit")]
    public bool keepAboveFloor = true;

    [Header("Head / Body Turning")]
    [Tooltip("The body stays still while the head is within this angle of the body")]
    [Range(0f, 170f)]
    public float bodyTurnStartAngle = 60f;

    [Tooltip("When the head exceeds this angle, the body catches up toward it")]
    [Range(1f, 179f)]
    public float bodyCatchUpAngle = 80f;

    [Tooltip("Once catching up, the body stops this many degrees away from the head direction")]
    [Range(0f, 30f)]
    public float headBodyAlignmentTolerance = 5f;

    [Tooltip("How smoothly the body follows between the start angle and catch-up angle")]
    [Min(0f)]
    public float bodyTurnSpeed = 6f;

    [Tooltip("How quickly the body catches up after the head exceeds the catch-up angle")]
    [Min(0f)]
    public float bodyCatchUpSpeed = 12f;

    [Tooltip("Overall strength of automatic body turning. 0 disables it; 1 is the normal effect.")]
    [Range(0f, 2f)]
    public float bodyTurnEffectStrength = 1f;

    private OVRCameraRig rig;
    private bool strongTurnActive;

    private void LateUpdate()
    {
        if (!IsOwner || root == null)
            return;

        if (rig == null)
        {
            rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig == null || rig.centerEyeAnchor == null)
                return;
        }

        Transform eye = rig.centerEyeAnchor;
        float headYaw = eye.eulerAngles.y;
        float currentBodyYaw = root.eulerAngles.y;
        float headBodyAngle = Mathf.DeltaAngle(currentBodyYaw, headYaw);
        float absHeadBodyAngle = Mathf.Abs(headBodyAngle);

        // Enter catch-up mode once the head turns beyond the larger threshold.
        if (!strongTurnActive && absHeadBodyAngle >= Mathf.Max(bodyCatchUpAngle, bodyTurnStartAngle + 1f))
            strongTurnActive = true;

        // Leave catch-up mode once the body is facing almost the same direction as the head.
        if (strongTurnActive && absHeadBodyAngle <= headBodyAlignmentTolerance)
            strongTurnActive = false;

        float targetBodyYaw = currentBodyYaw;
        float turnSpeed = 0f;

        if (strongTurnActive)
        {
            // Keep a small head/body angle while the body catches up.
            targetBodyYaw = headYaw - Mathf.Sign(headBodyAngle) * headBodyAlignmentTolerance;
            turnSpeed = bodyCatchUpSpeed;
        }
        else if (absHeadBodyAngle > bodyTurnStartAngle)
        {
            // In the transition zone, move the body gently to keep the head near the start angle.
            targetBodyYaw = headYaw - Mathf.Sign(headBodyAngle) * bodyTurnStartAngle;
            turnSpeed = bodyTurnSpeed;
        }

        // Exponential smoothing gives stable, frame-rate-independent turning.
        float turnFactor = 1f - Mathf.Exp(-turnSpeed * bodyTurnEffectStrength * Time.deltaTime);
        float nextBodyYaw = Mathf.LerpAngle(currentBodyYaw, targetBodyYaw, turnFactor);
        Quaternion bodyRotation = Quaternion.Euler(0f, nextBodyYaw, 0f);

        // Keep the body positioned below the head while respecting the floor height.
        float feetY = eye.position.y - avatarEyeHeight;
        if (keepAboveFloor)
            feetY = Mathf.Max(feetY, rig.transform.position.y); // Tracking Origin = Floor Level

        Vector3 bodyPosition = new Vector3(eye.position.x, feetY + rootHeightAboveFeet, eye.position.z);
        root.SetPositionAndRotation(bodyPosition + bodyRotation * rootOffset, bodyRotation);

        if (head != null)
            head.SetPositionAndRotation(eye.position, eye.rotation);

        if (leftHand != null && OVRInput.IsControllerConnected(OVRInput.Controller.LTouch))
            leftHand.SetPositionAndRotation(rig.leftControllerAnchor.position, rig.leftControllerAnchor.rotation);

        if (rightHand != null && OVRInput.IsControllerConnected(OVRInput.Controller.RTouch))
            rightHand.SetPositionAndRotation(rig.rightControllerAnchor.position, rig.rightControllerAnchor.rotation);
    }
}