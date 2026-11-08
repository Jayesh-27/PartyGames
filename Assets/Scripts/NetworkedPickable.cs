using Oculus.Interaction;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Put on every grabbable networked item (stone, rope, wood, slingshot).
/// - The player who grabs it becomes the owner (so the owner-authoritative NetworkTransform follows their hand).
/// - Non-owners don't simulate gravity on their copy (the owner's transform is streamed to them).
/// - The grinder consumes items through the server (see ObjectDetection / Grinder).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class NetworkedPickable : NetworkBehaviour
{
    private Grabbable grabbable;
    private Rigidbody rb;
    private bool originalGravity;
    private bool consumed;

    private void Awake()
    {
        grabbable = GetComponentInChildren<Grabbable>(true);
        rb = GetComponent<Rigidbody>();
        if (rb != null) originalGravity = rb.useGravity;
    }

    public override void OnNetworkSpawn()
    {
        if (grabbable != null) grabbable.WhenPointerEventRaised += OnPointerEvent;
        ApplyAuthority();
    }

    public override void OnNetworkDespawn()
    {
        if (grabbable != null) grabbable.WhenPointerEventRaised -= OnPointerEvent;
    }

    public override void OnGainedOwnership() => ApplyAuthority();
    public override void OnLostOwnership() => ApplyAuthority();

    private void ApplyAuthority()
    {
        if (rb != null) rb.useGravity = IsOwner && originalGravity;
    }

    private void OnPointerEvent(PointerEvent evt)
    {
        if (evt.Type == PointerEventType.Select && IsSpawned && !IsOwner)
            RequestOwnershipServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestOwnershipServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (NetworkObject.OwnerClientId != sender)
            NetworkObject.ChangeOwnership(sender);
    }

    /// <summary>Called by the owner when the grinder has finished with this item.</summary>
    public void RequestConsume() => ConsumeServerRpc();

    [ServerRpc]
    private void ConsumeServerRpc()
    {
        if (consumed) return;
        consumed = true;

        Grinder grinder = FindFirstObjectByType<Grinder>();
        if (grinder != null) grinder.onAdded(tag);

        NetworkObject.Despawn(true);
    }
}
