using System;
using Unity.Netcode;
using UnityEngine;

public enum KeeperBallMode : byte { Parked = 0, Flying = 1, Dead = 2, Pending = 3 }
public enum KeeperBallEvent : byte { Park = 0, Serve = 1, Touch = 2, Backboard = 3, Goal = 4 }

/// <summary>Ordered by importance: a step reports the most important thing that happened.</summary>
public enum KeeperStepResult { None = 0, Bounce = 1, Post = 2, Backboard = 3, Goal = 4 }

/// <summary>Everything a headset needs to continue the ball's flight exactly like the others do.</summary>
public struct KeeperBallState : INetworkSerializable
{
    public Vector3 pos;
    public Vector3 vel;
    public Vector3 target;      // aim point a little behind the target goal line
    public float speed;
    public float homeTimer;     // below 0: flying straight after a touch; ramps up to full homing
    public float flightTime;    // seconds since the last serve/touch/backboard
    public byte targetSide;     // goal the ball is heading for: 0 = Blue (-Z), 1 = Red (+Z)
    public byte mode;           // KeeperBallMode
    public byte touches;        // touches since the serve (sets the speed)
    public ushort seq;          // goes up on every serve/touch/backboard/goal

    public KeeperBallMode Mode
    {
        get => (KeeperBallMode)mode;
        set => mode = (byte)value;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref pos);
        s.SerializeValue(ref vel);
        s.SerializeValue(ref target);
        s.SerializeValue(ref speed);
        s.SerializeValue(ref homeTimer);
        s.SerializeValue(ref flightTime);
        s.SerializeValue(ref targetSide);
        s.SerializeValue(ref mode);
        s.SerializeValue(ref touches);
        s.SerializeValue(ref seq);
    }
}

[Serializable]
public class KeeperBallTuning
{
    [Header("Speed")]
    public float baseSpeed = 7f;
    public float speedPerTouch = 0.9f;
    public float maxSpeed = 22f;
    public bool speedUpOnBackboard = false;

    [Header("Homing")]
    [Tooltip("Seconds the ball flies straight right after a touch, so your hit direction shows")]
    public float homingDelay = 0.08f;
    [Tooltip("Seconds to go from no homing to full homing")]
    public float homingRamp = 0.25f;
    [Tooltip("Turning radius = distance to target x this. Keep it below 0.5 so the ball can always reach the goal")]
    public float turnRadiusFactor = 0.42f;
    public float minTurnRadius = 0.3f;
    public float maxTurnRadius = 6f;

    [Header("Aiming")]
    [Tooltip("Hand speed (m/s) where your swing starts to aim the shot")]
    public float aimMinSwing = 0.6f;
    [Tooltip("Hand speed (m/s) where your swing fully aims the shot")]
    public float aimFullSwing = 3f;
    [Range(0f, 1f)] public float aimInfluence = 1f;
    [Tooltip("Random spread around the aimed point, metres")]
    public float aimJitter = 0.25f;
    [Tooltip("Keep aim points this far inside the posts (on top of the ball radius)")]
    public float targetInset = 0.12f;
    [Tooltip("Aim points sit this far behind the goal line so the ball goes in fully")]
    public float targetBehindLine = 0.6f;

    public float SpeedFor(int touches) => Mathf.Min(maxSpeed, baseSpeed + speedPerTouch * touches);

    public float Heat01(float speed) =>
        maxSpeed > baseSpeed ? Mathf.Clamp01((speed - baseSpeed) / (maxSpeed - baseSpeed)) : 0f;
}

/// <summary>
/// The ball's flight, as plain maths: homing toward a point in the target goal, bounces off the floor, walls,
/// ceiling and goal frames, the backboard rule, and goal detection. It does not use Unity physics, so every
/// headset (and the self-test) computes the same flight.
/// </summary>
public static class KeeperBallSim
{
    /// <summary>
    /// Moves the ball forward by dt. Only the headset that runs the ball passes authoritative = true;
    /// that one bumps seq on a backboard, everyone else just predicts.
    /// </summary>
    public static KeeperStepResult Step(ref KeeperBallState s, float dt, KeeperArenaSpec a, KeeperBallTuning t,
        bool authoritative, out int goalSide)
    {
        goalSide = -1;
        if (s.Mode != KeeperBallMode.Flying || dt <= 0f) return KeeperStepResult.None;

        KeeperStepResult result = KeeperStepResult.None;
        float maxMove = Mathf.Max(0.02f, a.ballRadius * 0.5f);
        int n = Mathf.Clamp(Mathf.CeilToInt(s.speed * dt / maxMove), 1, 160);
        float h = dt / n;

        for (int i = 0; i < n; i++)
        {
            s.flightTime += h;
            Home(ref s, h, t);
            s.pos += s.vel * h;

            KeeperStepResult r = Collide(ref s, a, t, authoritative);
            if (r > result) result = r;

            if (IsGoal(s.pos, a, out goalSide)) return KeeperStepResult.Goal;
        }
        return result;
    }

    static void Home(ref KeeperBallState s, float h, KeeperBallTuning t)
    {
        s.homeTimer += h;
        if (s.homeTimer <= 0f) return;

        float strength = t.homingRamp > 0f ? Mathf.Clamp01(s.homeTimer / t.homingRamp) : 1f;
        Vector3 to = s.target - s.pos;
        float dist = to.magnitude;
        if (dist < 1e-4f) return;

        // tighter turns if a flight takes suspiciously long, so the ball can never circle forever
        float factor = t.turnRadiusFactor / (1f + Mathf.Max(0f, s.flightTime - 3f));
        float radius = Mathf.Clamp(dist * factor, t.minTurnRadius, t.maxTurnRadius);
        float maxAngle = s.speed / radius * h * strength;

        Vector3 dir = s.vel.sqrMagnitude > 1e-8f ? s.vel.normalized : to / dist;
        dir = Vector3.RotateTowards(dir, to / dist, maxAngle, 0f);
        s.vel = dir * s.speed;
    }

    static KeeperStepResult Collide(ref KeeperBallState s, KeeperArenaSpec a, KeeperBallTuning t, bool authoritative)
    {
        KeeperStepResult res = KeeperStepResult.None;
        float r = a.ballRadius;

        // goal posts and crossbars
        for (int side = 0; side < 2; side++)
            if (CollideFrame(ref s, a, side)) res = Max(res, KeeperStepResult.Post);

        // floor and ceiling
        if (s.pos.y < r) { s.pos.y = r; Reflect(ref s.vel, Vector3.up); res = Max(res, KeeperStepResult.Bounce); }
        if (s.pos.y > a.height - r) { s.pos.y = a.height - r; Reflect(ref s.vel, Vector3.down); res = Max(res, KeeperStepResult.Bounce); }

        // side walls
        float hw = a.HalfWidth - r;
        if (s.pos.x > hw) { s.pos.x = hw; Reflect(ref s.vel, Vector3.left); res = Max(res, KeeperStepResult.Bounce); }
        if (s.pos.x < -hw) { s.pos.x = -hw; Reflect(ref s.vel, Vector3.right); res = Max(res, KeeperStepResult.Bounce); }

        // end walls (the backboards) and the inside of the goals
        for (int side = 0; side < 2; side++)
        {
            float sign = KeeperArenaSpec.Sign(side);
            float depth = s.pos.z * sign; // how far toward this end the ball is

            if (!a.InGoalOpening(s.pos))
            {
                if (depth > a.HalfLength - r)
                {
                    s.pos.z = sign * (a.HalfLength - r);
                    Reflect(ref s.vel, new Vector3(0f, 0f, -sign));
                    if (side == s.targetSide)
                    {
                        // Backboard rule: missing the goal sends the ball at the other goal
                        Retarget(ref s, a, t, authoritative);
                        res = Max(res, KeeperStepResult.Backboard);
                    }
                    else res = Max(res, KeeperStepResult.Bounce);
                }
            }
            else if (depth > a.HalfLength)
            {
                // inside the goal box: side nets, roof and back net
                float gx = a.goalWidth * 0.5f - r;
                if (s.pos.x > gx) { s.pos.x = gx; Reflect(ref s.vel, Vector3.left); }
                if (s.pos.x < -gx) { s.pos.x = -gx; Reflect(ref s.vel, Vector3.right); }
                if (s.pos.y > a.goalHeight - r) { s.pos.y = a.goalHeight - r; Reflect(ref s.vel, Vector3.down); }
                float back = a.HalfLength + a.goalDepth - r;
                if (depth > back) { s.pos.z = sign * back; Reflect(ref s.vel, new Vector3(0f, 0f, -sign)); }
            }
        }
        return res;
    }

    static bool CollideFrame(ref KeeperBallState s, KeeperArenaSpec a, int side)
    {
        float z = a.GoalLineZ(side);
        float rr = a.ballRadius + a.postRadius;
        if (Mathf.Abs(s.pos.z - z) > rr) return false;

        float hw = a.goalWidth * 0.5f;
        float gh = a.goalHeight;
        bool hit = false;
        hit |= CollideSegment(ref s, new Vector3(-hw, 0f, z), new Vector3(-hw, gh, z), rr);
        hit |= CollideSegment(ref s, new Vector3(hw, 0f, z), new Vector3(hw, gh, z), rr);
        hit |= CollideSegment(ref s, new Vector3(-hw, gh, z), new Vector3(hw, gh, z), rr);
        return hit;
    }

    static bool CollideSegment(ref KeeperBallState s, Vector3 p0, Vector3 p1, float rr)
    {
        Vector3 seg = p1 - p0;
        float u = Mathf.Clamp01(Vector3.Dot(s.pos - p0, seg) / seg.sqrMagnitude);
        Vector3 closest = p0 + seg * u;
        Vector3 d = s.pos - closest;
        float dist = d.magnitude;
        if (dist >= rr || dist < 1e-6f) return false;

        Vector3 n = d / dist;
        s.pos = closest + n * rr;
        Reflect(ref s.vel, n);
        return true;
    }

    static void Reflect(ref Vector3 v, Vector3 n)
    {
        float vn = Vector3.Dot(v, n);
        if (vn < 0f) v -= 2f * vn * n;
    }

    static KeeperStepResult Max(KeeperStepResult a, KeeperStepResult b) => a > b ? a : b;

    /// <summary>True once the whole ball is over the line of either goal.</summary>
    public static bool IsGoal(Vector3 p, KeeperArenaSpec a, out int goalSide)
    {
        for (int side = 0; side < 2; side++)
        {
            float depth = p.z * KeeperArenaSpec.Sign(side);
            if (depth >= a.HalfLength + a.ballRadius && a.InGoalOpening(p))
            {
                goalSide = side;
                return true;
            }
        }
        goalSide = -1;
        return false;
    }

    /// <summary>Backboard rule. The new aim point comes from seq, so every headset picks the same one.</summary>
    public static void Retarget(ref KeeperBallState s, KeeperArenaSpec a, KeeperBallTuning t, bool authoritative)
    {
        int side = 1 - s.targetSide;
        s.targetSide = (byte)side;
        s.target = a.TargetPoint(side, Hash01(s.seq, 11u), Hash01(s.seq, 23u), t.targetInset, t.targetBehindLine);
        if (t.speedUpOnBackboard)
        {
            s.speed = Mathf.Min(t.maxSpeed, s.speed + t.speedPerTouch);
            s.vel = s.vel.normalized * s.speed;
        }
        s.flightTime = 0f;
        if (authoritative) s.seq++;
    }

    /// <summary>
    /// The keeper of hitterSide touched the ball: it now heads for the other goal, faster.
    /// awayNormal points from the hand/body to the ball. swingVelocity is the hand's velocity (aims the shot).
    /// </summary>
    public static void ApplyTouch(ref KeeperBallState s, KeeperArenaSpec a, KeeperBallTuning t, int hitterSide,
        Vector3 awayNormal, Vector3 swingVelocity, float rand1, float rand2)
    {
        int goal = 1 - hitterSide;
        s.touches = (byte)Mathf.Min(255, s.touches + 1);
        s.speed = t.SpeedFor(s.touches);

        Vector3 forward = a.Forward(hitterSide);
        Vector3 randomTarget = a.TargetPoint(goal, rand1, rand2, t.targetInset, t.targetBehindLine);
        Vector3 target = randomTarget;

        float swing = swingVelocity.magnitude;
        float aim = 0f;
        Vector3 swingDir = forward;
        if (swing > 1e-3f)
        {
            swingDir = swingVelocity / swing;
            aim = Mathf.Clamp01((swing - t.aimMinSwing) / Mathf.Max(0.01f, t.aimFullSwing - t.aimMinSwing)) * t.aimInfluence;
        }

        float towardGoal = Vector3.Dot(swingDir, forward);
        if (aim > 0f && towardGoal > 0.15f)
        {
            // where the swing direction would cross the other goal's line
            float dist = Mathf.Abs(a.GoalLineZ(goal) - s.pos.z);
            Vector3 onLine = s.pos + swingDir * (dist / towardGoal);
            Vector3 jitter = new Vector3(rand1 - 0.5f, rand2 - 0.5f, 0f) * (2f * t.aimJitter);
            Vector3 aimed = a.ClampTarget(goal, onLine + jitter, t.targetInset, t.targetBehindLine);
            target = Vector3.Lerp(randomTarget, aimed, aim);
        }
        else aim = 0f;

        s.target = a.ClampTarget(goal, target, t.targetInset, t.targetBehindLine);

        Vector3 toGoal = (s.target - s.pos).normalized;
        Vector3 launch = awayNormal.normalized * 0.5f + toGoal * 0.9f + swingDir * aim;
        if (Vector3.Dot(launch.normalized, forward) < 0.25f) launch += forward; // never back into your own goal
        s.vel = launch.normalized * s.speed;

        s.targetSide = (byte)goal;
        s.homeTimer = -t.homingDelay;
        s.flightTime = 0f;
        s.Mode = KeeperBallMode.Flying;
        s.seq++;
    }

    /// <summary>A fresh serve from the park point toward the given goal, a little off line so it curves.</summary>
    public static KeeperBallState Serve(KeeperArenaSpec a, KeeperBallTuning t, int side, ushort seq, float r1, float r2, float r3)
    {
        KeeperBallState s = new KeeperBallState
        {
            pos = a.parkPoint,
            speed = t.baseSpeed,
            targetSide = (byte)side,
            target = a.TargetPoint(side, r1, r2, t.targetInset, t.targetBehindLine),
            homeTimer = 0f,
            flightTime = 0f,
            touches = 0,
            seq = seq,
            Mode = KeeperBallMode.Flying
        };
        Vector3 to = (s.target - s.pos).normalized;
        Quaternion swerve = Quaternion.Euler((r3 - 0.5f) * 24f, (r1 - 0.5f) * 70f, 0f);
        s.vel = (swerve * to).normalized * s.speed;
        return s;
    }

    public static KeeperBallState Parked(KeeperArenaSpec a, KeeperBallTuning t, ushort seq) => new KeeperBallState
    {
        pos = a.parkPoint,
        vel = Vector3.zero,
        target = a.parkPoint,
        speed = t.baseSpeed,
        seq = seq,
        Mode = KeeperBallMode.Parked
    };

    /// <summary>
    /// Plays the flight forward without anyone touching it, until the ball crosses the plane z = planeZ
    /// (travelling toward sign). Used by the bot to know where to block.
    /// </summary>
    public static bool PredictCrossing(KeeperBallState s, float planeZ, float sign, KeeperArenaSpec a, KeeperBallTuning t,
        float maxTime, out Vector3 point, out float time)
    {
        const float dt = 1f / 90f;
        byte startTarget = s.targetSide;
        point = s.pos;
        for (time = 0f; time < maxTime; time += dt)
        {
            Vector3 before = s.pos;
            KeeperStepResult res = Step(ref s, dt, a, t, false, out _);
            if (s.targetSide != startTarget) return false; // hit the backboard: not coming any more

            float b = (before.z - planeZ) * sign;
            float e = (s.pos.z - planeZ) * sign;
            if (b < 0f && e >= 0f)
            {
                float f = Mathf.Abs(b) / Mathf.Max(1e-6f, Mathf.Abs(b) + Mathf.Abs(e));
                point = Vector3.Lerp(before, s.pos, f);
                return true;
            }
            if (res == KeeperStepResult.Goal)
            {
                point = s.pos;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Earliest moment in this frame (t from 0 to 1) at which two moving spheres touch.
    /// relStart/relEnd are (probe - ball) at the start and end of the frame.
    /// </summary>
    public static bool SweptTouch(Vector3 relStart, Vector3 relEnd, float radius, out float t, out Vector3 relAtTouch)
    {
        t = 0f;
        relAtTouch = relStart;
        float c = Vector3.Dot(relStart, relStart) - radius * radius;
        if (c <= 0f) return true; // already touching

        Vector3 d = relEnd - relStart;
        float qa = Vector3.Dot(d, d);
        if (qa < 1e-10f) return false;
        float qb = 2f * Vector3.Dot(relStart, d);
        float disc = qb * qb - 4f * qa * c;
        if (disc < 0f) return false;

        float root = (-qb - Mathf.Sqrt(disc)) / (2f * qa);
        if (root < 0f || root > 1f) return false;

        t = root;
        relAtTouch = relStart + d * root;
        return true;
    }

    /// <summary>Small deterministic hash to a number from 0 to 1.</summary>
    public static float Hash01(uint a, uint b)
    {
        uint h = a * 747796405u + b * 2891336453u + 0x9E3779B9u;
        h ^= h >> 16;
        h *= 0x7feb352du;
        h ^= h >> 15;
        h *= 0x846ca68bu;
        h ^= h >> 16;
        return (h & 0xFFFFFFu) / 16777216f;
    }

    /// <summary>True if sequence number a is older than b (handles wrap-around).</summary>
    public static bool SeqOlder(ushort a, ushort b) => (short)(a - b) < 0;
}
