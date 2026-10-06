using System.Net;
using System.Net.Sockets;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkButtons : MonoBehaviour
{
    [Header("Connection")]
    public string serverAddress = "192.168.1.x";   // set this in the Inspector
    public ushort port = 7777;

    [Header("UI (optional)")]
    public TMP_Text infoText;

    [Header("Inspector triggers (tick during Play mode)")]
    public bool host;
    public bool join;
    public bool disconnect;

    UnityTransport Transport => NetworkManager.Singleton.GetComponent<UnityTransport>();

    void Update()
    {
        // Ticking a bool runs the action once, then the bool resets itself
        if (host)       { host = false;       StartHost(); }
        if (join)       { join = false;       StartClient(); }
        if (disconnect) { disconnect = false; Disconnect(); }
    }

    // These stay public, so UI buttons can still call them
    public void StartHost()
    {
        if (!CanStart()) return;

        Transport.SetConnectionData("127.0.0.1", port, "0.0.0.0"); // listen on all interfaces
        NetworkManager.Singleton.StartHost();
        SetText($"Hosting\nIP: {GetLocalIP()}\nPort: {port}");
    }

    public void StartClient()
    {
        if (!CanStart()) return;

        string ip = serverAddress.Trim();
        Transport.SetConnectionData(ip, port);
        NetworkManager.Singleton.StartClient();
        SetText($"Joining {ip}:{port}...");
    }

    public void Disconnect()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();
        SetText("Disconnected");
    }

    bool CanStart()
    {
        if (NetworkManager.Singleton == null)
        {
            SetText("No NetworkManager in the scene");
            return false;
        }
        return !NetworkManager.Singleton.IsListening;
    }

    void SetText(string msg)
    {
        if (infoText != null) infoText.text = msg;
    }

    static string GetLocalIP()
    {
        try
        {
            // Connecting a UDP socket sends no data, it only reveals which local IP would be used
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);
                return ((IPEndPoint)socket.LocalEndPoint).Address.ToString();
            }
        }
        catch
        {
            return "unknown";
        }
    }
}