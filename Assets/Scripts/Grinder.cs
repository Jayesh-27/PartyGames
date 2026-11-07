using System.Collections;
using System.Collections.Generic;
using Deform;
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
    [SerializeField] private float endZ = -1.3f;
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
            GameObject reward = Instantiate(spawnRewardObject, rewardSpawnPoint.position, rewardSpawnPoint.rotation);
            StartCoroutine(RiseReward(reward));
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
                GameObject reward = Instantiate(r.reward, rewardSpawnPoint.position, rewardSpawnPoint.rotation);
                StartCoroutine(RiseReward(reward));
                addedItems.Clear();
            }
            return;
        }

        addedItems.Clear();
    }

    private IEnumerator RiseReward(GameObject reward)
    {
        BulgeDeformer bulge = reward.GetComponent<BulgeDeformer>();
        pipeDeformable.DeformerElements.Add(new DeformerElement(bulge));

        while (reward.transform.position.z > endZ)
        {
            reward.transform.position += Vector3.back * moveSpeed * Time.deltaTime;
            yield return null;
        }

        pipeDeformable.DeformerElements.RemoveAll(e => e.Component == bulge);
        Destroy(bulge);
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