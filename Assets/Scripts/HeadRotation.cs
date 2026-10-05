using UnityEngine;

public class EarHeadSway : MonoBehaviour
{
    [System.Serializable]
    public class EarBone
    {
        public Transform bone;
        public Vector3 defaultRotation;

        [Header("Head Y (turning) - offset from default")]
        public Vector3 forwardOffset;
        public Vector3 backwardOffset;

        [Header("Head X (nodding) - offset from default")]
        public Vector3 pitchMaxOffset;  // head X increasing
        public Vector3 pitchMinOffset;  // head X decreasing

        [Header("Tick if this bone nods the wrong way")]
        public bool invertPitch;

        public static EarBone Make(Vector3 defaultRot, Vector3 yawOffset, Vector3 pitchOffset)
        {
            return new EarBone
            {
                defaultRotation = defaultRot,
                forwardOffset   = yawOffset,
                backwardOffset  = -yawOffset,
                pitchMaxOffset  = pitchOffset,
                pitchMinOffset  = -pitchOffset
            };
        }

        public void Apply(float yawBend, float pitchBend)
        {
            if (bone == null) return;

            float pb = invertPitch ? -pitchBend : pitchBend;

            Vector3 yawOffset = (yawBend >= 0f)
                ? forwardOffset * yawBend
                : backwardOffset * -yawBend;

            Vector3 pitchOffset = (pb >= 0f)
                ? pitchMaxOffset * pb
                : pitchMinOffset * -pb;

            bone.localRotation = Quaternion.Euler(defaultRotation + yawOffset + pitchOffset);
        }
    }

    [Header("References")]
    public Transform head;  // CenterEyeAnchor

    // ---------------- LEFT EAR ----------------
    [Header("LEFT ear (head turns left = ear forward)")]
    public EarBone leftRoot = EarBone.Make(
        new Vector3(354.355377f, 336.544006f, 91.0143585f),
        new Vector3(0f, 15f, 0f),
        new Vector3(10f, 0f, 0f));

    public EarBone leftMiddle = EarBone.Make(
        new Vector3(8.50399685f, 0.368996888f, 4.96000481f),
        new Vector3(45f, 0f, 0f),
        new Vector3(0f, 15f, 0f));

    public EarBone leftEnd = EarBone.Make(
        new Vector3(7.40561295f, 359.694031f, 355.275024f),
        new Vector3(45f, 0f, 0f),
        new Vector3(0f, 10f, 0f));

    // ---------------- RIGHT EAR ----------------
    [Header("RIGHT ear (head turns right = ear forward)")]
    public EarBone rightRoot = EarBone.Make(
        new Vector3(2.04515696f, 14.2192087f, 267.396912f),
        new Vector3(0f, 15f, 0f),   // matches your Inspector (turning works)
        new Vector3(10f, 0f, 0f));

    public EarBone rightMiddle = EarBone.Make(
        new Vector3(357.422455f, 359.943512f, 2.5109756f),
        new Vector3(45f, 0f, 0f),
        new Vector3(0f, 15f, 0f));

    public EarBone rightEnd = EarBone.Make(
        new Vector3(3.72656226f, 0.00446147891f, 0.137083262f),
        new Vector3(45f, 0f, 0f),
        new Vector3(0f, 10f, 0f));

    [Header("Head turn speed (degrees/second)")]
    public float fullBendSpeed = 180f;
    public float deadZone = 10f;

    [Header("Head nod speed (degrees/second)")]
    public float pitchFullBendSpeed = 120f;
    public float pitchDeadZone = 10f;

    [Header("Smoothing")]
    public float bendSpeed = 12f;
    public float returnSpeed = 3f;

    [Header("Your test object (optional)")]
    public Transform temp;

    float lastYaw, lastPitch;
    float yawBend;    // +1 = head turning left, -1 = head turning right
    float pitchBend;  // +1 = max offsets, -1 = min offsets

    void Start()
    {
        lastYaw = head.eulerAngles.y;
        lastPitch = head.eulerAngles.x;
        ApplyAll();
    }

    void Update()
    {
        if (temp != null) temp.localRotation = head.localRotation;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float yaw = head.eulerAngles.y;
        float yawSpeed = Mathf.DeltaAngle(lastYaw, yaw) / dt;
        lastYaw = yaw;
        yawBend = Follow(yawBend, yawSpeed, -1f, fullBendSpeed, deadZone, dt);

        float pitch = head.eulerAngles.x;
        float pitchSpeed = Mathf.DeltaAngle(lastPitch, pitch) / dt;
        lastPitch = pitch;
        pitchBend = Follow(pitchBend, pitchSpeed, +1f, pitchFullBendSpeed, pitchDeadZone, dt);

        ApplyAll();
    }

    float Follow(float current, float headSpeed, float direction, float fullSpeed, float dead, float dt)
    {
        float magnitude = Mathf.Max(0f, Mathf.Abs(headSpeed) - dead);
        float strength = Mathf.Clamp01(magnitude / (fullSpeed - dead));
        float target = direction * Mathf.Sign(headSpeed) * strength;

        float speed = (Mathf.Abs(target) > Mathf.Abs(current)) ? bendSpeed : returnSpeed;
        return Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * dt));
    }

    void ApplyAll()
    {
        leftRoot.Apply(yawBend, pitchBend);
        leftMiddle.Apply(yawBend, pitchBend);
        leftEnd.Apply(yawBend, pitchBend);

        rightRoot.Apply(-yawBend, pitchBend);
        rightMiddle.Apply(-yawBend, pitchBend);
        rightEnd.Apply(-yawBend, pitchBend);
    }
}