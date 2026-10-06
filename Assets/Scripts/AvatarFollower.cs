using Unity.Netcode;
using UnityEngine;

public class AvatarFollower : NetworkBehaviour
{
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;

    OVRCameraRig rig;

    void LateUpdate()
    {
        if (!IsOwner) return;

        if (rig == null)
        {
            rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig == null) return;
        }

        if (head != null)
            head.SetPositionAndRotation(rig.centerEyeAnchor.position, rig.centerEyeAnchor.rotation);

        if (leftHand != null && OVRInput.IsControllerConnected(OVRInput.Controller.LTouch))
            leftHand.SetPositionAndRotation(rig.leftControllerAnchor.position, rig.leftControllerAnchor.rotation);

        if (rightHand != null && OVRInput.IsControllerConnected(OVRInput.Controller.RTouch))
            rightHand.SetPositionAndRotation(rig.rightControllerAnchor.position, rig.rightControllerAnchor.rotation);
    }
}