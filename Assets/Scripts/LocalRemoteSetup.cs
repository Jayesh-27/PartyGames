using Unity.Netcode;
using UnityEngine;

public class LocalRemoteSetup : NetworkBehaviour
{
    public GameObject[] localOnly;   // e.g. ownership-only scripts or objects
    public GameObject[] remoteOnly;  // e.g. a visible head mesh if you hide your own

    public override void OnNetworkSpawn()
    {
        foreach (var go in localOnly)  go.SetActive(IsOwner);
        foreach (var go in remoteOnly) go.SetActive(!IsOwner);
    }
}