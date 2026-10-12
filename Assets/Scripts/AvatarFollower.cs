using Unity.Netcode;
using UnityEngine;

public class AvatarFollower : NetworkBehaviour
{
    public Transform root;        // under the head, turns left/right only, moves up and down with the head
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;

    [Tooltip("Offset of the root from the point under the head, in the body's own space (x = right, y = up, z = forward)")]
    public Vector3 rootOffset = Vector3.zero;

    [Tooltip("Height of the avatar's eyes above its feet, in metres")]
    public float avatarEyeHeight = 1.6f;

    [Tooltip("Height of the root's pivot above the avatar's feet, in metres (hips = about 0.9, feet = 0)")]
    public float rootHeightAboveFeet = 0.9f;

    [Tooltip("Stops the feet sinking below the floor when you crouch or sit")]
    public bool keepAboveFloor = true;

    public float bodyTurnSpeed = 6f;

    OVRCameraRig rig;

    void LateUpdate()
    {
        if (!IsOwner) return;

        if (rig == null)
        {
            rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig == null) return;
        }

        Transform eye = rig.centerEyeAnchor;

        Quaternion yaw = Quaternion.Euler(0f, eye.eulerAngles.y, 0f);
        Quaternion rootRot = Quaternion.Slerp(root.rotation, yaw, Time.deltaTime * bodyTurnSpeed);

        // Feet follow the head's height, the root sits rootHeightAboveFeet above them
        float feetY = eye.position.y - avatarEyeHeight;
        if (keepAboveFloor)
            feetY = Mathf.Max(feetY, rig.transform.position.y);   // floor (Tracking Origin = Floor Level)

        Vector3 pos = new Vector3(eye.position.x, feetY + rootHeightAboveFeet, eye.position.z);

        root.SetPositionAndRotation(pos + rootRot * rootOffset, rootRot);

        head.SetPositionAndRotation(eye.position, eye.rotation);

        if (OVRInput.IsControllerConnected(OVRInput.Controller.LTouch))
            leftHand.SetPositionAndRotation(rig.leftControllerAnchor.position, rig.leftControllerAnchor.rotation);

        if (OVRInput.IsControllerConnected(OVRInput.Controller.RTouch))
            rightHand.SetPositionAndRotation(rig.rightControllerAnchor.position, rig.rightControllerAnchor.rotation);
    }
}