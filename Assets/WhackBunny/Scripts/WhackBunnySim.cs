using System;
using System.Collections.Generic;

[Serializable]
public class WhackTuning
{
    public int holes = 6;
    [UnityEngine.Tooltip("Seconds between pops at the start and at the end of a turn")]
    public float gapStart = 0.85f;
    public float gapEnd = 0.42f;
    [UnityEngine.Tooltip("How long a bunny stays up at the start and at the end of a turn")]
    public float stayStart = 1.35f;
    public float stayEnd = 0.85f;
    [UnityEngine.Tooltip("Bunnies up at the same time, at most")]
    public int maxUp = 2;
    [UnityEngine.Range(0f, 1f)] public float goldenChance = 0.12f;
    [UnityEngine.Tooltip("A golden bunny stays up for this part of the normal time")]
    public float goldenStay = 0.7f;
    [UnityEngine.Tooltip("A hole rests this long before a bunny can use it again")]
    public float holeRest = 0.3f;
    [UnityEngine.Tooltip("Seconds a bunny takes to rise or drop")]
    public float riseTime = 0.14f;
}

public struct WhackPop
{
    public int id;
    public int hole;
    public float up;      // seconds after the turn starts
    public float stay;    // seconds it stays up
    public bool golden;
    public float Down => up + stay;
}

/// <summary>
/// Which bunny pops out of which hole and when. It is plain maths from a seed, so the host and
/// both headsets work out the same turn without sending every pop over the network.
/// </summary>
public static class WhackBunnySim
{
    // small own random generator: the same numbers on every device
    struct Rng
    {
        uint s;
        public Rng(int seed) { s = (uint)seed * 2654435761u + 0x9E3779B9u; if (s == 0) s = 1; }
        public float Next()
        {
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            return (s & 0xFFFFFF) / 16777216f;
        }
        public int Range(int n) => Math.Min(n - 1, (int)(Next() * n));
    }

    public static List<WhackPop> Schedule(int seed, float turnSeconds, WhackTuning t)
    {
        var pops = new List<WhackPop>();
        Rng rng = new Rng(seed);
        float[] freeAt = new float[Math.Max(1, t.holes)];
        float time = 0.35f;
        int lastHole = -1;
        float lastStart = turnSeconds - 0.55f;

        while (time < lastStart && pops.Count < 250)
        {
            float k = turnSeconds > 0f ? time / turnSeconds : 1f;
            bool golden = rng.Next() < t.goldenChance;
            float stay = Lerp(t.stayStart, t.stayEnd, k) * (golden ? t.goldenStay : 1f);

            // wait until fewer than maxUp bunnies are up
            float start = time;
            for (int guard = 0; guard < 8; guard++)
            {
                int up = 0;
                float soonest = float.MaxValue;
                foreach (WhackPop p in pops)
                    if (p.up <= start && p.Down > start) { up++; soonest = Math.Min(soonest, p.Down); }
                if (up < Math.Max(1, t.maxUp)) break;
                start = soonest + 0.02f;
            }
            if (start >= lastStart) break;
            stay = Math.Min(stay, turnSeconds - start);

            // a free hole, not the one just used when there is a choice
            int hole = -1;
            int first = rng.Range(freeAt.Length);
            for (int pass = 0; pass < 2 && hole < 0; pass++)
                for (int i = 0; i < freeAt.Length; i++)
                {
                    int h = (first + i) % freeAt.Length;
                    if (freeAt[h] > start) continue;
                    if (pass == 0 && h == lastHole) continue;
                    hole = h;
                    break;
                }

            if (hole >= 0)
            {
                pops.Add(new WhackPop { id = pops.Count, hole = hole, up = start, stay = stay, golden = golden });
                freeAt[hole] = start + stay + t.holeRest;
                lastHole = hole;
            }
            time = start + Lerp(t.gapStart, t.gapEnd, k) * (0.8f + 0.4f * rng.Next());
        }
        return pops;
    }

    static float Lerp(float a, float b, float k) => a + (b - a) * Math.Max(0f, Math.Min(1f, k));

    /// <summary>How far out of its hole a bunny is (0 hidden, 1 fully up) at this time of the turn.</summary>
    public static float Height(WhackPop p, float time, float rise)
    {
        if (time <= p.up || time >= p.Down + rise) return 0f;
        if (time < p.up + rise) return (time - p.up) / rise;
        if (time < p.Down) return 1f;
        return 1f - (time - p.Down) / rise;
    }

    /// <summary>True while this pop can be hit (with a little slack for network delay).</summary>
    public static bool CanHit(WhackPop p, float time, float slack) => time >= p.up - 0.02f && time <= p.Down + slack;
}
