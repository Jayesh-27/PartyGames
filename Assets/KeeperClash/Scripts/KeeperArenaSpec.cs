using System;
using UnityEngine;

/// <summary>
/// Size of the Keeper Clash arena. The scene builder makes the walls and goals from these numbers and the
/// ball simulation bounces off the same numbers, so what you see always matches what the ball hits.
/// The field runs along Z. Side 0 (Blue) defends the goal at -Z, side 1 (Red) defends the goal at +Z.
/// </summary>
[Serializable]
public class KeeperArenaSpec
{
    [Tooltip("Goal line to goal line, metres")]
    public float length = 14f;
    public float width = 9f;
    public float height = 6f;

    [Header("Goals")]
    public float goalWidth = 3.2f;
    public float goalHeight = 2.3f;
    public float goalDepth = 1.2f;
    public float postRadius = 0.07f;

    [Header("Ball and players")]
    public float ballRadius = 0.22f;
    [Tooltip("How far in front of the goal line the keeper stands")]
    public float keeperStandOff = 0.9f;
    [Tooltip("Where the ball waits before a serve")]
    public Vector3 parkPoint = new Vector3(0f, 2.2f, 0f);

    public float HalfLength => length * 0.5f;
    public float HalfWidth => width * 0.5f;

    /// <summary>-1 for Blue's end (-Z), +1 for Red's end (+Z).</summary>
    public static float Sign(int side) => side == 0 ? -1f : 1f;

    public float GoalLineZ(int side) => Sign(side) * HalfLength;
    public Vector3 GoalCenter(int side) => new Vector3(0f, goalHeight * 0.5f, GoalLineZ(side));

    /// <summary>The direction the keeper of this side faces (toward the other goal).</summary>
    public Vector3 Forward(int side) => new Vector3(0f, 0f, -Sign(side));

    /// <summary>Where the keeper of this side stands (on the floor).</summary>
    public Vector3 KeeperSpot(int side) => new Vector3(0f, 0f, Sign(side) * (HalfLength - keeperStandOff));

    /// <summary>True when a point is inside the hole of a goal (seen from the field, ignoring depth).</summary>
    public bool InGoalOpening(Vector3 p) => Mathf.Abs(p.x) < goalWidth * 0.5f && p.y < goalHeight;

    /// <summary>A point in the goal opening (u, v from 0 to 1), a little behind the line so the ball goes all the way in.</summary>
    public Vector3 TargetPoint(int side, float u, float v, float inset, float behind)
    {
        float m = ballRadius + inset;
        float x = Mathf.Lerp(-goalWidth * 0.5f + m, goalWidth * 0.5f - m, u);
        float y = Mathf.Lerp(ballRadius + 0.05f, goalHeight - m, v);
        return new Vector3(x, y, GoalLineZ(side) + Sign(side) * behind);
    }

    /// <summary>Moves a point into the goal opening of this side.</summary>
    public Vector3 ClampTarget(int side, Vector3 p, float inset, float behind)
    {
        float m = ballRadius + inset;
        p.x = Mathf.Clamp(p.x, -goalWidth * 0.5f + m, goalWidth * 0.5f - m);
        p.y = Mathf.Clamp(p.y, ballRadius + 0.05f, goalHeight - m);
        p.z = GoalLineZ(side) + Sign(side) * behind;
        return p;
    }
}
