using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Lobby helpers for Keeper Clash: an offline Practice host (no Relay needed), leaving a match,
/// and the room code text copied from the keypad so the scoreboard can show it.
/// </summary>
public class KeeperLobby : MonoBehaviour
{
    public static KeeperLobby Instance { get; private set; }

    public RelayNetworkButtons keypad;

    [Header("Inspector triggers (tick during Play mode)")]
    public bool practice;
    public bool leave;

    /// <summary>The keypad's info text with the room code, flattened to one line ("" if none).</summary>
    public string RoomInfo
    {
        get
        {
            if (keypad == null || keypad.infoText == null) return "";
            string t = keypad.infoText.text;
            return t.Contains("Code") ? t.Replace("Hosting", "").Replace("\n", " ").Trim() : "";
        }
    }

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (keypad == null) keypad = FindFirstObjectByType<RelayNetworkButtons>();
    }

    void Update()
    {
        if (practice) { practice = false; StartPractice(); }
        if (leave) { leave = false; Leave(); }
    }

    /// <summary>Starts a local host with no internet; the bot keeps the other goal.</summary>
    public void StartPractice()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.IsListening) return;
        UnityTransport transport = nm.GetComponent<UnityTransport>();
        if (transport != null) transport.SetConnectionData("127.0.0.1", 7777);
        nm.StartHost();
    }

    public void Leave()
    {
        if (keypad != null) keypad.Disconnect();
        else if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
    }
}
