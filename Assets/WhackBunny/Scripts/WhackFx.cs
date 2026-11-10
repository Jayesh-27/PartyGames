using UnityEngine;

/// <summary>Whack-a-Bunny effects and sounds: bonk stars, pop puffs, table thuds, countdown beeps.</summary>
public class WhackFx : MonoBehaviour
{
    public static WhackFx Instance { get; private set; }

    public ParticleSystem stars;
    public ParticleSystem goldStars;
    public ParticleSystem puff;

    WhackPhase lastPhase = WhackPhase.Waiting;
    int lastBeep = -1, lastTick = -1;
    readonly bool[] wasUp = new bool[16];

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static void Bonk(Vector3 point, bool golden, int side)
    {
        PartySfx.PlayAt(PartySfx.Bonk, point, 1f, golden ? 1.25f : Random.Range(0.92f, 1.08f));
        PartySfx.PlayAt(PartySfx.Squeak, point, 0.6f, golden ? 1.4f : Random.Range(0.9f, 1.2f));
        WhackFx fx = Instance;
        if (fx == null) return;
        Emit(golden ? fx.goldStars : fx.stars, point, golden ? 26 : 12);
        if (golden) Emit(fx.stars, point, 8);
    }

    public static void Thud(Vector3 point)
    {
        PartySfx.PlayAt(PartySfx.Miss, point, 0.7f, Random.Range(0.9f, 1.1f));
        if (Instance != null) Emit(Instance.puff, point, 4);
    }

    static void Emit(ParticleSystem ps, Vector3 point, int count)
    {
        if (ps == null) return;
        ps.transform.position = point;
        ps.Emit(count);
    }

    void Update()
    {
        WhackMatch m = WhackMatch.Instance;
        if (m == null || !m.IsSpawned) { lastBeep = lastTick = -1; return; }

        if (m.Phase == WhackPhase.Ready)
        {
            int sec = Mathf.CeilToInt(m.PhaseTimeLeft);
            if (sec != lastBeep && sec > 0 && sec <= 3)
            {
                lastBeep = sec;
                PartySfx.Play2D(PartySfx.Beep, 0.6f);
            }
        }
        else lastBeep = -1;

        if (m.Phase == WhackPhase.Turn)
        {
            if (lastPhase != WhackPhase.Turn) PartySfx.Play2D(PartySfx.Go, 0.7f);
            int sec = Mathf.CeilToInt(m.TurnTimeLeft);
            if (sec != lastTick && sec > 0 && sec <= 3)
            {
                lastTick = sec;
                PartySfx.Play2D(PartySfx.Tick, 0.7f);
            }
        }
        else
        {
            lastTick = -1;
            if (lastPhase == WhackPhase.Turn) PartySfx.Play2D(PartySfx.Beep, 0.8f, 0.6f);
        }
        lastPhase = m.Phase;

        // a little pop when a bunny comes up
        WhackField field = WhackField.Instance;
        if (field == null || field.bunnies == null) return;
        for (int i = 0; i < field.bunnies.Length && i < wasUp.Length; i++)
        {
            bool up = field.PopAt(i) >= 0 && field.bunnies[i] != null && m.Phase == WhackPhase.Turn;
            if (up && !wasUp[i])
            {
                Vector3 p = field.HolePoint(i);
                PartySfx.PlayAt(PartySfx.Pop, p, 0.5f, Random.Range(0.9f, 1.25f));
                Emit(puff, p + Vector3.up * 0.03f, 5);
            }
            wasUp[i] = up;
        }
    }
}
