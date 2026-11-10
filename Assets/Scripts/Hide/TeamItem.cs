using UnityEngine;

public enum TeamItemKind { Wood = 0, Rope = 1, Stone = 2 }

/// <summary>
/// Marks a findable item as Blue (0) or Red (1). A grinder only takes items of its own team;
/// anybody can still pick up, hide or throw the other team's items.
/// </summary>
public class TeamItem : MonoBehaviour
{
    public const int Blue = 0, Red = 1;

    [Tooltip("0 = Blue, 1 = Red")]
    public int team;
    public TeamItemKind kind;
    [Tooltip("True if it only fits in roomy hiding spots (drawers, crates, boxes)")]
    public bool needsLargeSpot;

    public static int TeamOf(GameObject go)
    {
        TeamItem item = go != null ? go.GetComponentInParent<TeamItem>() : null;
        return item != null ? item.team : -1;
    }
}
