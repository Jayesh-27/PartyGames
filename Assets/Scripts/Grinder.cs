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

        Grinder1.transform.Rotate(-Vector3.up * rotationSpeed * Time.deltaTime);
        Grinder2.transform.Rotate(-Vector3.up * rotationSpeed * Time.deltaTime);

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
        BulgeDeformer bulge = reward.GetComponent<BulgeDeformer>();
        pipeDeformable.DeformerElements.Add(new DeformerElement(bulge));

        if (bulge == null)
            Debug.LogWarning($"[Grinder] '{reward.name}' has no BulgeDeformer on its root", reward);

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

        pipeDeformable.DeformerElements.RemoveAll(e => e.Component == bulge);
        if (reward == null) yield break;

        Destroy(bulge);
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