using UnityEngine;
using Unity.Netcode;

public class NetworkVRPlayer : NetworkBehaviour
{
    [SerializeField] private Transform headPosition;
    [SerializeField] private Transform leftHandPosition;
    [SerializeField] private Transform rightHandPosition;

    private NetworkVariable<Vector3> headNetworkPosition = new NetworkVariable<Vector3>();
    private NetworkVariable<Quaternion> headNetworkRotation = new NetworkVariable<Quaternion>();

    private NetworkVariable<Vector3> leftHandNetworkPosition = new NetworkVariable<Vector3>();
    private NetworkVariable<Quaternion> leftHandNetworkRotation = new NetworkVariable<Quaternion>();

    private NetworkVariable<Vector3> rightHandNetworkPosition = new NetworkVariable<Vector3>();
    private NetworkVariable<Quaternion> rightHandNetworkRotation = new NetworkVariable<Quaternion>();

    // smoothing for remote visuals
    [SerializeField] private float positionLerpRate = 15f;
    [SerializeField] private float rotationLerpRate = 15f;

    // targets for interpolation
    private Vector3 headTargetPos;
    private Quaternion headTargetRot;
    private Vector3 leftTargetPos;
    private Quaternion leftTargetRot;
    private Vector3 rightTargetPos;
    private Quaternion rightTargetRot;

    private void LateUpdate()
    {
        if (!IsSpawned)
            return;

        if (IsOwner)
        {
            // Local owner - read from VRRigReferences singleton (camera rig) and send to server
            if (VRRigReferences.Singleton != null)
            {
                Transform headTransform = VRRigReferences.Singleton.head;
                Transform leftHandTransform = VRRigReferences.Singleton.leftHand;
                Transform rightHandTransform = VRRigReferences.Singleton.rightHand;

                if (headTransform != null || leftHandTransform != null || rightHandTransform != null)
                {
                    Vector3 hp = headTransform != null ? headTransform.position : headNetworkPosition.Value;
                    Quaternion hr = headTransform != null ? headTransform.rotation : headNetworkRotation.Value;

                    Vector3 lp = leftHandTransform != null ? leftHandTransform.position : leftHandNetworkPosition.Value;
                    Quaternion lr = leftHandTransform != null ? leftHandTransform.rotation : leftHandNetworkRotation.Value;

                    Vector3 rp = rightHandTransform != null ? rightHandTransform.position : rightHandNetworkPosition.Value;
                    Quaternion rr = rightHandTransform != null ? rightHandTransform.rotation : rightHandNetworkRotation.Value;

                    // send to server to update authoritative network variables
                    UpdateTransformsServerRpc(hp, hr, lp, lr, rp, rr);
                }
            }
        }
        else
        {
            // Remote client - interpolate towards networked targets
            // update targets from network variables
            headTargetPos = headNetworkPosition.Value;
            headTargetRot = headNetworkRotation.Value;

            leftTargetPos = leftHandNetworkPosition.Value;
            leftTargetRot = leftHandNetworkRotation.Value;

            rightTargetPos = rightHandNetworkPosition.Value;
            rightTargetRot = rightHandNetworkRotation.Value;

            if (headPosition != null)
            {
                headPosition.position = Vector3.Lerp(headPosition.position, headTargetPos, Time.deltaTime * positionLerpRate);
                headPosition.rotation = Quaternion.Slerp(headPosition.rotation, headTargetRot, Time.deltaTime * rotationLerpRate);
            }

            if (leftHandPosition != null)
            {
                leftHandPosition.position = Vector3.Lerp(leftHandPosition.position, leftTargetPos, Time.deltaTime * positionLerpRate);
                leftHandPosition.rotation = Quaternion.Slerp(leftHandPosition.rotation, leftTargetRot, Time.deltaTime * rotationLerpRate);
            }

            if (rightHandPosition != null)
            {
                rightHandPosition.position = Vector3.Lerp(rightHandPosition.position, rightTargetPos, Time.deltaTime * positionLerpRate);
                rightHandPosition.rotation = Quaternion.Slerp(rightHandPosition.rotation, rightTargetRot, Time.deltaTime * rotationLerpRate);
            }
        }
    }

    [ServerRpc]
    private void UpdateTransformsServerRpc(Vector3 headPos, Quaternion headRot, Vector3 leftPos, Quaternion leftRot, Vector3 rightPos, Quaternion rightRot)
    {
        // Server updates network variables (server-authoritative)
        headNetworkPosition.Value = headPos;
        headNetworkRotation.Value = headRot;

        leftHandNetworkPosition.Value = leftPos;
        leftHandNetworkRotation.Value = leftRot;

        rightHandNetworkPosition.Value = rightPos;
        rightHandNetworkRotation.Value = rightRot;
    }
}
