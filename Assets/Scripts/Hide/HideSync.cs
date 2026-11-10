using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// One per scene. When the host starts, it hides the Blue and Red items in random hiding spots
/// (several copies of each). It also keeps the drawers in step between the headsets.
/// </summary>
public class HideSync : NetworkBehaviour
{
    public static HideSync Instance { get; private set; }

    [Header("Items to hide (prefabs with a TeamItem and a NetworkObject)")]
    public GameObject[] itemPrefabs;
    [Tooltip("How many of each item prefab get hidden")]
    public int copiesOfEach = 3;
    public HidingSpot[] spots;

    [Header("Drawers")]
    public SlideDrawer[] drawers;

    [Header("Testing")]
    [Tooltip("Tick in Play mode (host) to remove the hidden items and hide a fresh set")]
    public bool hideAgain;

    readonly List<NetworkObject> spawned = new List<NetworkObject>();
    float[] drawerOpen = new float[0];

    void Awake()
    {
        Instance = this;
        for (int i = 0; i < drawers.Length; i++) if (drawers[i] != null) drawers[i].id = i;
        drawerOpen = new float[drawers.Length];
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (!IsServer) return;
        NetworkManager.OnClientConnectedCallback += OnClientConnected;
        HideItems();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null) NetworkManager.OnClientConnectedCallback -= OnClientConnected;
    }

    void Update()
    {
        if (hideAgain)
        {
            hideAgain = false;
            if (IsSpawned && IsServer) HideItems();
        }
    }

    // ------------------------------------------------------------------ hiding items

    /// <summary>Host: removes the items hidden earlier and hides a new random set.</summary>
    public void HideItems()
    {
        if (!IsServer) return;
        foreach (NetworkObject old in spawned)
            if (old != null && old.IsSpawned) old.Despawn(true);
        spawned.Clear();

        var free = new List<HidingSpot>();
        foreach (HidingSpot s in spots) if (s != null && s.gameObject.activeInHierarchy) free.Add(s);
        Shuffle(free);

        var todo = new List<GameObject>();
        foreach (GameObject prefab in itemPrefabs)
            for (int i = 0; prefab != null && i < copiesOfEach; i++) todo.Add(prefab);
        // items that need a roomy spot pick first, so the small ones get what is left
        Shuffle(todo);
        todo.Sort((a, b) => NeedsLarge(b).CompareTo(NeedsLarge(a)));

        int hidden = 0;
        foreach (GameObject prefab in todo)
        {
            bool needsLarge = NeedsLarge(prefab);
            int at = free.FindIndex(s => s.large || !needsLarge);
            // small items prefer small spots, to leave the roomy ones for the big items
            if (!needsLarge)
            {
                int small = free.FindIndex(s => !s.large);
                if (small >= 0) at = small;
            }
            if (at < 0)
            {
                Debug.LogWarning($"[Hide] No free hiding spot for {prefab.name}. Add more hiding spots or hide fewer copies.");
                continue;
            }
            HidingSpot spot = free[at];
            free.RemoveAt(at);

            Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            GameObject item = Instantiate(prefab, spot.transform.position, rot);
            NetworkObject no = item.GetComponent<NetworkObject>();
            if (no == null) { Destroy(item); continue; }
            no.Spawn(true);
            spawned.Add(no);
            hidden++;
        }
        Debug.Log($"[Hide] Hid {hidden} items in {spots.Length} hiding spots.");
    }

    static bool NeedsLarge(GameObject prefab)
    {
        TeamItem t = prefab.GetComponent<TeamItem>();
        return t != null && t.needsLargeSpot;
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ------------------------------------------------------------------ drawers

    /// <summary>Called by a drawer that a hand on this headset is moving.</summary>
    public void SendDrawer(int id, float open)
    {
        if (id < 0 || id >= drawerOpen.Length) return;
        drawerOpen[id] = open;
        if (IsSpawned) DrawerServerRpc((byte)id, open);
    }

    [Rpc(SendTo.Server)]
    void DrawerServerRpc(byte id, float open, RpcParams rpc = default)
    {
        if (id >= drawerOpen.Length) return;
        drawerOpen[id] = open;
        DrawerRpc(id, open, rpc.Receive.SenderClientId);
    }

    [Rpc(SendTo.Everyone)]
    void DrawerRpc(byte id, float open, ulong mover)
    {
        if (mover == NetworkManager.LocalClientId || id >= drawers.Length || drawers[id] == null) return;
        drawerOpen[id] = open;
        if (!drawers[id].HeldHere) drawers[id].SetRemote(open);
    }

    void OnClientConnected(ulong clientId)
    {
        if (!IsServer || clientId == NetworkManager.LocalClientId) return;
        // tell the newcomer which drawers are already open
        for (int i = 0; i < drawers.Length; i++)
        {
            if (drawers[i] == null) continue;
            float open = drawers[i].Open;
            if (open > 0.005f) DrawerToRpc((byte)i, open, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void DrawerToRpc(byte id, float open, RpcParams target)
    {
        if (id < drawers.Length && drawers[id] != null) drawers[id].SetRemote(open);
    }
}
