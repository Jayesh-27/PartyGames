using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;

public class ObjectDetection : MonoBehaviour
{
    [SerializeField] private GameObject animatePosition;
    [SerializeField] private float deleteAfter = 2f;
    [SerializeField] private LayerMask pickable_layers;
    [SerializeField] private float downSpeed = -0.01f;
    [SerializeField] private Grinder grinder;

    [Header("Wobble")]
    [SerializeField] private float wobbleAngle = 10f;  // strength in degrees
    [SerializeField] private float wobbleSpeed = 6f;   // sine frequency
    [SerializeField] private float noiseSpeed = 8f;    // jitter frequency
    [SerializeField] private float noiseAmount = 1f;   // 0 = pure sine, higher = more jitter

    private bool enable = false;
    private GameObject targetObject = null;

    [Header("Debug")]           // temp
    [SerializeField] private GameObject ObjectToSpawn = null;
    [SerializeField] private GameObject WhereToSpawn = null;
    [SerializeField] private bool spawnTempObject = false;

    private void OnTriggerEnter(Collider other)
    {
        if (enable) return;

        if ((pickable_layers.value & (1 << other.gameObject.layer)) == 0)
            return;

        // Online: only the player who owns the item (the one who threw it in) runs the animation;
        // the owner-authoritative NetworkTransform shows it to everyone else.
        NetworkObject netObject = other.GetComponentInParent<NetworkObject>();
        if (netObject != null && netObject.IsSpawned && !netObject.IsOwner)
            return;

        targetObject = other.gameObject;

        // force the player to let go
        foreach (GrabInteractable g in targetObject.GetComponentsInChildren<GrabInteractable>())
            g.enabled = false;
        foreach (HandGrabInteractable h in targetObject.GetComponentsInChildren<HandGrabInteractable>())
            h.enabled = false;
        foreach (Grabbable g in targetObject.GetComponentsInChildren<Grabbable>())
            g.enabled = false;

        targetObject.transform.position = animatePosition.transform.position;
        targetObject.transform.rotation = animatePosition.transform.rotation;

        StartCoroutine(animateTargetObject());
    }

    private IEnumerator animateTargetObject()
    {
        enable = true;
        yield return null; // let the release finish before removing physics

        Destroy(targetObject.GetComponent<BoxCollider>());
        Destroy(targetObject.GetComponent<Rigidbody>());

        yield return new WaitForSeconds(deleteAfter);
        enable = false;

        NetworkedPickable pickable = targetObject.GetComponentInParent<NetworkedPickable>();
        if (pickable != null && pickable.IsSpawned)
        {
            pickable.RequestConsume(); // the server counts it for the recipe and despawns it for everyone
        }
        else
        {
            grinder.onAdded(targetObject.tag);
            Destroy(targetObject);
        }
    }

    private void Update()
    {
        // --------------- TEMP -----------------
        if(spawnTempObject)
        {
            Instantiate(ObjectToSpawn, WhereToSpawn.transform.position, WhereToSpawn.transform.rotation);
            spawnTempObject = false;
        }



        if (!enable || targetObject == null)
            return;

        targetObject.transform.position += Vector3.down * downSpeed * Time.deltaTime;
        targetObject.transform.rotation = animatePosition.transform.rotation * Quaternion.Euler((Mathf.Sin(Time.time * wobbleSpeed)
        + (Mathf.PerlinNoise(0f, Time.time * noiseSpeed) - 0.5f) * noiseAmount) * wobbleAngle, 0f, 0f);
    }
}