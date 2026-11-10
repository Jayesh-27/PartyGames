using System.Collections.Generic;
using System.Threading.Tasks;
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
using UnityEngine.SceneManagement;

/// <summary>
/// Hosting, joining with a 6-digit room code (Unity Relay + Lobby), offline play against the bot, and leaving.
/// Lives on the NetworkManager object, so the room stays alive while scenes change.
/// </summary>
public class PartyConnection : MonoBehaviour
{
    public const int CodeLength = 6;
    public static PartyConnection Instance { get; private set; }

    [Tooltip("Total players in a room, including the host")]
    public int maxPlayers = 2;
    public string lobbyScene = "Lobby";

    [Header("Inspector triggers (tick during Play mode)")]
    public bool host;
    public bool vsBot;
    public bool leave;

    /// <summary>Digits typed on the join keypad.</summary>
    public string JoinCode { get; private set; } = "";
    /// <summary>The code friends type to join; empty when offline or not hosting.</summary>
    public string RoomCode { get; private set; } = "";
    /// <summary>Last thing that happened, for the lobby screen.</summary>
    public string Status { get; private set; } = "";
    public bool Busy { get; private set; }
    public bool Offline { get; private set; }

    public static bool InSession => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    string lobbyId;
    float heartbeatTimer;
    bool hooked;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        DeleteLobby();
    }

    void Update()
    {
        if (host) { host = false; StartHost(); }
        if (vsBot) { vsBot = false; StartVsBot(); }
        if (leave) { leave = false; Leave(); }

        NetworkManager nm = NetworkManager.Singleton;
        if (!hooked && nm != null)
        {
            hooked = true;
            nm.OnClientStopped += OnStopped;
            nm.OnServerStopped += OnStopped;
        }

        // the room is deleted when it gets no heartbeat for about 30 seconds
        if (lobbyId != null)
        {
            heartbeatTimer -= Time.unscaledDeltaTime;
            if (heartbeatTimer <= 0f)
            {
                heartbeatTimer = 15f;
                SendHeartbeat(lobbyId);
            }
        }
    }

    void OnStopped(bool wasHost)
    {
        DeleteLobby();
        RoomCode = "";
        Offline = false;
        if (Status.Length == 0 || Status.StartsWith("Room") || Status.StartsWith("Joined")) Status = "Disconnected";
        if (SceneManager.GetActiveScene().name != lobbyScene) SceneManager.LoadScene(lobbyScene);
    }

    // ---------------- keypad ----------------

    public void AddDigit(int digit)
    {
        if (JoinCode.Length >= CodeLength) return;
        JoinCode += digit.ToString();
    }

    public void Backspace()
    {
        if (JoinCode.Length > 0) JoinCode = JoinCode.Substring(0, JoinCode.Length - 1);
    }

    public void ClearCode() => JoinCode = "";

    // ---------------- host ----------------

    public async void StartHost()
    {
        if (!CanStart()) return;
        Busy = true;
        try
        {
            Status = "Creating room...";
            await SignIn();

            Allocation a = await RelayService.Instance.CreateAllocationAsync(Mathf.Max(1, maxPlayers - 1));
            string relayCode = await RelayService.Instance.GetJoinCodeAsync(a.AllocationId);

            string numericCode = null;
            for (int i = 0; i < 10 && numericCode == null; i++)
            {
                string candidate = Random.Range(0, 1000000).ToString("D6");
                if (await FindLobbyByCode(candidate) == null) numericCode = candidate;
            }
            if (numericCode == null)
            {
                Status = "Could not create a room, try again";
                Busy = false;
                return;
            }

            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    { "code", new DataObject(DataObject.VisibilityOptions.Public, numericCode, DataObject.IndexOptions.S1) },
                    { "relay", new DataObject(DataObject.VisibilityOptions.Public, relayCode) }
                }
            };
            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync("Room " + numericCode, maxPlayers, options);
            lobbyId = lobby.Id;
            heartbeatTimer = 15f;

            var e = GetEndpoint(a.ServerEndpoints);
            Transport.SetRelayServerData(new RelayServerData(e.Host, (ushort)e.Port, a.AllocationIdBytes,
                a.ConnectionData, a.ConnectionData, a.Key, e.Secure));

            Offline = false;
            RoomCode = numericCode;
            if (NetworkManager.Singleton.StartHost()) Status = "Room " + numericCode;
            else { Status = "Could not start hosting"; RoomCode = ""; DeleteLobby(); }
        }
        catch (System.Exception e)
        {
            Status = "Host failed: " + Short(e.Message);
            Debug.LogException(e);
        }
        Busy = false;
    }

    // ---------------- join ----------------

    public async void StartClient()
    {
        if (!CanStart()) return;
        if (JoinCode.Length != CodeLength)
        {
            Status = $"Enter all {CodeLength} digits";
            return;
        }

        Busy = true;
        try
        {
            Status = "Looking for room " + JoinCode + "...";
            await SignIn();

            Lobby lobby = await FindLobbyByCode(JoinCode);
            if (lobby == null)
            {
                Status = "Room " + JoinCode + " not found";
                Busy = false;
                return;
            }

            JoinAllocation a = await RelayService.Instance.JoinAllocationAsync(lobby.Data["relay"].Value);
            var e = GetEndpoint(a.ServerEndpoints);
            Transport.SetRelayServerData(new RelayServerData(e.Host, (ushort)e.Port, a.AllocationIdBytes,
                a.ConnectionData, a.HostConnectionData, a.Key, e.Secure));

            Offline = false;
            RoomCode = JoinCode;
            if (NetworkManager.Singleton.StartClient()) Status = "Joined room " + JoinCode;
            else { Status = "Could not join"; RoomCode = ""; }
        }
        catch (System.Exception e)
        {
            Status = "Join failed: " + Short(e.Message);
            Debug.LogException(e);
        }
        Busy = false;
    }

    // ---------------- bot ----------------

    /// <summary>Starts a local host with no internet. The bot takes the Red side.</summary>
    public void StartVsBot()
    {
        if (!CanStart()) return;
        Transport.SetConnectionData("127.0.0.1", 7777);
        Offline = true;
        RoomCode = "";
        Status = NetworkManager.Singleton.StartHost() ? "Playing against the bot" : "Could not start";
        if (!NetworkManager.Singleton.IsListening) Offline = false;
    }

    // ---------------- leave ----------------

    public void Leave()
    {
        Status = "";
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && (nm.IsListening || nm.ShutdownInProgress)) nm.Shutdown();
        else if (SceneManager.GetActiveScene().name != lobbyScene) SceneManager.LoadScene(lobbyScene);
        DeleteLobby();
        RoomCode = "";
    }

    async void DeleteLobby()
    {
        if (lobbyId == null) return;
        string id = lobbyId;
        lobbyId = null;
        try { await LobbyService.Instance.DeleteLobbyAsync(id); }
        catch { /* already gone */ }
    }

    async void SendHeartbeat(string id)
    {
        try { await LobbyService.Instance.SendHeartbeatPingAsync(id); }
        catch { /* the next ping retries */ }
    }

    // ---------------- helpers ----------------

    UnityTransport Transport => NetworkManager.Singleton.GetComponent<UnityTransport>();

    static async Task<Lobby> FindLobbyByCode(string code)
    {
        var options = new QueryLobbiesOptions
        {
            Count = 1,
            Filters = new List<QueryFilter> { new QueryFilter(QueryFilter.FieldOptions.S1, code, QueryFilter.OpOptions.EQ) }
        };
        QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);
        return response.Results.Count > 0 ? response.Results[0] : null;
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
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Status = "No NetworkManager";
            return false;
        }
        return !Busy && !nm.IsListening && !nm.ShutdownInProgress;
    }

    static string Short(string s) => string.IsNullOrEmpty(s) ? "" : s.Length > 60 ? s.Substring(0, 60) + "..." : s;
}
