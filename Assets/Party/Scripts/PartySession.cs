using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum PartyPhase : byte
{
    Lobby = 0,        // in the lobby, nothing played yet
    Loading = 1,      // a mini-game scene is loading
    Intro = 2,        // mini-game name and rules
    Round = 3,        // the mini-game is running a round
    RoundResult = 4,  // points of the round
    GameResult = 5,   // points of the whole mini-game
    Final = 6         // back in the lobby with the party results
}

[Serializable]
public class PartyGame
{
    public string title;
    public string scene;
    [TextArea] public string rules;
}

/// <summary>Points and mini-game order. Plain code so it can be tested without Unity running.</summary>
public static class PartyScoring
{
    /// <summary>Adds the round-win bonus. winner: 0 Blue, 1 Red, 2 tie.</summary>
    public static void Round(int rawBlue, int rawRed, int winBonus, out int blue, out int red, out int winner)
    {
        blue = rawBlue;
        red = rawRed;
        if (rawBlue > rawRed) { winner = 0; blue += winBonus; }
        else if (rawRed > rawBlue) { winner = 1; red += winBonus; }
        else { winner = 2; blue += winBonus / 2; red += winBonus / 2; }
    }

    /// <summary>Every mini-game once, in random order.</summary>
    public static byte[] Shuffle(int count, System.Random rng)
    {
        byte[] q = new byte[count];
        for (int i = 0; i < count; i++) q[i] = (byte)i;
        for (int i = count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (q[i], q[j]) = (q[j], q[i]);
        }
        return q;
    }
}

/// <summary>
/// Runs the party on the host: who is Blue and Red, which mini-game is next, rounds, points and results.
/// Every headset gets a copy of the state. Lives on the NetworkManager object, so it survives scene changes.
///
/// A mini-game scene has one controller that
///   - starts a round whenever <see cref="RoundSerial"/> changes while <see cref="Phase"/> is Round, and
///   - calls <see cref="ReportRound"/> on the host when that round is over.
/// </summary>
[DefaultExecutionOrder(-60)]
public class PartySession : MonoBehaviour
{
    public const ulong NoClient = ulong.MaxValue;
    const string Message = "PartyState";
    const int MaxGames = 16;

    public static PartySession Instance { get; private set; }

    [Header("Mini-games (each is played once per party, in random order)")]
    public List<PartyGame> games = new List<PartyGame>
    {
        new PartyGame { title = "KEEPER CLASH", scene = "KeeperClash", rules = "Guard your goal. Hit the ball back.\nFirst to 5 goals takes the round." },
        new PartyGame { title = "WHACK-A-BUNNY", scene = "WhackBunny", rules = "Smash the bunnies with your hammer.\nGolden bunnies are worth triple." }
    };
    public string lobbyScene = "Lobby";

    [Header("Rules")]
    public int roundsPerGame = 2;
    [Tooltip("Extra points for winning a round (split when the round is tied)")]
    public int roundWinBonus = 50;

    [Header("Keeper Clash")]
    public int keeperGoalsPerRound = 5;
    public int keeperPointsPerGoal = 10;

    [Header("Whack-a-Bunny")]
    public float whackTurnSeconds = 15f;
    public int whackPointsPerBunny = 5;
    public int whackPointsPerGolden = 15;

    [Header("Timing (seconds)")]
    public float introTime = 6f;
    public float roundResultTime = 5f;
    public float gameResultTime = 7f;

    [Header("Testing")]
    [Tooltip("-1 = random order. Otherwise always start with this mini-game (index in the list above)")]
    public int forceFirstGame = -1;
    public bool start;

    // ------------------------------------------------------------------ state (same on every headset)
    PartyPhase phase;
    double phaseEnd;
    byte[] queue = new byte[0];
    int queuePos;
    int round;
    int roundSerial;
    ulong blueId = NoClient, redId = NoClient;
    bool vsBot;
    int firstSide;
    readonly int[] total = new int[2];
    readonly int[] gamePoints = new int[2];
    readonly int[] roundPoints = new int[2];
    readonly int[] wins = new int[2];
    int roundWinner = -1;
    int partyWinner = -1;

    bool dirty;
    string pendingScene;
    bool sceneHooked;
    NetworkManager hookedTo;

    public event Action Changed;

    public PartyPhase Phase => phase;
    public int Round => round;
    public int RoundSerial => roundSerial;
    public bool VsBot => vsBot;
    /// <summary>The side that goes first in turn-based mini-games; picked at random for each mini-game.</summary>
    public int FirstSide => firstSide;
    public int Total(int side) => total[side & 1];
    public int GamePoints(int side) => gamePoints[side & 1];
    public int RoundPoints(int side) => roundPoints[side & 1];
    public int Wins(int side) => wins[side & 1];
    /// <summary>0 Blue, 1 Red, 2 tie, -1 none yet.</summary>
    public int RoundWinner => roundWinner;
    public int PartyWinner => partyWinner;
    public int GamesPlayed => Mathf.Min(queuePos, queue.Length);
    public int GameCount => queue.Length;
    public int GameNumber => Mathf.Min(queuePos + 1, Mathf.Max(1, queue.Length));

    public PartyGame CurrentGame
    {
        get
        {
            if (queuePos < 0 || queuePos >= queue.Length) return null;
            int i = queue[queuePos];
            return i < games.Count ? games[i] : null;
        }
    }

    NetworkManager Net => NetworkManager.Singleton;
    public bool Online => Net != null && Net.IsListening;
    public bool IsServer => Online && Net.IsServer;
    public float TimeLeft => Online ? Mathf.Max(0f, (float)(phaseEnd - Net.ServerTime.Time)) : 0f;

    public ulong ClientOf(int side) => side == 0 ? blueId : side == 1 ? redId : NoClient;
    public bool HasOpponent => redId != NoClient;
    public bool IsBotSide(int side) => side == 1 && vsBot;

    public int SideOf(ulong clientId)
    {
        if (clientId == NoClient) return -1;
        if (clientId == blueId) return 0;
        if (clientId == redId) return 1;
        return -1;
    }

    /// <summary>The side of the player on this headset, or -1.</summary>
    public int LocalSide => Online ? SideOf(Net.LocalClientId) : -1;
    public bool IsLocalHumanSide(int side) => Online && side >= 0 && ClientOf(side) == Net.LocalClientId;
    /// <summary>True on the headset that plays this side: its human, or the host for the bot.</summary>
    public bool IsLocallyControlled(int side) => IsBotSide(side) ? IsServer : IsLocalHumanSide(side);

    public bool CanStart => IsServer && (phase == PartyPhase.Lobby || phase == PartyPhase.Final) && games.Count > 0 && pendingScene == null;

    // ------------------------------------------------------------------ lifecycle

    void Awake() => Instance = this;

    void OnDestroy()
    {
        Unhook();
        if (Instance == this) Instance = null;
    }

    void Hook()
    {
        NetworkManager nm = Net;
        if (nm == null || hookedTo == nm) return;
        Unhook();
        hookedTo = nm;
        nm.OnServerStarted += OnServerStarted;
        nm.OnClientStarted += OnClientStarted;
        nm.OnServerStopped += OnStopped;
        nm.OnClientStopped += OnStopped;
        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;
    }

    void Unhook()
    {
        if (hookedTo == null) return;
        hookedTo.OnServerStarted -= OnServerStarted;
        hookedTo.OnClientStarted -= OnClientStarted;
        hookedTo.OnServerStopped -= OnStopped;
        hookedTo.OnClientStopped -= OnStopped;
        hookedTo.OnClientConnectedCallback -= OnClientConnected;
        hookedTo.OnClientDisconnectCallback -= OnClientDisconnected;
        hookedTo = null;
    }

    void ResetState()
    {
        phase = PartyPhase.Lobby;
        phaseEnd = 0;
        queue = new byte[0];
        queuePos = 0;
        round = 0;
        blueId = redId = NoClient;
        vsBot = false;
        firstSide = 0;
        roundWinner = partyWinner = -1;
        pendingScene = null;
        for (int i = 0; i < 2; i++) total[i] = gamePoints[i] = roundPoints[i] = wins[i] = 0;
        Changed?.Invoke();
    }

    void OnServerStarted()
    {
        ResetState();
        blueId = Net.IsHost ? Net.LocalClientId : NoClient;
        if (Net.CustomMessagingManager != null) Net.CustomMessagingManager.RegisterNamedMessageHandler(Message, OnMessage);
        if (Net.SceneManager != null && !sceneHooked)
        {
            sceneHooked = true;
            Net.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
        }
        dirty = true;
    }

    void OnClientStarted()
    {
        if (Net.IsServer) return;
        ResetState();
        if (Net.CustomMessagingManager != null) Net.CustomMessagingManager.RegisterNamedMessageHandler(Message, OnMessage);
    }

    void OnStopped(bool wasHost)
    {
        sceneHooked = false;
        ResetState();
    }

    void OnClientConnected(ulong id)
    {
        if (!Net.IsServer) return;
        if (id != blueId && redId == NoClient && (phase == PartyPhase.Lobby || phase == PartyPhase.Final))
        {
            redId = id;
            vsBot = false;
        }
        dirty = true;
    }

    void OnClientDisconnected(ulong id)
    {
        if (Net == null || !Net.IsServer) return;
        if (id != redId) return;
        redId = NoClient;
        // the opponent left: stop the party and go back to the lobby
        if (phase != PartyPhase.Lobby && phase != PartyPhase.Final)
        {
            phase = PartyPhase.Lobby;
            LoadScene(lobbyScene);
        }
        dirty = true;
    }

    // ------------------------------------------------------------------ host: flow

    /// <summary>Host: start a party from the lobby. With nobody else in the room, the bot plays Red.</summary>
    public void StartParty()
    {
        if (!CanStart) return;
        vsBot = redId == NoClient;
        queue = PartyScoring.Shuffle(Mathf.Min(games.Count, MaxGames), new System.Random());
        if (forceFirstGame >= 0 && forceFirstGame < queue.Length)
        {
            int at = Array.IndexOf(queue, (byte)forceFirstGame);
            (queue[0], queue[at]) = (queue[at], queue[0]);
        }
        queuePos = 0;
        partyWinner = -1;
        total[0] = total[1] = 0;
        LoadGame();
    }

    void LoadGame()
    {
        PartyGame g = CurrentGame;
        if (g == null) { FinishParty(); return; }
        phase = PartyPhase.Loading;
        round = 1;
        roundWinner = -1;
        gamePoints[0] = gamePoints[1] = 0;
        roundPoints[0] = roundPoints[1] = 0;
        firstSide = UnityEngine.Random.Range(0, 2);
        LoadScene(g.scene);
        dirty = true;
    }

    void LoadScene(string scene)
    {
        pendingScene = scene;
        TryLoadPending();
    }

    void TryLoadPending()
    {
        if (pendingScene == null || !IsServer || Net.SceneManager == null) return;
        SceneEventProgressStatus status = Net.SceneManager.LoadScene(pendingScene, LoadSceneMode.Single);
        if (status == SceneEventProgressStatus.Started) pendingScene = null;
        else if (status != SceneEventProgressStatus.SceneEventInProgress)
        {
            Debug.LogError($"[Party] Could not load scene '{pendingScene}': {status}. Is it in Build Settings?");
            pendingScene = null;
            phase = PartyPhase.Lobby;
            dirty = true;
        }
    }

    void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> done, List<ulong> timedOut)
    {
        if (!IsServer) return;
        PartyGame g = CurrentGame;
        if (phase == PartyPhase.Loading && g != null && sceneName == g.scene) Enter(PartyPhase.Intro, introTime);
        dirty = true;
    }

    void Enter(PartyPhase p, float seconds)
    {
        phase = p;
        phaseEnd = Net.ServerTime.Time + seconds;
        dirty = true;
    }

    /// <summary>Host, called by the mini-game: the round is over. Pass the points each side earned, without the win bonus.</summary>
    public void ReportRound(int pointsBlue, int pointsRed)
    {
        if (!IsServer || phase != PartyPhase.Round) return;
        PartyScoring.Round(pointsBlue, pointsRed, roundWinBonus, out int b, out int r, out roundWinner);
        roundPoints[0] = b;
        roundPoints[1] = r;
        gamePoints[0] += b;
        gamePoints[1] += r;
        total[0] += b;
        total[1] += r;
        Enter(PartyPhase.RoundResult, roundResultTime);
    }

    void FinishParty()
    {
        partyWinner = total[0] > total[1] ? 0 : total[1] > total[0] ? 1 : 2;
        if (partyWinner < 2) wins[partyWinner]++;
        phase = PartyPhase.Final;
        LoadScene(lobbyScene);
        dirty = true;
    }

    void Update()
    {
        Hook();
        if (start) { start = false; StartParty(); }
        if (!IsServer) return;

        if (pendingScene != null) TryLoadPending();

        if (phase == PartyPhase.Intro || phase == PartyPhase.RoundResult || phase == PartyPhase.GameResult)
        {
            if (Net.ServerTime.Time >= phaseEnd)
            {
                switch (phase)
                {
                    case PartyPhase.Intro:
                        roundSerial++;
                        Enter(PartyPhase.Round, 0f);
                        break;
                    case PartyPhase.RoundResult:
                        if (round < roundsPerGame)
                        {
                            round++;
                            roundSerial++;
                            Enter(PartyPhase.Round, 0f);
                        }
                        else Enter(PartyPhase.GameResult, gameResultTime);
                        break;
                    case PartyPhase.GameResult:
                        queuePos++;
                        if (queuePos < queue.Length) LoadGame();
                        else FinishParty();
                        break;
                }
            }
        }
    }

    void LateUpdate()
    {
        if (!dirty) return;
        dirty = false;
        if (IsServer) Broadcast();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ state copy to the other headsets

    void Broadcast()
    {
        if (Net.CustomMessagingManager == null) return;
        using (var w = new FastBufferWriter(256, Allocator.Temp))
        {
            w.WriteValueSafe((byte)phase);
            w.WriteValueSafe(phaseEnd);
            w.WriteValueSafe((byte)queue.Length);
            for (int i = 0; i < queue.Length; i++) w.WriteValueSafe(queue[i]);
            w.WriteValueSafe((byte)queuePos);
            w.WriteValueSafe((byte)round);
            w.WriteValueSafe(roundSerial);
            w.WriteValueSafe(blueId);
            w.WriteValueSafe(redId);
            w.WriteValueSafe(vsBot);
            w.WriteValueSafe((byte)firstSide);
            for (int i = 0; i < 2; i++)
            {
                w.WriteValueSafe(total[i]);
                w.WriteValueSafe(gamePoints[i]);
                w.WriteValueSafe(roundPoints[i]);
                w.WriteValueSafe(wins[i]);
            }
            w.WriteValueSafe((sbyte)roundWinner);
            w.WriteValueSafe((sbyte)partyWinner);

            foreach (ulong id in Net.ConnectedClientsIds)
                if (id != Net.LocalClientId)
                    Net.CustomMessagingManager.SendNamedMessage(Message, id, w, NetworkDelivery.ReliableFragmentedSequenced);
        }
    }

    void OnMessage(ulong sender, FastBufferReader r)
    {
        if (Net == null || Net.IsServer) return;
        r.ReadValueSafe(out byte p);
        r.ReadValueSafe(out phaseEnd);
        r.ReadValueSafe(out byte n);
        queue = new byte[Mathf.Min(n, MaxGames)];
        for (int i = 0; i < n; i++)
        {
            r.ReadValueSafe(out byte q);
            if (i < queue.Length) queue[i] = q;
        }
        r.ReadValueSafe(out byte pos);
        r.ReadValueSafe(out byte rnd);
        r.ReadValueSafe(out roundSerial);
        r.ReadValueSafe(out blueId);
        r.ReadValueSafe(out redId);
        r.ReadValueSafe(out vsBot);
        r.ReadValueSafe(out byte first);
        for (int i = 0; i < 2; i++)
        {
            r.ReadValueSafe(out total[i]);
            r.ReadValueSafe(out gamePoints[i]);
            r.ReadValueSafe(out roundPoints[i]);
            r.ReadValueSafe(out wins[i]);
        }
        r.ReadValueSafe(out sbyte rw);
        r.ReadValueSafe(out sbyte pw);
        phase = (PartyPhase)p;
        queuePos = pos;
        round = rnd;
        firstSide = first;
        roundWinner = rw;
        partyWinner = pw;
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ text for scoreboards

    public string SideName(int side) => IsBotSide(side) ? "BOT" : PartyTeam.Name(side);

    /// <summary>"BLUE 120  :  80 RED" with team colours, using the party totals.</summary>
    public string TotalsLine() =>
        $"{PartyTeam.Tag(0, SideName(0) + " " + total[0])}   <color=#8892A8>:</color>   {PartyTeam.Tag(1, total[1] + " " + SideName(1))}";

    /// <summary>Headline for Intro / RoundResult / GameResult, or "" while a round is running.</summary>
    public string Headline()
    {
        PartyGame g = CurrentGame;
        switch (phase)
        {
            case PartyPhase.Loading: return "GET READY";
            case PartyPhase.Intro: return g != null ? g.title : "";
            case PartyPhase.RoundResult:
                return roundWinner == 2 ? $"ROUND {round} TIED" : $"{PartyTeam.Tag(roundWinner, SideName(roundWinner))} WINS ROUND {round}";
            case PartyPhase.GameResult:
                int w = gamePoints[0] > gamePoints[1] ? 0 : gamePoints[1] > gamePoints[0] ? 1 : 2;
                return w == 2 ? "ALL SQUARE" : $"{PartyTeam.Tag(w, SideName(w))} TAKES IT";
        }
        return "";
    }

    /// <summary>Second line for the same phases.</summary>
    public string Detail()
    {
        PartyGame g = CurrentGame;
        switch (phase)
        {
            case PartyPhase.Intro:
                return (g != null ? g.rules : "") + $"\n<color=#FFCC38>Starts in {Mathf.CeilToInt(TimeLeft)}</color>";
            case PartyPhase.RoundResult:
                return $"{PartyTeam.Tag(0, "+" + roundPoints[0])}   points   {PartyTeam.Tag(1, "+" + roundPoints[1])}";
            case PartyPhase.GameResult:
                return $"This game: {PartyTeam.Tag(0, gamePoints[0].ToString())} - {PartyTeam.Tag(1, gamePoints[1].ToString())}\n" +
                       (queuePos + 1 < queue.Length ? "Next game coming up..." : "Back to the lobby...");
        }
        return "";
    }
}
