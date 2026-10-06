// using UnityEngine;
// using UnityEngine.Animations.Rigging;

// public class ParentToController : MonoBehaviour
// {
//     public enum Hand
//     {
//         Left,
//         Right
//     }

//     [Header("Object to Parent")]
//     public GameObject objectToParent;

//     [Header("Controller")]
//     public Hand hand;

//     [Header("Rig Builder")]
//     public RigBuilder rigBuilder;

//     private void Awake()
//     {
//         if (objectToParent == null)
//         {
//             Debug.LogError("Object To Parent is not assigned.");
//             return;
//         }

//         OVRCameraRig cameraRig = FindObjectOfType<OVRCameraRig>();

//         if (cameraRig == null)
//         {
//             Debug.LogError("OVRCameraRig not found.");
//             return;
//         }

//         if (rigBuilder == null)
//         {
//             rigBuilder = FindObjectOfType<RigBuilder>();

//             if (rigBuilder == null)
//             {
//                 Debug.LogError("RigBuilder not found.");
//                 return;
//             }
//         }

//         // Disable Animation Rigging before changing hierarchy
//         rigBuilder.enabled = false;

//         Transform controllerAnchor;

//         if (hand == Hand.Left)
//         {
//             controllerAnchor = cameraRig.leftControllerAnchor;
//         }
//         else
//         {
//             controllerAnchor = cameraRig.rightControllerAnchor;
//         }

//         if (controllerAnchor == null)
//         {
//             Debug.LogError("Controller anchor not found.");
//             return;
//         }

//         // Parent object to controller
//         objectToParent.transform.SetParent(controllerAnchor);

//         // Match controller position and rotation
//         objectToParent.transform.localPosition = Vector3.zero;
//         objectToParent.transform.localRotation = Quaternion.identity;

//         // Rebuild Animation Rigging
//         rigBuilder.Build();

//         // Enable it again
//         rigBuilder.enabled = true;
//     }
// }