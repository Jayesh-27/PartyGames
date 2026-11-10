using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Locomotion;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The player on this headset. It
/// - moves the camera rig to your goal (or the lobby spot when not connected),
/// - recenters you when you click a thumbstick or recenter the headset,
/// - turns off teleport/turn locomotion in this scene so keepers stay in their goal,
/// - leaves the party when the Menu button is held for a second,
/// - tracks your hands, head and body so the ball can tell when you touch it,
/// - plays controller vibration.
/// </summary>
[DefaultExecutionOrder(40)]
public class KeeperLocalPlayer : MonoBehaviour
{
    public struct Probe
    {
        public Vector3 prev, cur, velocity;
        public float radius;
        public int hand;    // 0 left, 1 right, -1 head/body
        public bool valid;
    }

    public static KeeperLocalPlayer Instance { get; private set; }

    [Header("Scene")]
    public OVRCameraRig rig;
    public Canvas lobbyCanvas;
    public Vector3 lobbySpot = Vector3.zero;
    public Vector3 lobbyForward = Vector3.forward;

    [Header("Touch sizes (metres)")]
    public float handRadius = 0.09f;
    public float headRadius = 0.12f;
    public float bodyRadius = 0.17f;
    public bool bodyCounts = true;

    [Header("Thumbstick movement")]
    [Tooltip("Left stick walks you around, right stick turns")]
    public bool allowMove = true;
    [Tooltip("Walking speed, metres per second")]
    public float moveSpeed = 2.5f;
    [Tooltip("Right stick: snap turn by this many degrees (0 = smooth turn)")]
    public float snapTurnDegrees = 45f;
    public float smoothTurnSpeed = 120f;
    [Tooltip("During a match you can walk this far out from your goal line")]
    public float maxDistanceFromGoal = 4.5f;
    [Tooltip("How far behind your goal line you can step (into the goal mouth)")]
    public float behindGoalLine = 0.3f;

    [Header("Options")]
    [Tooltip("Turns off the Meta teleport/turn locomotion; this script's thumbstick movement replaces it")]
    public bool disableLocomotion = true;

    public List<Probe> Probes => probes;

    readonly List<Probe> probes = new List<Probe>();
    readonly List<GameObject> lobbyRayTargets = new List<GameObject>();
    int placedSide = int.MinValue;
    bool placeRequested;
    bool jumpedThisFrame;
    bool recenterHooked;
    float stopLeftAt, stopRightAt;
    bool turnLatched;
    float menuHeld;

    void Awake()
    {
        Instance = this;
        for (int i = 0; i < 7; i++) probes.Add(new Probe());
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (recenterHooked && OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentered;
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.Touch);
    }

    void Start()
    {
        if (rig == null) rig = FindFirstObjectByType<OVRCameraRig>();
        if (lobbyCanvas != null)
            foreach (RayInteractable ri in lobbyCanvas.GetComponentsInChildren<RayInteractable>(true))
                lobbyRayTargets.Add(ri.gameObject);
        if (disableLocomotion) DisableLocomotion();
    }

    static void DisableLocomotion()
    {
        foreach (var c in FindObjectsByType<PlayerLocomotor>(FindObjectsInactive.Include, FindObjectsSortMode.None)) c.enabled = false;
        foreach (var c in FindObjectsByType<FirstPersonLocomotor>(FindObjectsInactive.Include, FindObjectsSortMode.None)) c.enabled = false;
        foreach (var c in FindObjectsByType<TeleportInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None)) c.enabled = false;
        foreach (var c in FindObjectsByType<LocomotionTurnerInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None)) c.enabled = false;
    }

    void OnRecentered() => placeRequested = true;

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
        if (PartyConnection.InSession && OVRInput.Get(OVRInput.Button.Start))
        {
            menuHeld += Time.unscaledDeltaTime;
            if (menuHeld >= 1f && PartyConnection.Instance != null) { menuHeld = 0f; PartyConnection.Instance.Leave(); }
        }
        else menuHeld = 0f;
    }

    void LateUpdate()
    {
        if (rig == null) rig = FindFirstObjectByType<OVRCameraRig>();

        int wanted = WantedSide();
        if (wanted != placedSide || placeRequested)
        {
            if (TryPlace(wanted))
            {
                placedSide = wanted;
                placeRequested = false;
            }
        }

        if (allowMove) Move(Time.deltaTime);

        UpdateLobbyCanvas();
        UpdateProbes(Time.deltaTime);
        jumpedThisFrame = false;
    }

    static bool InSession => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    int WantedSide()
    {
        if (!InSession) return -1;
        KeeperMatch m = KeeperMatch.Instance;
        return m != null && m.IsSpawned ? m.LocalSide : -1;
    }

    /// <summary>Turns and moves the rig so your head is over the spot, facing the right way. Your floor stays put.</summary>
    bool TryPlace(int side)
    {
        if (rig == null || rig.centerEyeAnchor == null) return false;

        KeeperArenaSpec a = KeeperArena.Spec;
        Vector3 spot = side >= 0 ? a.KeeperSpot(side) : lobbySpot;
        Vector3 forward = side >= 0 ? a.Forward(side) : lobbyForward;

        Transform root = rig.transform;
        Transform head = rig.centerEyeAnchor;

        Vector3 headForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (headForward.sqrMagnitude < 1e-4f) headForward = Vector3.ProjectOnPlane(root.forward, Vector3.up);
        float turn = Vector3.SignedAngle(headForward, forward, Vector3.up);
        root.RotateAround(head.position, Vector3.up, turn);

        Vector3 move = spot - head.position;
        move.y = 0f;
        root.position += move;
        root.position = new Vector3(root.position.x, spot.y, root.position.z);

        jumpedThisFrame = true;
        return true;
    }

    // ------------------------------------------------------------------ thumbstick movement

    void Move(float dt)
    {
        if (rig == null || rig.centerEyeAnchor == null) return;
        Transform root = rig.transform;
        Transform head = rig.centerEyeAnchor;

        // right stick: turn around your head
        float turnX = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).x;
        if (snapTurnDegrees > 0f)
        {
            if (!turnLatched && Mathf.Abs(turnX) > 0.75f)
            {
                turnLatched = true;
                root.RotateAround(head.position, Vector3.up, Mathf.Sign(turnX) * snapTurnDegrees);
                jumpedThisFrame = true;
            }
            else if (Mathf.Abs(turnX) < 0.35f) turnLatched = false;
        }
        else if (Mathf.Abs(turnX) > 0.2f)
            root.RotateAround(head.position, Vector3.up, turnX * smoothTurnSpeed * dt);

        // left stick: walk where you look (flat)
        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
        if (stick.sqrMagnitude > 0.04f)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(root.forward, Vector3.up);
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 move = (fwd * stick.y + right * stick.x) * moveSpeed * dt;
            root.position += new Vector3(move.x, 0f, move.z);
        }

        // keep your head inside the area you're allowed in (also catches walking in real life)
        Vector3 h = head.position;
        Vector3 clamped = ClampHead(h);
        if ((clamped - h).sqrMagnitude > 1e-8f) root.position += new Vector3(clamped.x - h.x, 0f, clamped.z - h.z);
    }

    Vector3 ClampHead(Vector3 h)
    {
        KeeperArenaSpec a = KeeperArena.Spec;
        float margin = 0.3f;
        h.x = Mathf.Clamp(h.x, -a.HalfWidth + margin, a.HalfWidth - margin);

        if (placedSide == 0 || placedSide == 1)
        {
            float sign = KeeperArenaSpec.Sign(placedSide);
            float depth = h.z * sign; // distance toward your own end
            float minDepth = a.HalfLength - Mathf.Max(0.5f, maxDistanceFromGoal);
            float maxDepth = a.HalfLength + Mathf.Clamp(behindGoalLine, 0f, a.goalDepth - margin);
            depth = Mathf.Clamp(depth, minDepth, maxDepth);
            if (depth > a.HalfLength - margin) h.x = Mathf.Clamp(h.x, -a.goalWidth * 0.5f + margin, a.goalWidth * 0.5f - margin);
            h.z = depth * sign;
        }
        else h.z = Mathf.Clamp(h.z, -a.HalfLength + margin, a.HalfLength - margin);
        return h;
    }

    void UpdateLobbyCanvas()
    {
        if (lobbyCanvas == null) return;
        bool show = !InSession;
        if (lobbyCanvas.enabled == show) return;
        lobbyCanvas.enabled = show;
        foreach (GameObject go in lobbyRayTargets)
            if (go != null) go.SetActive(show);
    }

    // ------------------------------------------------------------------ touch probes

    void UpdateProbes(float dt)
    {
        Transform head = rig != null ? rig.centerEyeAnchor : null;
        AvatarHands avatar = AvatarHands.Local;

        bool leftOk = OVRInput.GetControllerPositionTracked(OVRInput.Controller.LTouch) || OVRInput.GetControllerPositionTracked(OVRInput.Controller.LHand);
        bool rightOk = OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch) || OVRInput.GetControllerPositionTracked(OVRInput.Controller.RHand);
#if UNITY_EDITOR
        // the headset emulator and XR simulator don't always report tracking; trust the anchors in the editor
        leftOk = rightOk = true;
#endif

        SetFrom(0, rig != null ? rig.leftHandAnchor : null, handRadius, 0, dt, leftOk);
        SetFrom(1, rig != null ? rig.rightHandAnchor : null, handRadius, 1, dt, rightOk);
        SetFrom(2, avatar != null ? avatar.Get(false) : null, handRadius, 0, dt, leftOk);
        SetFrom(3, avatar != null ? avatar.Get(true) : null, handRadius, 1, dt, rightOk);
        SetFrom(4, head, headRadius, -1, dt, true);

        bool body = head != null && bodyCounts;
        Vector3 h = head != null ? head.position : Vector3.zero;
        Set(5, h + Vector3.down * 0.38f, bodyRadius, -1, dt, body);
        Set(6, h + Vector3.down * 0.72f, bodyRadius, -1, dt, body);
    }

    void SetFrom(int i, Transform t, float radius, int hand, float dt, bool ok)
    {
        if (t == null) Set(i, Vector3.zero, radius, hand, dt, false);
        else Set(i, t.position, radius, hand, dt, ok);
    }

    void Set(int i, Vector3 pos, float radius, int hand, float dt, bool valid)
    {
        Probe p = probes[i];
        if (!valid)
        {
            p.valid = false;
            probes[i] = p;
            return;
        }

        if (!p.valid || jumpedThisFrame)
        {
            // no fake swipe after appearing or after the rig jumped
            p.prev = pos;
            p.cur = pos;
            p.velocity = Vector3.zero;
        }
        else
        {
            p.prev = p.cur;
            p.cur = pos;
            Vector3 v = dt > 1e-5f ? (p.cur - p.prev) / dt : Vector3.zero;
            p.velocity = Vector3.Lerp(p.velocity, v, 0.6f);
        }
        p.radius = radius;
        p.hand = hand;
        p.valid = true;
        probes[i] = p;
    }

    // ------------------------------------------------------------------ haptics

    /// <summary>Vibrates the controller of that hand (0 left, 1 right, anything else both).</summary>
    public void Haptic(int hand, float amplitude, float seconds)
    {
        float until = Time.unscaledTime + seconds;
        if (hand != 1) { OVRInput.SetControllerVibration(0.6f, amplitude, OVRInput.Controller.LTouch); stopLeftAt = until; }
        if (hand != 0) { OVRInput.SetControllerVibration(0.6f, amplitude, OVRInput.Controller.RTouch); stopRightAt = until; }
    }
}
