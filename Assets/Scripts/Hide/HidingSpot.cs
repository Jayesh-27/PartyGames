using UnityEngine;

/// <summary>A place where an item can be hidden when the game starts: inside a drawer, a crate, under a bucket...</summary>
public class HidingSpot : MonoBehaviour
{
    [Tooltip("Roomy spots take any item. Small spots (under a bucket, in a trash can) only take small items.")]
    public bool large = true;

    void OnDrawGizmos()
    {
        Gizmos.color = large ? new Color(1f, 0.8f, 0.2f, 0.8f) : new Color(0.3f, 1f, 0.5f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, large ? 0.1f : 0.05f);
    }
}
