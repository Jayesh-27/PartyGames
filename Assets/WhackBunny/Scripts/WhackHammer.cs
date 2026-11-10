using UnityEngine;

/// <summary>
/// One side's hammer. On the headset that plays this side it follows the controller, finds hits and sends its
/// pose to the others. For the bot the host moves it. Everywhere else it glides to the last pose received.
/// The hammer model has its grip at this object's origin and the handle along +Y.
/// </summary>
[DefaultExecutionOrder(50)]
public class WhackHammer : MonoBehaviour
{
    static readonly WhackHammer[] bySide = new WhackHammer[2];
    public static WhackHammer For(int side) => side >= 0 && side < 2 ? bySide[side] : null;

    public int side;
    public GameObject model;
    public Renderer[] renderers;

    [Header("Head (local to this object, set by the builder)")]
    public Vector3 headCenter = new Vector3(0f, 0.42f, 0f);
    [Tooltip("Half of the head's long side")]
    public Vector3 headHalf = new Vector3(0.09f, 0f, 0f);
    public float headRadius = 0.065f;

    [Header("Grip in the hand")]
    public Vector3 gripEuler = new Vector3(55f, 0f, 0f);
    public Vector3 gripOffset = new Vector3(0f, -0.02f, 0.02f);

    [Header("Hits")]
    [Tooltip("The head has to move down at least this fast (m/s) to count")]
    public float minDownSpeed = 0.8f;
    public float bunnyRadius = 0.085f;
    public float poseSendRate = 20f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int BaseColorFactorId = Shader.PropertyToID("baseColorFactor");

    readonly Vector3[] prev = new Vector3[3];
    readonly Vector3[] cur = new Vector3[3];
    bool havePrev;
    Vector3 remotePos;
    Quaternion remoteRot = Quaternion.identity;
    bool haveRemote;
    float nextSend, lastThump;
    bool dimmed;
    MaterialPropertyBlock mpb;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        if (side >= 0 && side < 2) bySide[side] = this;
    }

    void OnDestroy()
    {
        if (side >= 0 && side < 2 && bySide[side] == this) bySide[side] = null;
    }

    public void SetRemotePose(Vector3 pos, Quaternion rot)
    {
        remotePos = pos;
        remoteRot = rot;
        if (!haveRemote) transform.SetPositionAndRotation(pos, rot);
        haveRemote = true;
    }

    /// <summary>World position of the middle of the head.</summary>
    public Vector3 HeadPoint => transform.TransformPoint(headCenter);

    void LateUpdate()
    {
        PartySession party = PartySession.Instance;
        WhackMatch match = WhackMatch.Instance;
        bool inGame = party != null && match != null && match.IsSpawned;
        bool taken = inGame && (party.ClientOf(side) != PartySession.NoClient || party.IsBotSide(side));
        if (model != null && model.activeSelf != taken) model.SetActive(taken);
        if (!taken) { havePrev = false; haveRemote = false; return; }

        bool active = match.ActiveSide == side && (match.Phase == WhackPhase.Turn || match.Phase == WhackPhase.Ready);
        SetDimmed(!active);

        if (party.IsLocallyControlled(side))
        {
            bool placed = party.IsBotSide(side) ? PlaceBot() : PlaceInHand(match);
            if (!placed) { havePrev = false; return; }

            if (Time.unscaledTime >= nextSend)
            {
                nextSend = Time.unscaledTime + 1f / Mathf.Max(1f, poseSendRate);
                match.SendHammerPose(side, transform.position, transform.rotation);
            }
            if (!party.IsBotSide(side)) DetectHits(match);
        }
        else if (haveRemote)
        {
            float k = 1f - Mathf.Exp(-18f * Time.deltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, remotePos, k), Quaternion.Slerp(transform.rotation, remoteRot, k));
        }
    }

    bool PlaceInHand(WhackMatch match)
    {
        PartyRig rig = PartyRig.Instance;
        if (rig == null || rig.rig == null) return false;
        Transform hand = match.LeftHanded(side) ? rig.rig.leftHandAnchor : rig.rig.rightHandAnchor;
        if (hand == null) return false;
        transform.SetPositionAndRotation(hand.TransformPoint(gripOffset), hand.rotation * Quaternion.Euler(gripEuler));
        if (rig.JumpedThisFrame) havePrev = false;
        return true;
    }

    bool PlaceBot()
    {
        WhackBot bot = WhackBot.Instance;
        if (bot == null) return false;
        transform.SetPositionAndRotation(bot.GripPosition, bot.GripRotation);
        return true;
    }

    void DetectHits(WhackMatch match)
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < 3; i++) cur[i] = transform.TransformPoint(headCenter + headHalf * (i - 1));
        if (!havePrev || dt < 1e-5f)
        {
            for (int i = 0; i < 3; i++) prev[i] = cur[i];
            havePrev = true;
            return;
        }

        float down = -(cur[1].y - prev[1].y) / dt;
        WhackField field = WhackField.Instance;
        if (field != null && down >= minDownSpeed)
        {
            bool myTurn = match.TurnRunning && match.ActiveSide == side;
            if (myTurn && field.bunnies != null)
            {
                for (int h = 0; h < field.bunnies.Length; h++)
                {
                    WhackBunny b = field.bunnies[h];
                    if (b == null || !b.Hittable) continue;
                    int pop = field.PopAt(h);
                    if (pop < 0 || match.IsHit(pop)) continue;
                    Vector3 target = b.HeadPoint;
                    float reach = headRadius + bunnyRadius;
                    bool touching = false;
                    for (int i = 0; i < 3 && !touching; i++)
                        touching = SqrDistanceToSegment(target, prev[i], cur[i]) < reach * reach;
                    if (touching && match.TryLocalHit(pop))
                    {
                        PartyRig rig = PartyRig.Instance;
                        if (rig != null) rig.Haptic(match.LeftHanded(side) ? 0 : 1, 0.9f, 0.09f);
                        lastThump = Time.time;
                    }
                }
            }

            // the head landing on the table: a thud and a small buzz, so swings feel solid
            float top = field.transform.position.y + field.tableTopY + headRadius;
            Vector3 local = cur[1] - field.transform.position;
            if (prev[1].y > top && cur[1].y <= top && Time.time - lastThump > 0.2f &&
                Mathf.Abs(local.x) < field.tableHalfSize.x && Mathf.Abs(local.z) < field.tableHalfSize.y)
            {
                lastThump = Time.time;
                WhackFx.Thud(cur[1]);
                PartyRig rig = PartyRig.Instance;
                if (rig != null) rig.Haptic(match.LeftHanded(side) ? 0 : 1, 0.35f, 0.05f);
            }
        }
        for (int i = 0; i < 3; i++) prev[i] = cur[i];
    }

    static float SqrDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float len = ab.sqrMagnitude;
        float t = len > 1e-9f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len) : 0f;
        return (a + ab * t - p).sqrMagnitude;
    }

    void SetDimmed(bool on)
    {
        if (on == dimmed || renderers == null) return;
        dimmed = on;
        Color c = on ? new Color(0.45f, 0.47f, 0.52f, 1f) : Color.white;
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c);
            mpb.SetColor(BaseColorFactorId, c);
            r.SetPropertyBlock(mpb);
        }
    }
}
