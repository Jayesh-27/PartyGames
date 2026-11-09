using UnityEngine;

/// <summary>
/// Sits on the arena root and holds the arena size. The ball, the bot and the scene builder all read it from here.
/// </summary>
public class KeeperArena : MonoBehaviour
{
    public KeeperArenaSpec spec = new KeeperArenaSpec();

    static KeeperArena instance;
    static readonly KeeperArenaSpec fallback = new KeeperArenaSpec();

    public static KeeperArenaSpec Spec
    {
        get
        {
            if (instance == null) instance = FindFirstObjectByType<KeeperArena>();
            return instance != null ? instance.spec : fallback;
        }
    }

    void OnEnable() => instance = this;

    void OnDisable()
    {
        if (instance == this) instance = null;
    }
}
