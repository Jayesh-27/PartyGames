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
    public byte lastHitter;     // side that touched it last (its colour); NoHitter after a serve
    public ushort seq;          // goes up on every serve/touch/backboard/goal

    public const byte NoHitter = 255;

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
        s.SerializeValue(ref lastHitter);
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

    [Header("Homing (steering toward the centre of the goal)")]
    [Tooltip("How hard the ball keeps steering toward the centre of the goal it is chasing, in degrees per second. " +
             "0 = no steering at all: the ball flies straight where you hit it and only bounces off walls.")]
    [Range(0f, 360f)] public float homingStrength = 45f;
    [Tooltip("Extra steering per touch in a rally (degrees per second). 0 = same steering all rally")]
    public float homingPerTouch = 0f;
    [Tooltip("Seconds the ball flies straight right after a touch, so your hit direction shows")]
    public float homingDelay = 0.08f;
    [Tooltip("Seconds to go from no steering to full steering")]
    public float homingRamp = 0.25f;

    [Header("Aiming")]
    [Tooltip("Hand speed (m/s) where your swing starts to aim the shot")]
    public float aimMinSwing = 0.6f;
    [Tooltip("Hand speed (m/s) where the ball goes exactly where you swing")]
    public float aimFullSwing = 3f;
    [Tooltip("0 = the ball leaves exactly the way you hit it. 1 = every touch is sent straight at the goal centre")]
    [Range(0f, 1f)] public float launchAssist = 0.15f;
    [Tooltip("Random spread on every touch, degrees")]
    public float launchJitter = 3f;
    [Tooltip("A touch always sends the ball at least this much toward the other end (0 to 1), so it can't go back into your own goal")]
    [Range(0.1f, 0.9f)] public float minForward = 0.35f;
    [Tooltip("The steering aims this far behind the goal line so the ball goes in fully")]
    public float targetBehindLine = 0.6f;

    public float SteerDegrees(int touches) => Mathf.Max(0f, homingStrength + homingPerTouch * touches);

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
            KeepMovingEndward(ref s, t);

            if (IsGoal(s.pos, a, out goalSide)) return KeeperStepResult.Goal;
        }
        return result;
    }

    /// <summary>
    /// Steering: the ball keeps turning toward the centre of the goal it is chasing, at most SteerDegrees per
    /// second, for the whole flight. With 0 it never turns and flies straight like a normal ball.
    /// </summary>
    static void Home(ref KeeperBallState s, float h, KeeperBallTuning t)
    {
        s.homeTimer += h;
        if (s.homeTimer <= 0f) return;

        float degrees = t.SteerDegrees(s.touches);
        if (degrees <= 0f) return;

        // after a long flight steer harder, so a strong setting can never leave the ball circling
        if (s.flightTime > 4f) degrees *= 1f + (s.flightTime - 4f);

        float ramp = t.homingRamp > 0f ? Mathf.Clamp01(s.homeTimer / t.homingRamp) : 1f;
        Vector3 to = s.target - s.pos;
        float dist = to.magnitude;
        if (dist < 1e-4f) return;

        Vector3 dir = s.vel.sqrMagnitude > 1e-8f ? s.vel.normalized : to / dist;
        dir = TurnTowards(dir, to / dist, degrees * Mathf.Deg2Rad * h * ramp);
        s.vel = dir * s.speed;
    }

    /// <summary>
    /// A grazing hit on a post (or steering near the goal line) can leave the ball zig-zagging between the side
    /// walls while hardly moving toward either end. Keep part of its speed along the field so rallies never stall.
    /// </summary>
    static void KeepMovingEndward(ref KeeperBallState s, KeeperBallTuning t)
    {
        float minZ = s.speed * Mathf.Clamp(t.minForward * 0.8f, 0.05f, 0.9f);
        if (Mathf.Abs(s.vel.z) >= minZ) return;

        float sz = Mathf.Abs(s.vel.z) > 1e-4f ? Mathf.Sign(s.vel.z) : Mathf.Sign(s.target.z - s.pos.z);
        Vector3 lateral = new Vector3(s.vel.x, s.vel.y, 0f);
        float latSpeed = Mathf.Sqrt(Mathf.Max(0f, s.speed * s.speed - minZ * minZ));
        s.vel = (lateral.sqrMagnitude > 1e-8f ? lateral.normalized * latSpeed : Vector3.zero) + new Vector3(0f, 0f, sz * minZ);
    }

    /// <summary>Turns unit vector from toward unit vector to by at most maxRadians (plain maths, same on every device).</summary>
    public static Vector3 TurnTowards(Vector3 from, Vector3 to, float maxRadians)
    {
        float cos = Mathf.Clamp(Vector3.Dot(from, to), -1f, 1f);
        float angle = Mathf.Acos(cos);
        if (angle <= maxRadians || angle < 1e-6f) return to;

        Vector3 axis = Vector3.Cross(from, to);
        if (axis.sqrMagnitude < 1e-10f) axis = Vector3.Cross(from, Mathf.Abs(from.y) < 0.9f ? Vector3.up : Vector3.right);
        axis.Normalize();
        // Rodrigues' rotation of from around axis
        float c = Mathf.Cos(maxRadians), sn = Mathf.Sin(maxRadians);
        Vector3 r = from * c + Vector3.Cross(axis, from) * sn + axis * (Vector3.Dot(axis, from) * (1f - c));
        return r.normalized;
    }

    static KeeperStepResult Collide(ref KeeperBallState s, KeeperArenaSpec a, KeeperBallTuning t, bool authoritative)
    {
        KeeperStepResult res = KeeperStepResult.None;
        float r = a.ballRadius;

        // goal posts and crossbars
        for (int side = 0; side < 2; side++)
        {
            if (!CollideFrame(ref s, a, side)) continue;
            res = Max(res, KeeperStepResult.Post);
            // bounced off the frame of the goal it was chasing, back up the field: same as the backboard rule
            if (side == s.targetSide && s.vel.z * KeeperArenaSpec.Sign(side) < 0f)
            {
                Retarget(ref s, a, t, authoritative);
                res = Max(res, KeeperStepResult.Backboard);
            }
        }

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
        s.target = GoalTarget(a, t, side);
        if (t.speedUpOnBackboard)
        {
            s.speed = Mathf.Min(t.maxSpeed, s.speed + t.speedPerTouch);
            s.vel = s.vel.normalized * s.speed;
        }
        s.flightTime = 0f;
        if (authoritative) s.seq++;
    }

    /// <summary>The point the ball steers at: the centre of that goal, a little behind the line.</summary>
    public static Vector3 GoalTarget(KeeperArenaSpec a, KeeperBallTuning t, int side) =>
        a.GoalCenter(side) + new Vector3(0f, 0f, KeeperArenaSpec.Sign(side) * t.targetBehindLine);

    /// <summary>
    /// The keeper of hitterSide touched the ball: it now chases the other goal, faster, and takes that team's colour.
    /// It leaves the way it was hit: awayNormal points from the hand/body to the ball (a block), and a fast swing
    /// (swingVelocity, the hand's velocity) sends it along the swing. So you have to aim; a bad touch
    /// can hit the far wall, and the backboard rule then sends it back at your own goal.
    /// </summary>
    public static void ApplyTouch(ref KeeperBallState s, KeeperArenaSpec a, KeeperBallTuning t, int hitterSide,
        Vector3 awayNormal, Vector3 swingVelocity, float rand1, float rand2)
    {
        int goal = 1 - hitterSide;
        s.touches = (byte)Mathf.Min(254, s.touches + 1);
        s.speed = t.SpeedFor(s.touches);
        s.target = GoalTarget(a, t, goal);

        Vector3 forward = a.Forward(hitterSide);
        Vector3 dir = awayNormal.sqrMagnitude > 1e-8f ? awayNormal.normalized : forward;

        float swing = swingVelocity.magnitude;
        if (swing > 1e-3f)
        {
            float aim = Mathf.Clamp01((swing - t.aimMinSwing) / Mathf.Max(0.01f, t.aimFullSwing - t.aimMinSwing));
            dir = Vector3.Lerp(dir, swingVelocity / swing, aim);
            if (dir.sqrMagnitude < 1e-6f) dir = forward;
            dir.Normalize();
        }

        Vector3 toGoal = (s.target - s.pos).normalized;
        if (t.launchAssist > 0f) dir = Vector3.Lerp(dir, toGoal, t.launchAssist).normalized;

        // small random spread
        if (t.launchJitter > 0f)
        {
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            side.Normalize();
            Vector3 up = Vector3.Cross(dir, side);
            float j = t.launchJitter * Mathf.Deg2Rad;
            dir = (dir + side * Mathf.Tan((rand1 - 0.5f) * 2f * j) + up * Mathf.Tan((rand2 - 0.5f) * 2f * j)).normalized;
        }

        // always some speed toward the other end, so it can't go back into your own goal or stall
        float f = Vector3.Dot(dir, forward);
        if (f < t.minForward)
        {
            Vector3 lateral = dir - forward * f;
            float keep = Mathf.Sqrt(1f - t.minForward * t.minForward);
            dir = lateral.sqrMagnitude > 1e-8f ? lateral.normalized * keep + forward * t.minForward : forward;
        }
        s.vel = dir.normalized * s.speed;

        s.targetSide = (byte)goal;
        s.lastHitter = (byte)hitterSide;
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
            target = GoalTarget(a, t, side),
            lastHitter = KeeperBallState.NoHitter,
            homeTimer = 0f,
            flightTime = 0f,
            touches = 0,
            seq = seq,
            Mode = KeeperBallMode.Flying
        };
        // aimed a little off centre so it curves in; with no steering it still flies into the goal
        Vector3 aimAt = a.TargetPoint(side, r1, r2, 0.12f, t.targetBehindLine);
        Vector3 off = aimAt - s.target;
        off.z = 0f;
        off *= t.homingStrength > 0f ? Mathf.Lerp(1.2f, 2.2f, r3) : 0.6f;
        s.vel = (s.target + off - s.pos).normalized * s.speed;
        return s;
    }

    public static KeeperBallState Parked(KeeperArenaSpec a, KeeperBallTuning t, ushort seq) => new KeeperBallState
    {
        pos = a.parkPoint,
        vel = Vector3.zero,
        target = a.parkPoint,
        speed = t.baseSpeed,
        lastHitter = KeeperBallState.NoHitter,
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
