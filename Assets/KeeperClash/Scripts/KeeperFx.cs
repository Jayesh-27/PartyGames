using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Visual and sound feedback: hit sparks, goal explosion with a shockwave, the team's neon flashing,
/// a post-processing flash, the warning glow on the goal under attack, countdown beeps and haptics.
/// Static helpers so the ball and match can call it without references.
/// </summary>
public class KeeperFx : MonoBehaviour
{
    public static KeeperFx Instance { get; private set; }

    [Header("Particles")]
    public ParticleSystem hitSparks;
    public ParticleSystem goalBurst;
    public Transform shockwave;
    public Renderer shockwaveRenderer;

    [Header("Team neon (0 = Blue, 1 = Red)")]
    public Renderer[] blueNeon;
    public Renderer[] redNeon;
    public Renderer[] blueGoalFrame;
    public Renderer[] redGoalFrame;
    public Color blueColor = new Color(0.1f, 0.75f, 1f);
    public Color redColor = new Color(1f, 0.18f, 0.35f);

    [Header("Post-processing flash")]
    public Volume goalFlashVolume;

    [Header("Audio")]
    public AudioSource uiAudio;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    MaterialPropertyBlock mpb;
    float flashTime = -10f;
    int flashSide = -1;
    float lastGoalTime = -10f;
    float shockTime = -10f;
    Vector3 shockPos;
    int dangerSide = -1;
    float dangerAmount;
    int lastBeep = -1;
    KeeperPhase lastPhase;

    public static Color TeamColor(int side) =>
        Instance != null ? (side == 0 ? Instance.blueColor : Instance.redColor) : (side == 0 ? new Color(0.1f, 0.75f, 1f) : new Color(1f, 0.18f, 0.35f));

    void Awake()
    {
        Instance = this;
        mpb = new MaterialPropertyBlock();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ static entry points

    public static void Hit(Vector3 point, float heat)
    {
        if (Instance == null) return;
        Instance.Burst(Instance.hitSparks, point, 25 + Mathf.RoundToInt(heat * 40f), KeeperBall.DefaultHeatColors().Evaluate(heat));
        KeeperSfx.PlayAt(KeeperSfx.Hit, point, 0.9f, 0.9f + heat * 0.5f);
    }

    public static void Bounce(Vector3 point, bool backboard)
    {
        if (Instance == null) return;
        if (backboard) Instance.Burst(Instance.hitSparks, point, 18, Color.white);
        KeeperSfx.PlayAt(KeeperSfx.Bounce, point, backboard ? 0.9f : 0.5f, backboard ? 0.7f : 1f);
    }

    public static void Serve(Vector3 point)
    {
        if (Instance == null) return;
        KeeperSfx.PlayAt(KeeperSfx.Hit, point, 0.6f, 0.7f);
    }

    /// <summary>The ball went into goalSide's goal. Safe to call more than once for the same goal.</summary>
    public static void Goal(int goalSide)
    {
        if (Instance == null) return;
        KeeperFx f = Instance;
        if (Time.time - f.lastGoalTime < 1.5f) return;
        f.lastGoalTime = Time.time;

        int scorer = 1 - goalSide;
        KeeperArenaSpec a = KeeperArena.Spec;
        Vector3 at = a.GoalCenter(goalSide) + new Vector3(0f, 0f, KeeperArenaSpec.Sign(goalSide) * 0.4f);
        f.Burst(f.goalBurst, at, 220, TeamColor(scorer));
        f.shockTime = Time.time;
        f.shockPos = at;
        f.flashTime = Time.time;
        f.flashSide = scorer;
        KeeperSfx.Play2D(KeeperSfx.Goal, 1f);

        KeeperMatch m = KeeperMatch.Instance;
        if (m != null && KeeperLocalPlayer.Instance != null && m.LocalSide >= 0)
            KeeperLocalPlayer.Instance.Haptic(-1, m.LocalSide == goalSide ? 0.9f : 0.4f, m.LocalSide == goalSide ? 0.35f : 0.15f);
    }

    /// <summary>Glow the frame of the goal the ball is flying at (amount 0 to 1).</summary>
    public static void SetDanger(int side, float amount)
    {
        if (Instance == null) return;
        Instance.dangerSide = side;
        Instance.dangerAmount = amount;
    }

    // ------------------------------------------------------------------ per frame

    void Burst(ParticleSystem ps, Vector3 point, int count, Color color)
    {
        if (ps == null) return;
        ps.transform.position = point;
        var main = ps.main;
        main.startColor = color;
        ps.Emit(count);
    }

    void Update()
    {
        float t = Time.time;

        // team neon flash after a goal
        float flash = Mathf.Clamp01(1f - (t - flashTime) / 1.2f);
        float strobe = flash > 0f ? 0.5f + 0.5f * Mathf.Sign(Mathf.Sin(t * 28f)) : 0f;
        Tint(blueNeon, blueColor, flashSide == 0 ? flash * strobe : 0f, 0f);
        Tint(redNeon, redColor, flashSide == 1 ? flash * strobe : 0f, 0f);

        // goal frames: warning glow when the ball is coming, flash on goals
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * (8f + dangerAmount * 14f));
        Tint(blueGoalFrame, blueColor, flashSide == 0 ? flash * strobe : 0f, dangerSide == 0 ? dangerAmount * pulse : 0f);
        Tint(redGoalFrame, redColor, flashSide == 1 ? flash * strobe : 0f, dangerSide == 1 ? dangerAmount * pulse : 0f);

        // post-processing flash
        if (goalFlashVolume != null)
        {
            float w = Mathf.Clamp01(1f - (t - flashTime) / 0.9f);
            goalFlashVolume.weight = w * w;
        }

        // shockwave sphere
        if (shockwave != null)
        {
            float k = (t - shockTime) / 0.7f;
            bool on = k >= 0f && k < 1f;
            if (shockwave.gameObject.activeSelf != on) shockwave.gameObject.SetActive(on);
            if (on)
            {
                shockwave.position = shockPos;
                shockwave.localScale = Vector3.one * Mathf.Lerp(0.5f, 9f, 1f - (1f - k) * (1f - k));
                if (shockwaveRenderer != null)
                {
                    Color c = TeamColor(flashSide);
                    c.a = (1f - k) * 0.6f;
                    shockwaveRenderer.GetPropertyBlock(mpb);
                    mpb.SetColor(BaseColorId, c);
                    shockwaveRenderer.SetPropertyBlock(mpb);
                }
            }
        }

        CountdownSounds();
    }

    void Tint(Renderer[] rs, Color baseColor, float flash, float danger)
    {
        if (rs == null) return;
        Color c = Color.Lerp(baseColor * (0.85f + danger * 0.15f), Color.white, Mathf.Max(flash, danger * 0.65f));
        c.a = 1f;
        foreach (Renderer r in rs)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(mpb);
        }
    }

    void CountdownSounds()
    {
        KeeperMatch m = KeeperMatch.Instance;
        if (m == null || !m.IsSpawned) { lastBeep = -1; return; }

        if (m.Phase == KeeperPhase.Countdown)
        {
            int sec = Mathf.CeilToInt(m.TimeLeft);
            if (sec != lastBeep && sec > 0 && sec <= 3)
            {
                lastBeep = sec;
                KeeperSfx.Play2D(KeeperSfx.Beep, 0.6f);
            }
        }
        else lastBeep = -1;

        if (m.Phase == KeeperPhase.Playing && lastPhase == KeeperPhase.Countdown) KeeperSfx.Play2D(KeeperSfx.Go, 0.7f);
        lastPhase = m.Phase;
    }
}
