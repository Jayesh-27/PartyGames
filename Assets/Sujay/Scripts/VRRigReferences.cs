using UnityEngine;

public class VRRigReferences : MonoBehaviour
{
    public static VRRigReferences Singleton;

    public Transform root;
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;

    private void Awake()
    {
        if (Singleton != null && Singleton != this)
        {
            Destroy(gameObject);
            return;
        }

        Singleton = this;

        // Provide fallback transforms when running without a VR headset to avoid null refs
        if (head == null) head = CreateFallback("Head_Fallback");
        if (leftHand == null) leftHand = CreateFallback("LeftHand_Fallback");
        if (rightHand == null) rightHand = CreateFallback("RightHand_Fallback");
    }

    private void OnDestroy()
    {
        if (Singleton == this) Singleton = null;
    }

    private Transform CreateFallback(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        return go.transform;
    }
}
