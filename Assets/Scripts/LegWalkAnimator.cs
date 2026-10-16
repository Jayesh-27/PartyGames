using UnityEngine;

/// <summary>
/// Procedural VR walking and turn-in-place legs for single-bone legs (LEFT_LEG / RIGHT_LEG).
/// Driven by actual distance walked with a VR sway deadzone and distance-clamped envelope,
/// ensuring tiny movements only produce tiny steps and legs settle cleanly to rest.
/// Runs locally on every client from synced bones, requiring no extra network traffic.
/// </summary>
[DefaultExecutionOrder(500)] // Runs after AvatarFollower, ParentToController, NetworkTransform, and RigBuilder
public class LegWalkAnimator : MonoBehaviour
{
    [Header("Required References (Auto-found if left empty)")]
    [Tooltip("The HEAD bone. Synced via NetworkTransform so remote avatars walk automatically.")]
    public Transform head;
    [Tooltip("Optional BODY root used to detect turn-in-place rotation. Auto-finds 'BODY' if empty.")]
    public Transform bodyRoot;

    [Header("Leg Bones (Auto-found by name if left empty)")]
    public Transform leftLeg;
    public Transform rightLeg;

    [Header("Walk Cycle")]
    [Tooltip("Metres walked for one full cycle (left step + right step). Smaller = quicker steps.")]
    public float strideLength = 0.55f;
    [Tooltip("Maximum leg swing angle at full walking speed (degrees).")]
    public float maxSwingAngle = 32f;
    [Tooltip("Minimum leg swing angle when walking slowly (degrees).")]
    public float minSwingAngle = 6f;
    [Tooltip("Walking speed (m/s) at which leg swing reaches maxSwingAngle.")]
    public float fullSpeedAt = 1.4f;
    [Tooltip("Speed (m/s) below which the avatar is considered stopped.")]
    public float moveThreshold = 0.12f;

    [Header("Tiny-Move & VR Jitter Protection")]
    [Tooltip("Horizontal deadzone (metres). Head movements smaller than this won't trigger steps.")]
    public float stepDeadzone = 0.025f;
    [Tooltip("Distance (metres) you must walk before swing amplitude is allowed to reach 100%.")]
    public float stepBuildUpDistance = 0.18f;
    [Tooltip("Distance from neck pivot to eyes (metres). Stops legs shuffling when looking around.")]
    public float neckOffset = 0.09f;

    [Header("Step Lift & Turn-in-Place")]
    [Tooltip("How high (metres in world space) a foot lifts while swinging forward. 0 disables lift.")]
    public float stepLiftHeight = 0.03f;
    [Tooltip("Enables subtle leg stepping when rotating in place.")]
    public bool stepWhenTurning = true;
    [Tooltip("Angular speed (deg/s) required to trigger turn-in-place stepping.")]
    public float turnSpeedThreshold = 35f;
    [Tooltip("Converts angular turn speed into virtual walk speed for turn steps.")]
    public float turnStepStrength = 0.006f;

    [Header("Smoothing")]
    public float velocitySmoothing = 10f;
    public float headingSmoothing = 10f;
    [Tooltip("How fast legs ease into walking and return to rest when stopping.")]
    public float amplitudeSmoothing = 10f;

    [Header("Safety & Debug")]
    [Tooltip("Movement larger than this in a single frame is treated as a teleport and ignored.")]
    public float teleportDistance = 0.5f;
    [Tooltip("Tick if the legs swing backward instead of forward.")]
    public bool invertSwing;
    [Tooltip("Editor testing: set above 0 to simulate walking forward at this speed (m/s).")]
    public float debugSpeed;

    Quaternion leftRestRot, rightRestRot;
    Vector3 leftRestPos, rightRestPos;

    Vector3 lastNeckPos;
    Vector3 walkAnchorPos;
    Vector3 smoothedVel;
    Vector3 heading = Vector3.forward;
    Vector3 lastFlatForward = Vector3.forward;

    float lastBodyYaw;
    float smoothedTurnSpeed;
    float activeWalkDistance;
    float amplitude;
    float phase;
    bool initialized;
    bool nextStepFlip;

    void Awake()
    {
        AutoFindBones();
        CacheRestPose();
    }

    void OnValidate()
    {
        AutoFindBones();
    }

    void AutoFindBones()
    {
        if (head == null) head = FindDeep(transform, "HEAD");
        if (bodyRoot == null) bodyRoot = FindDeep(transform, "BODY");
        if (leftLeg == null) leftLeg = FindDeep(transform, "LEFT_LEG");
        if (rightLeg == null) rightLeg = FindDeep(transform, "RIGHT_LEG");
    }

    void CacheRestPose()
    {
        if (leftLeg != null)
        {
            leftRestRot = leftLeg.localRotation;
            leftRestPos = leftLeg.localPosition;
        }
        if (rightLeg != null)
        {
            rightRestRot = rightLeg.localRotation;
            rightRestPos = rightLeg.localPosition;
        }
    }

    void LateUpdate()
    {
        if (head == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // 1. Keep a stable horizontal forward direction from the head
        Vector3 flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (flatForward.sqrMagnitude > 0.04f)
            lastFlatForward = flatForward.normalized;

        // 2. Estimate neck position so turning the head in place doesn't translate into walking
        Vector3 neckPos = head.position - lastFlatForward * neckOffset;
        neckPos.y = 0f; // Ignore vertical crouching/standing

        Transform yawRef = bodyRoot != null ? bodyRoot : head;
        float currentYaw = yawRef.eulerAngles.y;

        if (!initialized)
        {
            lastNeckPos = neckPos;
            walkAnchorPos = neckPos;
            heading = lastFlatForward;
            lastBodyYaw = currentYaw;
            initialized = true;
            return;
        }

        // 3. Teleport check
        Vector3 rawFrameDelta = neckPos - lastNeckPos;
        lastNeckPos = neckPos;

        if (rawFrameDelta.magnitude > teleportDistance)
        {
            walkAnchorPos = neckPos;
            smoothedVel = Vector3.zero;
            activeWalkDistance = 0f;
            lastBodyYaw = currentYaw;
            return;
        }

        // 4. Deadzone tether: ignore micro head sway inside stepDeadzone
        Vector3 anchorOffset = neckPos - walkAnchorPos;
        Vector3 effectiveDelta = Vector3.zero;
        float distFromAnchor = anchorOffset.magnitude;

        if (distFromAnchor > stepDeadzone)
        {
            Vector3 prevAnchor = walkAnchorPos;
            walkAnchorPos = neckPos - (anchorOffset / distFromAnchor) * stepDeadzone;
            effectiveDelta = walkAnchorPos - prevAnchor;
        }

        // 5. Calculate linear velocity
        Vector3 rawVel = debugSpeed > 0f ? (lastFlatForward * debugSpeed) : (effectiveDelta / dt);
        float velLerp = 1f - Mathf.Exp(-velocitySmoothing * dt);
        smoothedVel = Vector3.Lerp(smoothedVel, rawVel, velLerp);
        float linearSpeed = smoothedVel.magnitude;

        // 6. Calculate turn-in-place speed
        float yawDelta = Mathf.Abs(Mathf.DeltaAngle(lastBodyYaw, currentYaw));
        lastBodyYaw = currentYaw;
        float rawTurnSpeed = yawDelta / dt;
        smoothedTurnSpeed = Mathf.Lerp(smoothedTurnSpeed, rawTurnSpeed, velLerp);

        bool isWalkingLinear = linearSpeed > moveThreshold;
        bool isTurningInPlace = stepWhenTurning && !isWalkingLinear && smoothedTurnSpeed > turnSpeedThreshold;
        bool isMoving = isWalkingLinear || isTurningInPlace;

        float effectiveSpeed = isWalkingLinear
            ? linearSpeed
            : (isTurningInPlace ? smoothedTurnSpeed * turnStepStrength : 0f);

        // 7. Track distance walked in this burst so tiny moves cannot produce big swings
        if (isMoving)
        {
            float stepDist = isWalkingLinear ? (linearSpeed * dt) : (effectiveSpeed * dt);
            activeWalkDistance = Mathf.Min(
                Mathf.Max(0.01f, stepBuildUpDistance),
                activeWalkDistance + stepDist
            );

            // Update swing heading along travel direction (or facing direction when turning in place)
            Vector3 targetHeading = isWalkingLinear ? (smoothedVel / linearSpeed) : lastFlatForward;
            heading = Vector3.Slerp(heading, targetHeading, 1f - Mathf.Exp(-headingSmoothing * dt));
            if (heading.sqrMagnitude < 0.0001f) heading = targetHeading;
            heading.Normalize();

            // Advance phase strictly by distance covered
            float cycleDist = Mathf.Max(0.05f, strideLength);
            phase += (effectiveSpeed * dt / cycleDist) * Mathf.PI * 2f;
            phase = Mathf.Repeat(phase, Mathf.PI * 2f);
        }
        else
        {
            // Decay the build-up distance when stopped
            float decayRate = Mathf.Max(0.05f, stepBuildUpDistance) * amplitudeSmoothing;
            activeWalkDistance = Mathf.MoveTowards(activeWalkDistance, 0f, decayRate * dt);
        }

        // 8. Compute target amplitude clamped by BOTH speed and distance traveled
        float distanceEnvelope = stepBuildUpDistance > 0.001f
            ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(activeWalkDistance / stepBuildUpDistance))
            : 1f;

        float speedFactor = Mathf.InverseLerp(moveThreshold, Mathf.Max(moveThreshold + 0.01f, fullSpeedAt), effectiveSpeed);
        float rawTargetAmp = isMoving ? Mathf.Lerp(minSwingAngle, maxSwingAngle, speedFactor) : 0f;
        float targetAmp = rawTargetAmp * distanceEnvelope;

        amplitude = Mathf.Lerp(amplitude, targetAmp, 1f - Mathf.Exp(-amplitudeSmoothing * dt));

        // Reset phase to neutral (0 or PI) once legs have settled so the next step always starts from 0 swing
        if (!isMoving && amplitude < 0.25f)
        {
            amplitude = 0f;
            phase = nextStepFlip ? Mathf.PI : 0f;
        }
        else if (isMoving)
        {
            nextStepFlip = (phase < Mathf.PI);
        }

        // 9. Apply swing and subtle foot lift to both legs
        float dirSign = invertSwing ? -1f : 1f;
        float sinPhase = Mathf.Sin(phase);
        float cosPhase = Mathf.Cos(phase);

        float leftSwingDeg = amplitude * sinPhase * dirSign;
        float rightSwingDeg = -leftSwingDeg;

        // Lift the foot slightly while it is actively swinging forward (cosPhase > 0 for left, < 0 for right)
        float ampRatio = maxSwingAngle > 0.01f ? Mathf.Clamp01(amplitude / maxSwingAngle) : 0f;
        float leftLift = isMoving ? Mathf.Max(0f, cosPhase * dirSign) * stepLiftHeight * ampRatio : 0f;
        float rightLift = isMoving ? Mathf.Max(0f, -cosPhase * dirSign) * stepLiftHeight * ampRatio : 0f;

        Vector3 rightAxis = Vector3.Cross(Vector3.up, heading);

        ApplyPose(leftLeg, leftRestPos, leftRestRot, leftSwingDeg, leftLift, rightAxis);
        ApplyPose(rightLeg, rightRestPos, rightRestRot, rightSwingDeg, rightLift, rightAxis);
    }

    static void ApplyPose(Transform leg, Vector3 restLocalPos, Quaternion restLocalRot, float footForwardDeg, float liftWorldMeters, Vector3 rightAxis)
    {
        if (leg == null) return;

        // Reset to rest pose every frame so transforms never drift or accumulate
        leg.localRotation = restLocalRot;
        leg.localPosition = restLocalPos;

        // Swing in world space so bone local export axes don't matter
        if (Mathf.Abs(footForwardDeg) > 0.001f)
            leg.rotation = Quaternion.AngleAxis(-footForwardDeg, rightAxis) * leg.rotation;

        // Lift in world space (automatically safe against Armature 100x scale)
        if (liftWorldMeters > 0.0001f)
            leg.position += Vector3.up * liftWorldMeters;
    }

    static Transform FindDeep(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName) return child;
            Transform found = FindDeep(child, childName);
            if (found != null) return found;
        }
        return null;
    }
}