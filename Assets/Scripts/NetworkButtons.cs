using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;

public class RelayNetworkButtons : MonoBehaviour
{
    const int CodeLength = 6;

    [Header("Join code (the keypad fills this, you can also type it here)")]
    public string joinCode;

    [Header("Keypad")]
    public Button[] digitButtons = new Button[10];   // element 0 = digit 0 ... element 9 = digit 9
    public Button backspaceButton;
    public TMP_Text codeText;                        // shows the digits entered so far

    [Header("Settings")]
    public int maxPlayers = 4;                       // total, including the host

    [Header("UI (optional)")]
    public TMP_Text infoText;

    [Header("Inspector triggers (tick during Play mode)")]
    public bool host;
    public bool join;
    public bool disconnect;

    UnityTransport Transport => NetworkManager.Singleton.GetComponent<UnityTransport>();
    bool busy;
    string lobbyId;
    float heartbeatTimer;

    void Awake()
    {
        for (int i = 0; i < digitButtons.Length; i++)
        {
            if (digitButtons[i] == null) continue;
            int digit = i;                           // copy for the closure
            digitButtons[i].onClick.AddListener(() => AddDigit(digit));
        }

        if (backspaceButton != null)
            backspaceButton.onClick.AddListener(Backspace);

        RefreshCodeText();
    }

    void Update()
    {
        if (host)       { host = false;       StartHost(); }
        if (join)       { join = false;       StartClient(); }
        if (disconnect) { disconnect = false; Disconnect(); }

        // Lobbies are deleted if they get no heartbeat for about 30 seconds
        if (lobbyId != null)
        {
            heartbeatTimer -= Time.deltaTime;
            if (heartbeatTimer <= 0f)
            {
                heartbeatTimer = 15f;
                SendHeartbeat(lobbyId);
            }
        }
    }

    // ---------------- Keypad ----------------

    public void AddDigit(int digit)
    {
        if (joinCode.Length >= CodeLength) return;
        joinCode += digit.ToString();
        RefreshCodeText();
    }

    public void Backspace()
    {
        if (joinCode.Length == 0) return;
        joinCode = joinCode.Substring(0, joinCode.Length - 1);
        RefreshCodeText();
    }

    void RefreshCodeText()
    {
        if (codeText == null) return;
        codeText.text = joinCode + new string('_', Mathf.Max(0, CodeLength - joinCode.Length));
    }

    // ---------------- Host ----------------

    public async void StartHost()
    {
        if (!CanStart()) return;
        busy = true;
        try
        {
            SetText("Connecting to services...");
            await SignIn();

            Allocation a = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            string relayCode = await RelayService.Instance.GetJoinCodeAsync(a.AllocationId);

            // Pick a random 6-digit code that no other lobby is using
            string numericCode = null;
            for (int i = 0; i < 10; i++)
            {
                string candidate = Random.Range(0, 1000000).ToString("D6");
                if (await FindLobbyByCode(candidate) == null)
                {
                    numericCode = candidate;
                    break;
                }
            }
            if (numericCode == null)
            {
                SetText("Could not create a unique code, try again");
                busy = false;
                return;
            }

            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    { "code",  new DataObject(DataObject.VisibilityOptions.Public, numericCode, DataObject.IndexOptions.S1) },
                    { "relay", new DataObject(DataObject.VisibilityOptions.Public, relayCode) }
                }
            };
            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync("Room " + numericCode, maxPlayers, options);
            lobbyId = lobby.Id;
            heartbeatTimer = 15f;

            SetRelay(a);
            NetworkManager.Singleton.StartHost();

            SetText($"Hosting\nCode: {numericCode}");
            Debug.Log("Join code: " + numericCode);
        }
        catch (System.Exception e)
        {
            SetText("Host failed: " + e.Message);
            Debug.LogException(e);
        }
        busy = false;
    }

    // ---------------- Client ----------------

    public async void StartClient()
    {
        if (!CanStart()) return;

        if (joinCode.Length != CodeLength)
        {
            SetText($"Enter all {CodeLength} digits first");
            return;
        }

        busy = true;
        try
        {
            SetText("Connecting to services...");
            await SignIn();

            Lobby lobby = await FindLobbyByCode(joinCode);
            if (lobby == null)
            {
                SetText("Code not found");
                busy = false;
                return;
            }

            string relayCode = lobby.Data["relay"].Value;
            JoinAllocation a = await RelayService.Instance.JoinAllocationAsync(relayCode);

            SetRelay(a);
            NetworkManager.Singleton.StartClient();

            SetText("Joining " + joinCode + "...");
        }
        catch (System.Exception e)
        {
            SetText("Join failed: " + e.Message);
            Debug.LogException(e);
        }
        busy = false;
    }

    // ---------------- Disconnect ----------------

    public void Disconnect()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();

        DeleteLobby();
        SetText("Disconnected");
    }

    void OnDestroy()
    {
        DeleteLobby();
    }

    async void DeleteLobby()
    {
        if (lobbyId == null) return;
        string id = lobbyId;
        lobbyId = null;
        try { await LobbyService.Instance.DeleteLobbyAsync(id); }
        catch { /* already gone, ignore */ }
    }

    async void SendHeartbeat(string id)
    {
        try { await LobbyService.Instance.SendHeartbeatPingAsync(id); }
        catch { /* ignore, next ping will retry */ }
    }

    // ---------------- Helpers ----------------

    static async Task<Lobby> FindLobbyByCode(string code)
    {
        var options = new QueryLobbiesOptions
        {
            Count = 1,
            Filters = new List<QueryFilter>
            {
                new QueryFilter(QueryFilter.FieldOptions.S1, code, QueryFilter.OpOptions.EQ)
            }
        };
        QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);
        return response.Results.Count > 0 ? response.Results[0] : null;
    }

    void SetRelay(Allocation a)
    {
        var e = GetEndpoint(a.ServerEndpoints);
        Transport.SetRelayServerData(new RelayServerData(
            e.Host, (ushort)e.Port, a.AllocationIdBytes,
            a.ConnectionData, a.ConnectionData, a.Key, e.Secure));
    }

    void SetRelay(JoinAllocation a)
    {
        var e = GetEndpoint(a.ServerEndpoints);
        Transport.SetRelayServerData(new RelayServerData(
            e.Host, (ushort)e.Port, a.AllocationIdBytes,
            a.ConnectionData, a.HostConnectionData, a.Key, e.Secure));
    }

    static RelayServerEndpoint GetEndpoint(List<RelayServerEndpoint> endpoints)
    {
        foreach (var e in endpoints)
            if (e.ConnectionType == "dtls") return e;
        return endpoints[0];
    }

    static async Task SignIn()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    bool CanStart()
    {
        if (NetworkManager.Singleton == null)
        {
            SetText("No NetworkManager in the scene");
            return false;
        }
        return !busy && !NetworkManager.Singleton.IsListening;
    }

    void SetText(string msg)
    {
        if (infoText != null) infoText.text = msg;
    }
}