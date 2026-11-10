using UnityEngine;

/// <summary>
/// Sits on the NetworkManager object in the Lobby scene. Keeps that object alive across scenes and removes
/// the copy that appears whenever the Lobby scene is loaded again.
/// </summary>
[DefaultExecutionOrder(-32000)]
public class PartyCore : MonoBehaviour
{
    public static PartyCore Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
