using UnityEngine;

/// <summary>
/// One bunny in one hole. WhackField tells it how far out it should be; it does the springy rise,
/// the arm wave, the golden tint and the squash when it gets bonked.
/// </summary>
public class WhackBunny : MonoBehaviour
{
    [Header("Set by the builder")]
    public Transform model;              // the imported bunny, child of this object (this object sits on the table top, in the hole)
    public Renderer[] renderers;
    public Transform leftArm, rightArm;  // upper arm bones
    public Transform leftHand, rightHand;
    [Tooltip("Model height in metres at scale 1")]
    public float height = 0.24f;

    [Header("Pose (metres, relative to the table top)")]
    [Tooltip("Feet position when hidden in the burrow")]
    public float hiddenY = -0.14f;
    [Tooltip("Feet position when fully up")]
    public float upY = -0.07f;
    [Tooltip("Squashed height when hidden, so it fits in the burrow")]
    [Range(0.2f, 1f)] public float hiddenSquash = 0.5f;

    [Header("Arms")]
    public float armRaise = 105f;
    public float armWave = 28f;
    public float waveSpeed = 15f;

    public Color goldenTint = new Color(1.6f, 1.25f, 0.35f);

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int BaseColorFactorId = Shader.PropertyToID("baseColorFactor");

    float shown, velocity;       // 0 hidden .. 1 up, with a spring
    float target;
    float bonkTime = -10f;
    bool golden, appliedGolden, visible = true;
    Quaternion leftRest, rightRest;
    float leftSign = 1f, rightSign = -1f;
    float phaseOffset;
    MaterialPropertyBlock mpb;

    /// <summary>True when the hammer can hit it.</summary>
    public bool Hittable => shown > 0.4f && Time.time - bonkTime > 0.5f && target > 0.01f;

    /// <summary>The middle of the head, for the hit test.</summary>
    public Vector3 HeadPoint => transform.position + Vector3.up * (Mathf.Lerp(hiddenY, upY, shown) + height * 0.72f);

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        phaseOffset = Random.value * 10f;
        if (leftArm != null) leftRest = leftArm.localRotation;
        if (rightArm != null) rightRest = rightArm.localRotation;
        leftSign = RaiseSign(leftArm, leftHand);
        rightSign = RaiseSign(rightArm, rightHand);
        Apply(0f, 0f);
        SetVisible(false);
    }

    // which way to turn the arm around the bunny's forward axis so the hand goes up
    float RaiseSign(Transform arm, Transform hand)
    {
        if (arm == null || hand == null) return 1f;
        Vector3 d = hand.position - arm.position;
        Vector3 r = Quaternion.AngleAxis(20f, transform.forward) * d;
        return r.y > d.y ? 1f : -1f;
    }

    public void Face(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(direction);
    }

    /// <summary>How far out it should be (0..1) and whether it is a golden one.</summary>
    public void SetTarget(float out01, bool isGolden)
    {
        target = Mathf.Clamp01(out01);
        if (target > 0.01f) golden = isGolden;
    }

    public void Bonk()
    {
        bonkTime = Time.time;
        velocity = -6f;
    }

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        float sinceBonk = Time.time - bonkTime;
        float goal = sinceBonk < 0.45f ? 0f : target;

        // spring toward the goal, with a little overshoot
        float accel = (goal - shown) * 420f - velocity * 26f;
        velocity += accel * dt;
        shown = Mathf.Clamp(shown + velocity * dt, -0.05f, 1.25f);

        bool show = shown > 0.02f || goal > 0.02f;
        if (show != visible) SetVisible(show);
        if (!show) return;

        float squash = sinceBonk < 0.22f ? Mathf.Sin(sinceBonk / 0.22f * Mathf.PI) : 0f;
        Apply(shown, squash);

        if (golden != appliedGolden) Tint();
    }

    void Apply(float s, float squash)
    {
        if (model == null) return;
        float k = Mathf.Clamp01(s);
        float stretch = Mathf.Lerp(hiddenSquash, 1f, k) + Mathf.Max(0f, s - 1f) * 0.6f;
        float y = stretch * (1f - 0.6f * squash);
        float xz = 1f + 0.45f * squash + (1f - Mathf.Min(1f, stretch)) * 0.15f;
        model.localPosition = new Vector3(0f, Mathf.LerpUnclamped(hiddenY, upY, s), 0f);
        model.localScale = new Vector3(xz, y, xz);
        // a little side-to-side wiggle while it is up
        model.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin((Time.time + phaseOffset) * 11f) * 7f * k * (1f - squash));
    }

    void LateUpdate()
    {
        if (!visible) return;
        float up = Mathf.Clamp01((shown - 0.35f) / 0.5f);
        float wave = Mathf.Sin((Time.time + phaseOffset) * waveSpeed) * armWave;
        float angle = up * (armRaise + wave);
        if (leftArm != null)
        {
            leftArm.localRotation = leftRest;
            leftArm.rotation = Quaternion.AngleAxis(angle * leftSign, transform.forward) * leftArm.rotation;
        }
        if (rightArm != null)
        {
            rightArm.localRotation = rightRest;
            rightArm.rotation = Quaternion.AngleAxis((up * (armRaise - wave)) * rightSign, transform.forward) * rightArm.rotation;
        }
    }

    void SetVisible(bool on)
    {
        visible = on;
        if (renderers == null) return;
        foreach (Renderer r in renderers)
            if (r != null) r.enabled = on;
    }

    void Tint()
    {
        appliedGolden = golden;
        if (renderers == null) return;
        Color c = golden ? goldenTint : Color.white;
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c);
            mpb.SetColor(BaseColorFactorId, c);
            r.SetPropertyBlock(mpb);
        }
    }
}
