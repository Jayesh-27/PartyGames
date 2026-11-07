using System.Collections.Generic;
using Oculus.Interaction;
using Unity.Netcode;
using UnityEngine;

// Put this on the root of the Player prefab (next to AvatarFollower). It is the ONLY setup needed.
// 1) It tells other scripts where the avatar's real hands are (AvatarHands.Local).
// 2) Every frame it moves anything the player is holding from the controller to the avatar hand.
[DefaultExecutionOrder(100)] // after Meta moves held objects, before Slingshot (200)
public class AvatarHands : NetworkBehaviour
{
    public static AvatarHands Local { get; private set; }   // the hands of the player on THIS headset

    [SerializeField] private Transform leftHandPoint;    // LEFT_HAND_end
    [SerializeField] private Transform rightHandPoint;   // RIGHT_HAND_end

    private OVRCameraRig rig;
    private readonly List<Grabbable> grabbables = new List<Grabbable>();
    private float nextRefresh;

    public Transform Get(bool right) => right ? rightHandPoint : leftHandPoint;

    private void Reset() => FindBones();

    private void Awake()
    {
        if (leftHandPoint == null || rightHandPoint == null)
            FindBones();
    }

    private void FindBones()
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "LEFT_HAND_end") leftHandPoint = t;
            else if (t.name == "RIGHT_HAND_end") rightHandPoint = t;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner) Local = this;
    }

    public override void OnNetworkDespawn()
    {
        if (Local == this) Local = null;
    }

    private void LateUpdate()
    {
        if (Local != this) return;                       // only the player on this headset

        if (rig == null)
        {
            rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig == null) return;
        }

        // refresh the list a few times a second so spawned stones/slingshots are included
        if (Time.time >= nextRefresh)
        {
            nextRefresh = Time.time + 0.25f;
            grabbables.Clear();
            grabbables.AddRange(FindObjectsByType<Grabbable>(FindObjectsSortMode.None));
        }

        Transform leftC = rig.leftControllerAnchor;
        Transform rightC = rig.rightControllerAnchor;

        foreach (Grabbable g in grabbables)
        {
            if (g == null || g.SelectingPointsCount == 0) continue;

            // which controller holds it: the one closest to the grab point
            Vector3 grabPos = g.GrabPoints.Count > 0 ? g.GrabPoints[0].position : g.transform.position;
            bool isRight = (grabPos - rightC.position).sqrMagnitude < (grabPos - leftC.position).sqrMagnitude;

            Transform hand = Get(isRight);
            if (hand == null) continue;

            // Meta put the object on the controller; move it by the gap to the avatar hand
            g.transform.position += hand.position - (isRight ? rightC : leftC).position;
        }
    }
}