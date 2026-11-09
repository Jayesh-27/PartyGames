using Unity.Netcode;
using UnityEngine;

public enum KeeperPhase : byte { Waiting = 0, Countdown = 1, Playing = 2, GoalScored = 3, MatchOver = 4 }

/// <summary>
/// Runs the 1v1 match on the server: who keeps which goal, the score, serves, goals and rematches.
/// The host keeps the Blue goal, the first player who joins keeps Red. While the host is alone a
/// practice bot keeps the Red goal.
/// </summary>
public class KeeperMatch : NetworkBehaviour
{
    public const ulong NoClient = ulong.MaxValue;
    public static KeeperMatch Instance { get; private set; }

    [Header("Rules")]
    public int goalsToWin = 7;
    public float countdownTime = 3f;
    public float countdownAfterGoal = 2f;
    public float goalPauseTime = 2.5f;
    public float matchOverTime = 6f;
    public bool practiceBotWhenAlone = true;

    readonly NetworkVariable<byte> phase = new NetworkVariable<byte>((byte)KeeperPhase.Waiting);
    readonly NetworkVariable<int> scoreBlue = new NetworkVariable<int>();
    readonly NetworkVariable<int> scoreRed = new NetworkVariable<int>();
    readonly NetworkVariable<ulong> blueClient = new NetworkVariable<ulong>(NoClient);
    readonly NetworkVariable<ulong> redClient = new NetworkVariable<ulong>(NoClient);
    readonly NetworkVariable<bool> botActive = new NetworkVariable<bool>();
    readonly NetworkVariable<double> phaseEnd = new NetworkVariable<double>();
    readonly NetworkVariable<sbyte> lastScorer = new NetworkVariable<sbyte>(-1);
    readonly NetworkVariable<sbyte> winner = new NetworkVariable<sbyte>(-1);

    int nextServe;

    public KeeperPhase Phase => (KeeperPhase)phase.Value;
    public bool InPlay => IsSpawned && Phase == KeeperPhase.Playing;
    public int ScoreBlue => scoreBlue.Value;
    public int ScoreRed => scoreRed.Value;
    public bool BotActive => botActive.Value;
    public int LastScorer => lastScorer.Value;
    public int Winner => winner.Value;
    public bool HasOpponent => redClient.Value != NoClient;

    public float TimeLeft => IsSpawned ? Mathf.Max(0f, (float)(phaseEnd.Value - NetworkManager.ServerTime.Time)) : 0f;

    /// <summary>The side the player on this headset keeps, or -1 (not connected / watching).</summary>
    public int LocalSide => IsSpawned ? SideOf(NetworkManager.LocalClientId) : -1;

    public ulong ClientOf(int side) => side == 0 ? blueClient.Value : side == 1 ? redClient.Value : NoClient;

    public int SideOf(ulong clientId)
    {
        if (clientId == NoClient) return -1;
        if (clientId == blueClient.Value) return 0;
        if (clientId == redClient.Value) return 1;
        return -1;
    }

    public bool IsBotSide(int side) => side == 1 && botActive.Value;

    /// <summary>True if a human on this headset keeps this side.</summary>
    public bool IsLocalHumanSide(int side) => IsSpawned && side >= 0 && ClientOf(side) == NetworkManager.LocalClientId;

    /// <summary>True if this headset runs the ball while it flies at this side (its human keeper, or the server for the bot).</summary>
    public bool IsLocallyControlled(int side) => IsBotSide(side) ? IsServer : IsLocalHumanSide(side);

    void Awake() => Instance = this;

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (!IsServer) return;

        NetworkManager.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

        blueClient.Value = NetworkManager.IsHost ? NetworkManager.LocalClientId : NoClient;
        redClient.Value = NoClient;
        foreach (ulong id in NetworkManager.ConnectedClientsIds)
        {
            if (id == blueClient.Value) continue;
            if (blueClient.Value == NoClient) blueClient.Value = id;
            else { redClient.Value = id; break; }
        }
        RefreshBot();
        StartMatch();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    void OnClientConnected(ulong id)
    {
        if (id == NetworkManager.LocalClientId || SideOf(id) >= 0) return;

        if (blueClient.Value == NoClient) blueClient.Value = id;
        else if (redClient.Value == NoClient) redClient.Value = id;
        else return; // a third player just watches

        RefreshBot();
        StartMatch();
        Debug.Log($"[KeeperClash] Player {id} joined. Blue={blueClient.Value} Red={redClient.Value}");
    }

    void OnClientDisconnected(ulong id)
    {
        int side = SideOf(id);
        if (side < 0) return;

        if (side == 0) blueClient.Value = NoClient;
        else redClient.Value = NoClient;

        RefreshBot();
        StartMatch();
        Debug.Log($"[KeeperClash] Player {id} left.");
    }

    void RefreshBot() => botActive.Value = practiceBotWhenAlone && redClient.Value == NoClient && blueClient.Value != NoClient;

    void StartMatch()
    {
        scoreBlue.Value = 0;
        scoreRed.Value = 0;
        winner.Value = -1;
        lastScorer.Value = -1;
        nextServe = Random.Range(0, 2);

        if (KeeperBall.Instance != null) KeeperBall.Instance.ServerPark();

        bool canPlay = blueClient.Value != NoClient && (redClient.Value != NoClient || botActive.Value);
        Enter(canPlay ? KeeperPhase.Countdown : KeeperPhase.Waiting, countdownTime);
    }

    void Enter(KeeperPhase p, float duration)
    {
        phase.Value = (byte)p;
        phaseEnd.Value = NetworkManager.ServerTime.Time + duration;
    }

    void Update()
    {
        if (!IsSpawned || !IsServer) return;
        if (Phase == KeeperPhase.Waiting || Phase == KeeperPhase.Playing) return;
        if (NetworkManager.ServerTime.Time < phaseEnd.Value) return;

        switch (Phase)
        {
            case KeeperPhase.Countdown:
                Serve();
                break;
            case KeeperPhase.GoalScored:
                if (KeeperBall.Instance != null) KeeperBall.Instance.ServerPark();
                Enter(KeeperPhase.Countdown, countdownAfterGoal);
                break;
            case KeeperPhase.MatchOver:
                StartMatch();
                break;
        }
    }

    void Serve()
    {
        KeeperBall ball = KeeperBall.Instance;
        if (ball == null) return;
        int side = nextServe;
        nextServe = 1 - nextServe;
        Enter(KeeperPhase.Playing, 0f);
        ball.ServerServe(side);
    }

    /// <summary>Server only: the ball went into the goal of goalSide.</summary>
    public void ServerGoal(int goalSide)
    {
        if (!IsServer || !InPlay) return;

        int scorer = 1 - goalSide;
        if (scorer == 0) scoreBlue.Value++;
        else scoreRed.Value++;
        lastScorer.Value = (sbyte)scorer;

        GoalRpc((byte)goalSide);

        int score = scorer == 0 ? scoreBlue.Value : scoreRed.Value;
        if (score >= goalsToWin)
        {
            winner.Value = (sbyte)scorer;
            Enter(KeeperPhase.MatchOver, matchOverTime);
        }
        else Enter(KeeperPhase.GoalScored, goalPauseTime);
    }

    [Rpc(SendTo.Everyone)]
    void GoalRpc(byte goalSide)
    {
        KeeperFx.Goal(goalSide);
    }
}
