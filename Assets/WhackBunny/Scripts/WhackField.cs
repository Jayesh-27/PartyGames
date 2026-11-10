using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The table: where the holes are and the bunny in each one. Every frame it reads the turn from WhackMatch
/// and tells each bunny how far out it should be.
/// </summary>
[DefaultExecutionOrder(-10)]
public class WhackField : MonoBehaviour
{
    public static WhackField Instance { get; private set; }

    [Tooltip("One per hole, in the order of the pop schedule")]
    public WhackBunny[] bunnies;
    [Tooltip("Height of the table top, metres")]
    public float tableTopY = 0.8f;
    [Tooltip("Half size of the table top (x, z), for the hammer-on-table thump")]
    public Vector2 tableHalfSize = new Vector2(0.6f, 0.375f);
    [Tooltip("Blue stands at -Z, Red at +Z. Distance from the table centre to where a player stands")]
    public float standDistance = 0.85f;

    readonly int[] popAtHole = new int[16];
    WhackMatch hooked;
    float[] want = new float[0];
    bool[] gold = new bool[0];

    void Awake()
    {
        Instance = this;
        for (int i = 0; i < popAtHole.Length; i++) popAtHole[i] = -1;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (hooked != null) hooked.Hit -= OnHit;
    }

    public static float Sign(int side) => side == 0 ? -1f : 1f;
    public Vector3 StandSpot(int side) => transform.position + new Vector3(0f, 0f, Sign(side) * standDistance);
    public Vector3 Forward(int side) => new Vector3(0f, 0f, -Sign(side));
    public Vector3 HolePoint(int hole) => bunnies != null && hole >= 0 && hole < bunnies.Length && bunnies[hole] != null
        ? bunnies[hole].transform.position : transform.position + Vector3.up * tableTopY;

    /// <summary>The pop currently in this hole, or -1.</summary>
    public int PopAt(int hole) => hole >= 0 && hole < popAtHole.Length ? popAtHole[hole] : -1;

    void Update()
    {
        WhackMatch m = WhackMatch.Instance;
        if (m != hooked)
        {
            if (hooked != null) hooked.Hit -= OnHit;
            hooked = m;
            if (hooked != null) hooked.Hit += OnHit;
        }
        if (bunnies == null) return;

        for (int i = 0; i < bunnies.Length && i < popAtHole.Length; i++) popAtHole[i] = -1;

        bool running = m != null && m.IsSpawned && (m.Phase == WhackPhase.Turn || m.Phase == WhackPhase.TurnOver);
        if (!running)
        {
            foreach (WhackBunny b in bunnies)
                if (b != null) b.SetTarget(0f, false);
            return;
        }

        float t = m.TurnTime;
        float rise = m.tuning.riseTime;
        List<WhackPop> pops = m.Pops;
        if (want.Length != bunnies.Length) { want = new float[bunnies.Length]; gold = new bool[bunnies.Length]; }
        for (int i = 0; i < want.Length; i++) { want[i] = 0f; gold[i] = false; }
        for (int i = 0; i < pops.Count; i++)
        {
            WhackPop p = pops[i];
            if (p.hole < 0 || p.hole >= bunnies.Length) continue;
            if (t < p.up || t > p.Down + rise) continue;
            popAtHole[p.hole] = p.id;
            want[p.hole] = m.IsHit(p.id) ? 0f : WhackBunnySim.Height(p, t, rise);
            gold[p.hole] = p.golden;
        }

        Vector3 face = Forward(m.ActiveSide >= 0 ? m.ActiveSide : 0) * -1f;
        for (int i = 0; i < bunnies.Length; i++)
        {
            WhackBunny b = bunnies[i];
            if (b == null) continue;
            if (want[i] > 0.01f && !b.Hittable) b.Face(face);
            b.SetTarget(want[i], gold[i]);
        }
    }

    void OnHit(WhackPop pop, int side)
    {
        if (bunnies == null || pop.hole < 0 || pop.hole >= bunnies.Length || bunnies[pop.hole] == null) return;
        WhackBunny b = bunnies[pop.hole];
        b.Bonk();
        WhackFx.Bonk(b.HeadPoint, pop.golden, side);
    }
}
