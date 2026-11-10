using System.Collections;
using System.Collections.Generic;
using Deform;
using Unity.Netcode;
using UnityEngine;

public enum PickableItem
{
    rope,
    wooden_branch
}

[System.Serializable]
public struct Recipe
{
    public PickableItem[] sequence;
    public GameObject reward; // prefab with a BulgeDeformer on it
}

public class Grinder : MonoBehaviour
{
    [Tooltip("-1 = takes any item. 0 = Blue grinder, 1 = Red grinder: only takes items of that team.")]
    public int team = -1;

    private static readonly List<Grinder> all = new List<Grinder>();
    private void OnEnable() => all.Add(this);
    private void OnDisable() => all.Remove(this);

    /// <summary>The grinder of that team, or any grinder if there is no such team grinder.</summary>
    public static Grinder ForTeam(int team)
    {
        foreach (Grinder g in all)
            if (g.team == team) return g;
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>True if this grinder grinds this item: it is one of the recipe items and has the right team colour.</summary>
    public bool Accepts(GameObject item)
    {
        if (item == null) return false;
        if (team >= 0)
        {
            int itemTeam = TeamItem.TeamOf(item);
            if (itemTeam >= 0 && itemTeam != team) return false;
        }
        Transform root = item.transform;
        NetworkObject no = item.GetComponentInParent<NetworkObject>();
        if (no != null) root = no.transform;
        for (int i = 0; i < pickableObjects.Length; i++)
            if (pickableObjects[i] != null && (item.CompareTag(pickableObjects[i].tag) || root.CompareTag(pickableObjects[i].tag))) return true;
        return false;
    }

    [SerializeField] private GameObject Grinder1;
    [SerializeField] private GameObject Grinder2;
    [SerializeField] private float rotationSpeed = 1f;

    [SerializeField] private GameObject[] pickableObjects; // same order as the PickableItem enum
    [SerializeField] private Recipe[] recipes;
    [SerializeField] private Transform rewardSpawnPoint;
    [SerializeField] private Deformable pipeDeformable;
    [SerializeField] private float moveSpeed = 0.5f;
    [SerializeField] private float travelDistance = 0.8f; // how far the reward moves from the spawn point
    [SerializeField] private Vector3 travelDirection = Vector3.back; // in the reward spawn point's local space, e.g. (1,0,0) or (0,0,-1)
    [SerializeField] private bool resetAdded = false;

    [SerializeField] private List<PickableItem> addedItems = new List<PickableItem>();


    //temp/debug
    [SerializeField] private GameObject spawnRewardObject;
    [SerializeField] private bool spawnReward = false;


    private void Update()
    {
        if (resetAdded)
        {
            addedItems.Clear();
            resetAdded = false;
        }

        if (Grinder1 != null) Grinder1.transform.Rotate(-Vector3.up * rotationSpeed * Time.deltaTime);
        if (Grinder2 != null) Grinder2.transform.Rotate(-Vector3.up * rotationSpeed * Time.deltaTime);

        if (spawnReward)
        {
            spawnReward = false;
            SpawnReward(spawnRewardObject);
        }
    }

    public void onAdded(string itemTag)
    {
        for (int i = 0; i < pickableObjects.Length; i++)
        {
            if (itemTag != pickableObjects[i].tag)
                continue;

            addedItems.Add((PickableItem)i);
            CheckRecipes();
            return;
        }
    }

    private void CheckRecipes()
    {
        foreach (Recipe r in recipes)
        {
            if (!StartsWith(r.sequence, addedItems))
                continue;

            if (addedItems.Count == r.sequence.Length)
            {
                SpawnReward(r.reward);
                addedItems.Clear();
            }
            return;
        }

        addedItems.Clear();
    }

    // Online, only the server creates the reward (a networked spawn, so every player sees it).
    // Offline (no NetworkManager running) it is just a normal local instance.
    private void SpawnReward(GameObject prefab)
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool online = nm != null && nm.IsListening;
        if (online && !nm.IsServer)
            return;

        GameObject reward = Instantiate(prefab, rewardSpawnPoint.position, rewardSpawnPoint.rotation);
        if (online && reward.TryGetComponent(out NetworkObject netObject))
            netObject.Spawn();

        StartCoroutine(RiseReward(reward));
    }

    private IEnumerator RiseReward(GameObject reward)
    {
        // the pipe bulge is optional: the toon grinder has a chute instead of a pipe
        BulgeDeformer bulge = reward.GetComponent<BulgeDeformer>();
        if (pipeDeformable != null && bulge != null)
            pipeDeformable.DeformerElements.Add(new DeformerElement(bulge));

        // Keep the Rigidbody (Meta's grab scripts need it) but stop physics from acting on it while it travels
        Rigidbody rb = reward.GetComponent<Rigidbody>();
        bool hadGravity = false, hadCollisions = true, wasKinematic = false;
        if (rb != null)
        {
            hadGravity = rb.useGravity;
            hadCollisions = rb.detectCollisions;
            wasKinematic = rb.isKinematic;
            rb.useGravity = false;
            rb.detectCollisions = false;
            rb.isKinematic = true;
        }

        Vector3 direction = rewardSpawnPoint.TransformDirection(travelDirection).normalized;
        float travelled = 0f;

        while (reward != null && travelled < travelDistance)
        {
            float step = Mathf.Min(moveSpeed * Time.deltaTime, travelDistance - travelled);
            reward.transform.position += direction * step;
            travelled += step;
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            yield return null;
        }

        if (pipeDeformable != null && bulge != null)
            pipeDeformable.DeformerElements.RemoveAll(e => e.Component == bulge);
        if (reward == null) yield break;

        if (bulge != null) Destroy(bulge);
        if (rb != null)
        {
            rb.useGravity = hadGravity;
            rb.detectCollisions = hadCollisions;
            rb.isKinematic = wasKinematic;
        }
    }

    private bool StartsWith(PickableItem[] recipe, List<PickableItem> current)
    {
        if (current.Count > recipe.Length)
            return false;

        for (int i = 0; i < current.Count; i++)
            if (recipe[i] != current[i])
                return false;

        return true;
    }
}