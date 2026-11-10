using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The bot's turn, on the host: it decides which bunnies it will hit (with a reaction delay and some misses),
/// swings its hammer over each one and tells WhackMatch when the hammer lands.
/// </summary>
public class WhackBot : MonoBehaviour
{
    public static WhackBot Instance { get; private set; }

    public int side = 1;
    [Range(0f, 1f)] public float hitChance = 0.72f;
    [Range(0f, 1f)] public float goldenHitChance = 0.5f;
    public float reactionMin = 0.42f;
    public float reactionMax = 0.8f;
    [Tooltip("The bot needs this long between two hits")]
    public float recover = 0.33f;
    [Tooltip("Distance from grip to the middle of the hammer head")]
    public float hammerReach = 0.42f;
    public float swingTime = 0.26f;

    struct Plan { public int pop; public int hole; public float time; }

    readonly List<Plan> plans = new List<Plan>();
    int next;
    int plannedSeed = int.MinValue;
    Vector3 grip;
    Quaternion gripRot = Quaternion.identity;
    Vector3 hover;
    float raise = 70f;

    public Vector3 GripPosition => grip;
    public Quaternion GripRotation => gripRot;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        WhackMatch m = WhackMatch.Instance;
        WhackField field = WhackField.Instance;
        PartySession party = PartySession.Instance;
        if (m == null || field == null || party == null || !m.IsSpawned || !m.IsServer || !party.IsBotSide(side)) return;

        Vector3 toward = field.Forward(side);
        Vector3 rest = field.transform.position + Vector3.up * (field.tableTopY + 0.22f) - toward * 0.25f;
        Vector3 aim = rest;
        float wantRaise = 60f;

        if (m.TurnRunning && m.ActiveSide == side)
        {
            List<WhackPop> pops = m.Pops;
            if (plannedSeed != m.TurnSeed) MakePlan(m, pops);

            float t = m.TurnTime;
            while (next < plans.Count && t >= plans[next].time)
            {
                m.ServerBotHit(plans[next].pop);
                next++;
            }
            if (next < plans.Count)
            {
                Plan p = plans[next];
                float until = p.time - t;
                if (until < swingTime + 0.25f)
                {
                    aim = field.HolePoint(p.hole) + Vector3.up * 0.11f;
                    wantRaise = until < swingTime ? Mathf.Lerp(4f, 75f, Mathf.Clamp01(until / swingTime)) : 75f;
                }
            }
        }
        else plannedSeed = int.MinValue;

        float k = 1f - Mathf.Exp(-16f * Time.deltaTime);
        hover = Vector3.Lerp(hover, aim, k);
        raise = Mathf.Lerp(raise, wantRaise, 1f - Mathf.Exp(-40f * Time.deltaTime));

        // the handle points from the grip to the head; raised = tilted up, strike = flat over the hole
        float a = raise * Mathf.Deg2Rad;
        Vector3 handle = toward * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a);
        grip = hover - toward * hammerReach * 0.98f + Vector3.up * 0.02f;
        gripRot = Quaternion.LookRotation(Vector3.Cross(handle, Vector3.Cross(Vector3.up, toward)).normalized, handle);
    }

    void MakePlan(WhackMatch m, List<WhackPop> pops)
    {
        plannedSeed = m.TurnSeed;
        plans.Clear();
        next = 0;
        float free = 0f;
        foreach (WhackPop p in pops)
        {
            if (Random.value > (p.golden ? goldenHitChance : hitChance)) continue;
            float time = Mathf.Max(p.up + Random.Range(reactionMin, reactionMax), free);
            if (time > p.Down - 0.03f) continue;
            plans.Add(new Plan { pop = p.id, hole = p.hole, time = time });
            free = time + recover;
        }
    }
}
