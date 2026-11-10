using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum WhackPhase : byte { Waiting = 0, Ready = 1, Turn = 2, TurnOver = 3, RoundOver = 4 }

/// <summary>
/// Runs one Whack-a-Bunny round on the host: one side's turn, then the other side's turn, same order every
/// round (the party picks who goes first, once per game). The pops of a turn come from a seed, so every headset
/// works out the same bunnies. The player whose turn it is detects their own hits (no lag) and the host
/// checks and counts them.
/// </summary>
public class WhackMatch : NetworkBehaviour
{
    public static WhackMatch Instance { get; private set; }

    public WhackTuning tuning = new WhackTuning();

    [Header("Timing (turn length and points come from PartySession)")]
    public float turnSecondsFallback = 15f;
    public float readyTime = 4f;
    public float turnOverTime = 2.2f;
    [Tooltip("The host still accepts a hit this long after the bunny dropped (network delay)")]
    public float hitSlack = 0.35f;

    readonly NetworkVariable<byte> phase = new NetworkVariable<byte>((byte)WhackPhase.Waiting);
    readonly NetworkVariable<sbyte> activeSide = new NetworkVariable<sbyte>(-1);
    readonly NetworkVariable<double> turnStart = new NetworkVariable<double>();
    readonly NetworkVariable<double> phaseEnd = new NetworkVariable<double>();
    readonly NetworkVariable<int> seed = new NetworkVariable<int>();
    readonly NetworkVariable<int> scoreBlue = new NetworkVariable<int>();
    readonly NetworkVariable<int> scoreRed = new NetworkVariable<int>();
    readonly NetworkVariable<byte> leftHandMask = new NetworkVariable<byte>();

    List<WhackPop> pops = new List<WhackPop>();
    bool[] hit = new bool[0];
    int popsSeed = int.MinValue;
    int handledSerial = -1;
    int turnIndex;
    int firstSide;

    /// <summary>A bunny was hit: (pop, side that hit it). Raised once per pop on every headset.</summary>
    public event Action<WhackPop, int> Hit;

    static PartySession Party => PartySession.Instance;

    public WhackPhase Phase => (WhackPhase)phase.Value;
    public int ActiveSide => activeSide.Value;
    public int Score(int side) => side == 0 ? scoreBlue.Value : scoreRed.Value;
    public float TurnSeconds => Party != null ? Mathf.Max(3f, Party.whackTurnSeconds) : turnSecondsFallback;
    double Now => NetworkManager.ServerTime.Time;
    /// <summary>Seconds since the turn started.</summary>
    public float TurnTime => IsSpawned ? (float)(Now - turnStart.Value) : 0f;
    public float TurnTimeLeft => Mathf.Clamp(TurnSeconds - TurnTime, 0f, TurnSeconds);
    public float PhaseTimeLeft => IsSpawned ? Mathf.Max(0f, (float)(phaseEnd.Value - Now)) : 0f;
    public int TurnSeed => seed.Value;
    public bool TurnRunning => IsSpawned && Phase == WhackPhase.Turn;
    public bool LeftHanded(int side) => (leftHandMask.Value & (1 << side)) != 0;

    public List<WhackPop> Pops
    {
        get
        {
            if (popsSeed != seed.Value)
            {
                popsSeed = seed.Value;
                pops = WhackBunnySim.Schedule(popsSeed, TurnSeconds, tuning);
                hit = new bool[pops.Count];
            }
            return pops;
        }
    }

    public bool IsHit(int popId) => popId >= 0 && popId < hit.Length && hit[popId];

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
        phase.Value = (byte)WhackPhase.Waiting;
        activeSide.Value = -1;
    }

    // ------------------------------------------------------------------ host: flow

    void Enter(WhackPhase p, float seconds)
    {
        phase.Value = (byte)p;
        phaseEnd.Value = Now + seconds;
    }

    void Update()
    {
        if (!IsSpawned || !IsServer) return;

        PartySession party = Party;
        bool roundOn = party != null && party.Phase == PartyPhase.Round;
        if (roundOn && party.RoundSerial != handledSerial)
        {
            handledSerial = party.RoundSerial;
            scoreBlue.Value = 0;
            scoreRed.Value = 0;
            firstSide = party.FirstSide & 1;
            turnIndex = 0;
            activeSide.Value = (sbyte)firstSide;
            Enter(WhackPhase.Ready, readyTime);
            return;
        }
        if (!roundOn)
        {
            if (Phase == WhackPhase.Ready || Phase == WhackPhase.Turn || Phase == WhackPhase.TurnOver)
            {
                activeSide.Value = -1;
                Enter(WhackPhase.Waiting, 0f);
            }
            return;
        }

        switch (Phase)
        {
            case WhackPhase.Ready:
                if (Now < phaseEnd.Value) break;
                seed.Value = UnityEngine.Random.Range(1, int.MaxValue);
                turnStart.Value = Now;
                Enter(WhackPhase.Turn, TurnSeconds);
                break;

            case WhackPhase.Turn:
                if (Now < turnStart.Value + TurnSeconds) break;
                Enter(WhackPhase.TurnOver, turnOverTime);
                break;

            case WhackPhase.TurnOver:
                if (Now < phaseEnd.Value) break;
                if (turnIndex == 0)
                {
                    turnIndex = 1;
                    activeSide.Value = (sbyte)(1 - firstSide);
                    Enter(WhackPhase.Ready, readyTime);
                }
                else
                {
                    activeSide.Value = -1;
                    Enter(WhackPhase.RoundOver, 0f);
                    party.ReportRound(scoreBlue.Value, scoreRed.Value);
                }
                break;
        }
    }

    // ------------------------------------------------------------------ hits

    /// <summary>The player on this headset hit a bunny with their hammer. Shows it at once and tells the host.</summary>
    public bool TryLocalHit(int popId)
    {
        if (!TurnRunning || Party == null || !Party.IsLocalHumanSide(ActiveSide)) return false;
        List<WhackPop> list = Pops;
        if (popId < 0 || popId >= list.Count || hit[popId]) return false;
        if (!WhackBunnySim.CanHit(list[popId], TurnTime, 0.05f)) return false;

        if (IsServer) ServerHit(popId, ActiveSide);
        else
        {
            MarkHit(popId, ActiveSide);
            HitServerRpc(popId, seed.Value);
        }
        return true;
    }

    /// <summary>Host: the bot hit a bunny.</summary>
    public void ServerBotHit(int popId)
    {
        if (IsServer && TurnRunning && Party != null && Party.IsBotSide(ActiveSide)) ServerHit(popId, ActiveSide);
    }

    [Rpc(SendTo.Server)]
    void HitServerRpc(int popId, int turnSeed, RpcParams rpc = default)
    {
        if (Party == null || turnSeed != seed.Value) return;
        int side = Party.SideOf(rpc.Receive.SenderClientId);
        if (side < 0 || side != ActiveSide) return;
        // the turn may have just ended on the host; still count hits made in time
        if (Phase != WhackPhase.Turn && Phase != WhackPhase.TurnOver) return;
        ServerHit(popId, side);
    }

    void ServerHit(int popId, int side)
    {
        List<WhackPop> list = Pops;
        if (popId < 0 || popId >= list.Count || hit[popId]) return;
        WhackPop p = list[popId];
        if (!WhackBunnySim.CanHit(p, TurnTime, hitSlack)) return;

        int points = Party != null ? (p.golden ? Party.whackPointsPerGolden : Party.whackPointsPerBunny) : 1;
        if (side == 0) scoreBlue.Value += points;
        else scoreRed.Value += points;
        HitRpc(popId, (byte)side, seed.Value);
    }

    [Rpc(SendTo.Everyone)]
    void HitRpc(int popId, byte side, int turnSeed)
    {
        if (turnSeed != seed.Value && !IsServer) return;
        MarkHit(popId, side);
    }

    void MarkHit(int popId, int side)
    {
        List<WhackPop> list = Pops;
        if (popId < 0 || popId >= list.Count || hit[popId]) return;
        hit[popId] = true;
        Hit?.Invoke(list[popId], side);
    }

    // ------------------------------------------------------------------ hammers

    /// <summary>Which hand the player on this headset holds the hammer in.</summary>
    public void SetLocalHand(bool left)
    {
        if (!IsSpawned || Party == null) return;
        int side = Party.LocalSide;
        if (side < 0 || LeftHanded(side) == left) return;
        HandServerRpc(left);
    }

    [Rpc(SendTo.Server)]
    void HandServerRpc(bool left, RpcParams rpc = default)
    {
        int side = Party != null ? Party.SideOf(rpc.Receive.SenderClientId) : -1;
        if (side < 0) return;
        int mask = leftHandMask.Value;
        mask = left ? mask | (1 << side) : mask & ~(1 << side);
        leftHandMask.Value = (byte)mask;
    }

    /// <summary>Sent by whoever drives a hammer (its player, or the host for the bot) so the others see it move.</summary>
    public void SendHammerPose(int side, Vector3 pos, Quaternion rot)
    {
        if (IsSpawned) HammerPoseRpc((byte)side, pos, rot);
    }

    [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
    void HammerPoseRpc(byte side, Vector3 pos, Quaternion rot)
    {
        WhackHammer h = WhackHammer.For(side);
        if (h != null) h.SetRemotePose(pos, rot);
    }
}
