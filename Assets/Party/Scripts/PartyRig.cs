using Oculus.Interaction.Locomotion;
using UnityEngine;

/// <summary>
/// The player on this headset, for scenes where you stand in one spot (lobby, Whack-a-Bunny).
/// Puts your head over a spot facing a direction, recenters on a thumbstick click or a headset recenter,
/// turns off teleport/turn locomotion, vibrates controllers, and leaves the party when Menu is held.
/// </summary>
[DefaultExecutionOrder(30)]
public class PartyRig : MonoBehaviour
{
    public static PartyRig Instance { get; private set; }

    public OVRCameraRig rig;
    public Vector3 homeSpot = Vector3.zero;
    public Vector3 homeForward = Vector3.forward;
    [Tooltip("Turns off the Meta teleport/turn locomotion in this scene")]
    public bool disableLocomotion = true;
    [Tooltip("Hold the Menu button this long to leave the party (0 = off)")]
    public float holdMenuToLeave = 1f;

    /// <summary>True on the frame the rig was moved; hand speeds are meaningless on that frame.</summary>
    public bool JumpedThisFrame { get; private set; }

    bool placeRequested = true;
    bool recenterHooked;
    float stopLeftAt, stopRightAt, menuHeld;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (recenterHooked && OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentered;
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.Touch);
    }

    void Start()
    {
        if (rig == null) rig = FindFirstObjectByType<OVRCameraRig>();
        if (!disableLocomotion) return;
        foreach (var c in FindObjectsByType<PlayerLocomotor>(FindObjectsInactive.Include, FindObjectsSortMode.None)) c.enabled = false;
        foreach (var c in FindObjectsByType<FirstPersonLocomotor>(FindObjectsInactive.Include, FindObjectsSortMode.None)) c.enabled = false;
        foreach (var c in FindObjectsByType<TeleportInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None)) c.enabled = false;
        foreach (var c in FindObjectsByType<LocomotionTurnerInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None)) c.enabled = false;
    }

    void OnRecentered() => placeRequested = true;

    /// <summary>Where your head should be and which way you face. Moves you there when it changes.</summary>
    public void SetHome(Vector3 spot, Vector3 forward)
    {
        if ((spot - homeSpot).sqrMagnitude < 1e-6f && Vector3.Dot(forward.normalized, homeForward.normalized) > 0.9999f) return;
        homeSpot = spot;
        homeForward = forward;
        placeRequested = true;
    }

    void Update()
    {
        if (!recenterHooked && OVRManager.display != null)
        {
            OVRManager.display.RecenteredPose += OnRecentered;
            recenterHooked = true;
        }

        if (stopLeftAt > 0f && Time.unscaledTime >= stopLeftAt) { OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch); stopLeftAt = 0f; }
        if (stopRightAt > 0f && Time.unscaledTime >= stopRightAt) { OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch); stopRightAt = 0f; }

        if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick) || OVRInput.GetDown(OVRInput.Button.SecondaryThumbstick))
            placeRequested = true;

        if (holdMenuToLeave > 0f && PartyConnection.InSession && OVRInput.Get(OVRInput.Button.Start))
        {
            menuHeld += Time.unscaledDeltaTime;
            if (menuHeld >= holdMenuToLeave && PartyConnection.Instance != null)
            {
                menuHeld = 0f;
                PartyConnection.Instance.Leave();
            }
        }
        else menuHeld = 0f;
    }

    void LateUpdate()
    {
        JumpedThisFrame = false;
        if (rig == null) rig = FindFirstObjectByType<OVRCameraRig>();
        if (placeRequested && Place(homeSpot, homeForward)) placeRequested = false;
    }

    /// <summary>Turns and moves the rig so your head is over the spot, facing forward. Your real floor stays the floor.</summary>
    public bool Place(Vector3 spot, Vector3 forward)
    {
        if (rig == null || rig.centerEyeAnchor == null) return false;
        Transform root = rig.transform;
        Transform head = rig.centerEyeAnchor;

        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        Vector3 headForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (headForward.sqrMagnitude < 1e-4f) headForward = Vector3.ProjectOnPlane(root.forward, Vector3.up);
        if (forward.sqrMagnitude > 1e-4f)
            root.RotateAround(head.position, Vector3.up, Vector3.SignedAngle(headForward, forward, Vector3.up));

        Vector3 move = spot - head.position;
        root.position += new Vector3(move.x, 0f, move.z);
        root.position = new Vector3(root.position.x, spot.y, root.position.z);
        JumpedThisFrame = true;
        return true;
    }

    /// <summary>Vibrates the controller of that hand (0 left, 1 right, anything else both).</summary>
    public void Haptic(int hand, float amplitude, float seconds)
    {
        float until = Time.unscaledTime + seconds;
        if (hand != 1) { OVRInput.SetControllerVibration(0.6f, amplitude, OVRInput.Controller.LTouch); stopLeftAt = until; }
        if (hand != 0) { OVRInput.SetControllerVibration(0.6f, amplitude, OVRInput.Controller.RTouch); stopRightAt = until; }
    }
}
