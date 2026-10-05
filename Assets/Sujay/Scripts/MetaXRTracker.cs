using UnityEngine;

public class MetaXRTracker : MonoBehaviour
{
    public static MetaXRTracker Instance { get; private set; }

    [SerializeField] private Transform headTransform;
    [SerializeField] private Transform leftHandTransform;
    [SerializeField] private Transform rightHandTransform;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public Transform GetHeadTransform() => headTransform;
    public Transform GetLeftHandTransform() => leftHandTransform;
    public Transform GetRightHandTransform() => rightHandTransform;
}
