using Unity.Netcode;
using UnityEngine;

public enum KeeperPhase : byte { Waiting = 0, Countdown = 1, Playing = 2, GoalScored = 3, MatchOver = 4 }

/// <summary>
/// Runs one Keeper Clash round on the host: serves, goals and the score. A round is first to
/// <see cref="GoalsToWin"/> goals. The party (PartySession) says who is Blue and Red, whether the bot
/// plays Red, when a round starts, and gets the points when the round is over.
/// </summary>
public class KeeperMatch : NetworkBehaviour
{
    public const ulong NoClient = ulong.MaxValue;
    public static KeeperMatch Instance { get; private set; }

    [Header("Rules (goals per round and points come from PartySession)")]
    public int goalsToWinFallback = 5;
    public float countdownTime = 3f;
    public float countdownAfterGoal = 2f;
    public float goalPauseTime = 2.5f;

    readonly NetworkVariable<byte> phase = new NetworkVariable<byte>((byte)KeeperPhase.Waiting);
    readonly NetworkVariable<int> scoreBlue = new NetworkVariable<int>();
    readonly NetworkVariable<int> scoreRed = new NetworkVariable<int>();
    readonly NetworkVariable<double> phaseEnd = new NetworkVariable<double>();
    readonly NetworkVariable<sbyte> lastScorer = new NetworkVariable<sbyte>(-1);
    readonly NetworkVariable<sbyte> winner = new NetworkVariable<sbyte>(-1);

    int nextServe;
    int handledSerial = -1;

    static PartySession Party => PartySession.Instance;

    public KeeperPhase Phase => (KeeperPhase)phase.Value;
    public bool InPlay => IsSpawned && Phase == KeeperPhase.Playing;
    public int ScoreBlue => scoreBlue.Value;
    public int ScoreRed => scoreRed.Value;
    public bool BotActive => Party != null && Party.VsBot;
    public int LastScorer => lastScorer.Value;
    public int Winner => winner.Value;
    public bool HasOpponent => Party != null && Party.HasOpponent;
    public int GoalsToWin => Party != null ? Mathf.Max(1, Party.keeperGoalsPerRound) : goalsToWinFallback;

    public float TimeLeft => IsSpawned ? Mathf.Max(0f, (float)(phaseEnd.Value - NetworkManager.ServerTime.Time)) : 0f;

    /// <summary>The side the player on this headset keeps, or -1 (not connected / watching).</summary>
    public int LocalSide => IsSpawned && Party != null ? Party.LocalSide : -1;

    public ulong ClientOf(int side) => Party != null ? Party.ClientOf(side) : NoClient;
    public int SideOf(ulong clientId) => Party != null ? Party.SideOf(clientId) : -1;
    public bool IsBotSide(int side) => Party != null && Party.IsBotSide(side);

    /// <summary>True if a human on this headset keeps this side.</summary>
    public bool IsLocalHumanSide(int side) => IsSpawned && Party != null && Party.IsLocalHumanSide(side);

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
        if (Party != null && Party.Phase != PartyPhase.Round) handledSerial = Party.RoundSerial;
        phase.Value = (byte)KeeperPhase.Waiting;
    }

    void StartRound()
    {
        scoreBlue.Value = 0;
        scoreRed.Value = 0;
        winner.Value = -1;
        lastScorer.Value = -1;
        nextServe = Random.Range(0, 2);
        if (KeeperBall.Instance != null) KeeperBall.Instance.ServerPark();
        Enter(KeeperPhase.Countdown, countdownTime);
    }

    void Enter(KeeperPhase p, float duration)
    {
        phase.Value = (byte)p;
        phaseEnd.Value = NetworkManager.ServerTime.Time + duration;
    }

    void Update()
    {
        if (!IsSpawned || !IsServer) return;

        PartySession party = Party;
        bool roundOn = party != null && party.Phase == PartyPhase.Round;
        if (roundOn && party.RoundSerial != handledSerial)
        {
            handledSerial = party.RoundSerial;
            StartRound();
            return;
        }
        if (!roundOn)
        {
            // the party stopped the round (someone left): stop the ball
            if (Phase == KeeperPhase.Countdown || Phase == KeeperPhase.Playing || Phase == KeeperPhase.GoalScored)
            {
                if (KeeperBall.Instance != null) KeeperBall.Instance.ServerPark();
                Enter(KeeperPhase.Waiting, 0f);
            }
            return;
        }

        if (Phase == KeeperPhase.Waiting || Phase == KeeperPhase.Playing || Phase == KeeperPhase.MatchOver) return;
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
        if (score >= GoalsToWin)
        {
            winner.Value = (sbyte)scorer;
            Enter(KeeperPhase.MatchOver, 0f);
            PartySession party = Party;
            if (party != null) party.ReportRound(scoreBlue.Value * party.keeperPointsPerGoal, scoreRed.Value * party.keeperPointsPerGoal);
        }
        else Enter(KeeperPhase.GoalScored, goalPauseTime);
    }

    [Rpc(SendTo.Everyone)]
    void GoalRpc(byte goalSide)
    {
        KeeperFx.Goal(goalSide);
    }
}
