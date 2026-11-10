using UnityEngine;

/// <summary>
/// Practice keeper for the Red goal while the host is alone. It runs on the host only: it watches where the
/// ball will cross its plane, floats there and blocks it. Its save chance drops as the ball gets faster.
/// </summary>
public class KeeperBot : MonoBehaviour
{
    public const int Side = 1;

    public Transform body;
    [Tooltip("Save chance at the slowest ball")]
    [Range(0f, 1f)] public float easySaveChance = 0.95f;
    [Tooltip("Save chance at the fastest ball")]
    [Range(0f, 1f)] public float hardSaveChance = 0.45f;
    public float moveSpeed = 4.5f;
    public float reach = 0.6f;

    public static float SaveChance(float heat01, float easy, float hard) => Mathf.Lerp(easy, hard, heat01);

    ushort decidedSeq;
    bool hasDecision;
    bool willSave;
    Vector3 missOffset;
    Vector3 lastPos;

    void Start()
    {
        if (body == null) body = transform;
        body.position = Home();
        lastPos = body.position;
    }

    Vector3 Home()
    {
        KeeperArenaSpec a = KeeperArena.Spec;
        Vector3 p = a.KeeperSpot(Side);
        p.y = 1.2f;
        return p;
    }

    void Update()
    {
        KeeperMatch match = KeeperMatch.Instance;
        KeeperBall ball = KeeperBall.Instance;
        bool active = match != null && match.IsSpawned && match.BotActive;
        if (body.gameObject.activeSelf != active) body.gameObject.SetActive(active);
        if (!active || ball == null) return;

        KeeperArenaSpec a = KeeperArena.Spec;
        KeeperBallState s = ball.State;
        float sign = KeeperArenaSpec.Sign(Side);
        float planeZ = a.KeeperSpot(Side).z;
        Vector3 goalPos = Home();

        bool incoming = ball.IsFlying && s.targetSide == Side;
        if (incoming && KeeperBallSim.PredictCrossing(s, planeZ, sign, a, ball.tuning, 3f, out Vector3 cross, out _))
        {
            if (!hasDecision || decidedSeq != s.seq)
            {
                hasDecision = true;
                decidedSeq = s.seq;
                willSave = Random.value < SaveChance(ball.Heat01, easySaveChance, hardSaveChance);
                missOffset = willSave ? Vector3.zero : new Vector3(Random.value < 0.5f ? -0.9f : 0.9f, Random.Range(-0.3f, 0.3f), 0f);
            }
            goalPos = new Vector3(cross.x, cross.y, planeZ) + missOffset;
        }

        float step = moveSpeed * Time.deltaTime;
        body.position = Vector3.MoveTowards(body.position, goalPos, step);
        Vector3 vel = (body.position - lastPos) / Mathf.Max(Time.deltaTime, 1e-4f);
        lastPos = body.position;
        body.rotation = Quaternion.LookRotation(a.Forward(Side));

        // only the host decides saves
        if (!match.IsServer || !incoming || !willSave || ball.ControlSide != Side) return;

        Vector3 bp = s.pos;
        bool reached = (bp.z - planeZ) * sign >= -a.ballRadius;
        if (reached && (bp - body.position).magnitude < reach + a.ballRadius)
        {
            Vector3 away = (bp - body.position).sqrMagnitude > 1e-4f ? (bp - body.position).normalized : a.Forward(Side);
            // swing at a random spot around the other goal (sometimes a bit wide) so practice shots vary
            Vector3 aimAt = a.TargetPoint(1 - Side, Random.Range(-0.15f, 1.15f), Random.Range(0.1f, 0.9f), 0.12f, 0f);
            Vector3 swing = (aimAt - bp).normalized * 3.5f + vel * 0.1f;
            ball.ServerBotTouch(away, swing);
            hasDecision = false;
        }
    }
}
