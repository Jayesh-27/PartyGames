using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The Keeper Clash ball. Whoever keeps the goal the ball is flying at runs its flight on their own headset,
/// so their save registers with no lag. They tell everyone else about touches, backboards and goals, and
/// send position updates 10 times a second. Everyone else plays the same flight forward and smooths corrections.
/// </summary>
[DefaultExecutionOrder(50)]
public class KeeperBall : NetworkBehaviour
{
    public KeeperBallTuning tuning = new KeeperBallTuning();

    [Header("Visuals")]
    public Transform visual;
    public Renderer[] tintRenderers;
    public Renderer glowRenderer;
    public TrailRenderer trail;
    public ParticleSystem heatSparks;
    public Transform shadow;
    public Renderer shadowRenderer;
    public AudioSource hum;
    [Tooltip("Ball takes the colour of the team that touched it last")]
    public bool teamColors = true;
    [Tooltip("Ball colour before anyone has touched it (after a serve)")]
    public Color neutralColor = new Color(0.9f, 0.95f, 1f);
    [Tooltip("Used only when teamColors is off: colour by speed")]
    public Gradient heatColors;

    [Header("Network")]
    public float snapshotInterval = 0.1f;
    public float correctionSpeed = 12f;

    public static KeeperBall Instance { get; private set; }

    public KeeperBallState State => state;
    public int ControlSide => controlSide;
    public float Heat01 => tuning.Heat01(state.speed);
    public bool IsFlying => state.Mode == KeeperBallMode.Flying || state.Mode == KeeperBallMode.Pending;
    public Vector3 ShownPosition => transform.position;

    KeeperBallState state;
    int controlSide = -1;      // side whose keeper runs the ball; -1 = the server (ball parked or dead)
    Vector3 visualOffset;      // hides small corrections
    Vector3 lastCheckPos;
    float snapshotTimer;
    float pendingTime;
    MaterialPropertyBlock mpb;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    KeeperArenaSpec Arena => KeeperArena.Spec;
    KeeperMatch Match => KeeperMatch.Instance;

    void Awake()
    {
        Instance = this;
        mpb = new MaterialPropertyBlock();
        state = KeeperBallSim.Parked(Arena, tuning, 0);
        lastCheckPos = state.pos;
        if (heatColors == null || heatColors.colorKeys.Length < 2) heatColors = DefaultHeatColors();
        if (hum != null && hum.clip == null) hum.clip = KeeperSfx.Hum;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public static Gradient DefaultHeatColors()
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.2f, 0.85f, 1f), 0f),
                new GradientColorKey(new Color(0.85f, 0.95f, 1f), 0.35f),
                new GradientColorKey(new Color(1f, 0.75f, 0.15f), 0.65f),
                new GradientColorKey(new Color(1f, 0.15f, 0.05f), 1f)
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            state = KeeperBallSim.Parked(Arena, tuning, state.seq);
            controlSide = -1;
        }
        else RequestSyncRpc();
    }

    public override void OnNetworkDespawn()
    {
        state = KeeperBallSim.Parked(Arena, tuning, state.seq);
        controlSide = -1;
    }

    /// <summary>True if this headset runs the ball right now.</summary>
    bool IAmAuthority
    {
        get
        {
            if (!IsSpawned) return false;
            if (controlSide < 0 || state.Mode == KeeperBallMode.Parked || state.Mode == KeeperBallMode.Dead) return IsServer;
            return Match != null && Match.IsLocallyControlled(controlSide);
        }
    }

    // ------------------------------------------------------------------ simulation

    void Update()
    {
        if (!IsSpawned) return;
        float dt = Time.deltaTime;

        if (state.Mode == KeeperBallMode.Flying)
        {
            bool auth = IAmAuthority;
            KeeperStepResult res = KeeperBallSim.Step(ref state, dt, Arena, tuning, auth, out int goalSide);

            if (res == KeeperStepResult.Goal)
            {
                if (auth) AuthorityGoal(goalSide);
                else { state.Mode = KeeperBallMode.Pending; pendingTime = 0f; } // wait for the keeper to confirm
            }
            else if (res == KeeperStepResult.Backboard)
            {
                KeeperFx.Bounce(state.pos, true);
                if (auth)
                {
                    controlSide = state.targetSide;
                    SendEvent(KeeperBallEvent.Backboard, state.pos);
                }
            }
            else if (res != KeeperStepResult.None) KeeperFx.Bounce(state.pos, false);

            if (auth && state.Mode == KeeperBallMode.Flying && IAmAuthority)
            {
                snapshotTimer -= dt;
                if (snapshotTimer <= 0f)
                {
                    snapshotTimer = snapshotInterval;
                    SnapshotRpc(state);
                }
            }
        }
        else if (state.Mode == KeeperBallMode.Pending)
        {
            pendingTime += dt;
            if (IAmAuthority) AuthorityGoal(state.pos.z < 0f ? 0 : 1);
        }
    }

    void LateUpdate()
    {
        if (IsSpawned && state.Mode == KeeperBallMode.Flying && controlSide >= 0 && Match != null && Match.IsLocalHumanSide(controlSide))
            CheckLocalTouch();
        lastCheckPos = state.pos;
        UpdateVisuals(Time.deltaTime);
    }

    void CheckLocalTouch()
    {
        KeeperLocalPlayer player = KeeperLocalPlayer.Instance;
        if (player == null) return;

        float r = Arena.ballRadius;
        var probes = player.Probes;
        for (int i = 0; i < probes.Count; i++)
        {
            KeeperLocalPlayer.Probe p = probes[i];
            if (!p.valid) continue;

            if (!KeeperBallSim.SweptTouch(p.prev - lastCheckPos, p.cur - state.pos, r + p.radius, out float t, out Vector3 rel))
                continue;

            Vector3 ballAt = Vector3.Lerp(lastCheckPos, state.pos, t);
            Vector3 toProbe = rel.sqrMagnitude > 1e-8f ? rel.normalized : -Arena.Forward(controlSide);
            Vector3 contact = ballAt + toProbe * r;

            state.pos = ballAt;
            int side = controlSide;
            KeeperBallSim.ApplyTouch(ref state, Arena, tuning, side, -toProbe, p.velocity, Random.value, Random.value);
            controlSide = state.targetSide;
            visualOffset = Vector3.zero;

            SendEvent(KeeperBallEvent.Touch, contact);
            KeeperFx.Hit(contact, Heat01);
            player.Haptic(p.hand, Mathf.Lerp(0.6f, 1f, Heat01), 0.09f);
            return;
        }
    }

    void AuthorityGoal(int goalSide)
    {
        state.Mode = KeeperBallMode.Dead;
        state.seq++;
        SendEvent(KeeperBallEvent.Goal, state.pos);
        KeeperFx.Goal(goalSide);
        if (IsServer && Match != null) Match.ServerGoal(goalSide);
    }

    // ------------------------------------------------------------------ server API

    public void ServerServe(int side)
    {
        if (!IsServer) return;
        state = KeeperBallSim.Serve(Arena, tuning, side, (ushort)(state.seq + 2), Random.value, Random.value, Random.value);
        controlSide = side;
        visualOffset = Vector3.zero;
        ClearTrail();
        BallEventRpc(state, (byte)KeeperBallEvent.Serve, (sbyte)controlSide, state.pos);
        KeeperFx.Serve(state.pos);
    }

    public void ServerPark()
    {
        if (!IsServer) return;
        state = KeeperBallSim.Parked(Arena, tuning, (ushort)(state.seq + 2));
        controlSide = -1;
        visualOffset = Vector3.zero;
        ClearTrail();
        BallEventRpc(state, (byte)KeeperBallEvent.Park, -1, state.pos);
    }

    /// <summary>Server only: the practice bot blocked the ball.</summary>
    public void ServerBotTouch(Vector3 awayNormal, Vector3 swing)
    {
        if (!IsServer || state.Mode != KeeperBallMode.Flying || Match == null || !Match.IsBotSide(controlSide)) return;
        int side = controlSide;
        KeeperBallSim.ApplyTouch(ref state, Arena, tuning, side, awayNormal, swing, Random.value, Random.value);
        controlSide = state.targetSide;
        Vector3 contact = state.pos - awayNormal.normalized * Arena.ballRadius;
        SendEvent(KeeperBallEvent.Touch, contact);
        KeeperFx.Hit(contact, Heat01);
    }

    // ------------------------------------------------------------------ networking

    void SendEvent(KeeperBallEvent e, Vector3 fxPoint)
    {
        if (!IsSpawned) return;
        snapshotTimer = snapshotInterval;
        BallEventRpc(state, (byte)e, (sbyte)controlSide, fxPoint);
    }

    [Rpc(SendTo.NotMe)]
    void BallEventRpc(KeeperBallState s, byte evt, sbyte newControl, Vector3 fxPoint, RpcParams rpc = default)
    {
        KeeperBallEvent e = (KeeperBallEvent)evt;
        ulong sender = rpc.Receive.SenderClientId;
        bool fromServer = sender == NetworkManager.ServerClientId;
        bool reset = e == KeeperBallEvent.Park || e == KeeperBallEvent.Serve;

        if (IsServer && !fromServer)
        {
            // only the keeper who runs the ball may change it, and only during play
            if (reset || Match == null || !Match.InPlay || controlSide < 0 || Match.ClientOf(controlSide) != sender) return;
        }
        if (reset && !fromServer) return;
        if (!reset && KeeperBallSim.SeqOlder(s.seq, state.seq)) return;

        Apply(s, newControl, reset ? 0f : OneWayLatency(sender));

        switch (e)
        {
            case KeeperBallEvent.Touch:
                KeeperFx.Hit(fxPoint, Heat01);
                break;
            case KeeperBallEvent.Backboard:
                KeeperFx.Bounce(fxPoint, true);
                break;
            case KeeperBallEvent.Goal:
                int goalSide = s.pos.z < 0f ? 0 : 1;
                state = s;
                state.Mode = KeeperBallMode.Dead;
                KeeperFx.Goal(goalSide);
                if (IsServer && Match != null) Match.ServerGoal(goalSide);
                break;
            case KeeperBallEvent.Serve:
                visualOffset = Vector3.zero;
                ClearTrail();
                KeeperFx.Serve(s.pos);
                break;
            case KeeperBallEvent.Park:
                visualOffset = Vector3.zero;
                ClearTrail();
                break;
        }
    }

    [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
    void SnapshotRpc(KeeperBallState s, RpcParams rpc = default)
    {
        if (IAmAuthority) return;
        if (state.Mode == KeeperBallMode.Parked || state.Mode == KeeperBallMode.Dead) return;
        if (KeeperBallSim.SeqOlder(s.seq, state.seq)) return;

        ulong sender = rpc.Receive.SenderClientId;
        if (IsServer && (controlSide < 0 || (Match != null && Match.ClientOf(controlSide) != sender && s.seq == state.seq))) return;

        int control = s.seq == state.seq ? controlSide : s.targetSide;
        Apply(s, (sbyte)control, OneWayLatency(sender));
    }

    [Rpc(SendTo.Server)]
    void RequestSyncRpc(RpcParams rpc = default)
    {
        SyncRpc(state, (sbyte)controlSide, RpcTarget.Single(rpc.Receive.SenderClientId, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void SyncRpc(KeeperBallState s, sbyte control, RpcParams rpc)
    {
        if (IsServer) return;
        Apply(s, control, OneWayLatency(NetworkManager.ServerClientId));
        visualOffset = Vector3.zero;
    }

    void Apply(KeeperBallState s, int newControl, float latency)
    {
        Vector3 shownBefore = state.pos + visualOffset;
        state = s;
        controlSide = newControl;

        if (state.Mode == KeeperBallMode.Flying && latency > 0f)
        {
            KeeperStepResult res = KeeperBallSim.Step(ref state, latency, Arena, tuning, false, out _);
            if (res == KeeperStepResult.Goal) { state.Mode = KeeperBallMode.Pending; pendingTime = 0f; }
        }

        visualOffset = shownBefore - state.pos;
        if (visualOffset.sqrMagnitude > 9f) visualOffset = Vector3.zero; // too far: just jump
        lastCheckPos = state.pos;
    }

    float OneWayLatency(ulong sender)
    {
        NetworkManager nm = NetworkManager;
        if (nm == null || nm.NetworkConfig.NetworkTransport == null) return 0f;
        if (IsServer && sender == NetworkManager.ServerClientId) return 0f;

        ulong peer = IsServer ? sender : NetworkManager.ServerClientId;
        float rttMs = nm.NetworkConfig.NetworkTransport.GetCurrentRtt(peer);
        if (!IsServer && sender != NetworkManager.ServerClientId) rttMs *= 2f; // came through the host
        return Mathf.Clamp(rttMs * 0.0005f, 0f, 0.3f);
    }

    // ------------------------------------------------------------------ visuals

    void ClearTrail()
    {
        if (trail != null) trail.Clear();
    }

    void UpdateVisuals(float dt)
    {
        bool show = IsSpawned && state.Mode != KeeperBallMode.Dead;
        if (visual != null && visual.gameObject.activeSelf != show)
        {
            visual.gameObject.SetActive(show);
            ClearTrail();
        }
        if (shadow != null && shadow.gameObject.activeSelf != show) shadow.gameObject.SetActive(show);
        if (!show)
        {
            if (hum != null && hum.isPlaying) hum.Stop();
            SetSparks(0f);
            return;
        }

        visualOffset = Vector3.Lerp(visualOffset, Vector3.zero, 1f - Mathf.Exp(-correctionSpeed * dt));
        Vector3 p = state.pos + visualOffset;
        bool parked = state.Mode == KeeperBallMode.Parked;
        if (parked) p += Vector3.up * (Mathf.Sin(Time.time * 2.2f) * 0.07f);
        transform.position = p;

        float heat = parked ? 0f : Heat01;
        Color c = BallColor(heat);
        if (parked)
        {
            KeeperMatch m = Match;
            float pulse = m != null && m.Phase == KeeperPhase.Countdown ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 6f)) : 0.8f;
            c *= pulse;
            c.a = 1f;
        }

        if (tintRenderers != null)
        {
            foreach (Renderer r in tintRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, c);
                r.SetPropertyBlock(mpb);
            }
        }
        if (glowRenderer != null)
        {
            glowRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, new Color(c.r, c.g, c.b, 0.35f + 0.35f * heat));
            glowRenderer.SetPropertyBlock(mpb);
        }

        if (trail != null)
        {
            trail.emitting = !parked;
            trail.time = Mathf.Lerp(0.2f, 0.45f, heat);
            trail.startColor = new Color(c.r, c.g, c.b, 0.9f);
            trail.endColor = new Color(c.r, c.g, c.b, 0f);
        }

        SetSparks(parked ? 0f : 8f + heat * 70f, c);

        if (shadow != null)
        {
            float height = Mathf.Max(0f, p.y - Arena.ballRadius);
            shadow.position = new Vector3(p.x, 0.012f, p.z);
            shadow.localScale = Vector3.one * (0.55f + height * 0.12f);
            if (shadowRenderer != null)
            {
                shadowRenderer.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, new Color(0f, 0f, 0f, Mathf.Lerp(0.6f, 0.12f, height / Mathf.Max(1f, Arena.height))));
                shadowRenderer.SetPropertyBlock(mpb);
            }
        }

        if (hum != null)
        {
            if (!hum.isPlaying && hum.clip != null) hum.Play();
            hum.pitch = parked ? 0.6f : Mathf.Lerp(0.8f, 2f, heat);
            hum.volume = parked ? 0.12f : 0.3f + 0.45f * heat;
        }

        if (!parked && IsFlying) KeeperFx.SetDanger(state.targetSide, 1f - Mathf.Clamp01(Mathf.Abs(state.pos.z - Arena.GoalLineZ(state.targetSide)) / Arena.length));
        else KeeperFx.SetDanger(-1, 0f);
    }

    /// <summary>
    /// The colour of the team that touched the ball last (white after a serve), getting brighter and hotter
    /// as it speeds up. Turn off teamColors to go back to the speed gradient.
    /// </summary>
    Color BallColor(float heat)
    {
        if (!teamColors || state.lastHitter > 1) return teamColors ? Color.Lerp(neutralColor, Color.white, heat) : heatColors.Evaluate(heat);
        Color team = KeeperFx.TeamColor(state.lastHitter);
        Color c = Color.Lerp(team * 0.85f, Color.Lerp(team, Color.white, 0.35f), heat);
        c.a = 1f;
        return c;
    }

    void SetSparks(float rate, Color c = default)
    {
        if (heatSparks == null) return;
        var emission = heatSparks.emission;
        emission.rateOverTime = rate;
        if (rate > 0f)
        {
            var main = heatSparks.main;
            main.startColor = c;
            if (!heatSparks.isPlaying) heatSparks.Play();
        }
    }
}
