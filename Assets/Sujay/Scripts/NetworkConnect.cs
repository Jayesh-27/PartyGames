using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using TMPro;

public class NetworkConnect : MonoBehaviour
{
    [SerializeField] private TMP_InputField codeInput;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private int maxConnections = 1;

    private const string RelayJoinCodeKey = "RelayJoinCode";
    private Lobby currentLobby;
    private Coroutine heartbeatCoroutine;
    private bool servicesInitialized;

    private async void Awake()
    {
        await InitializeServicesAsync();
    }

    public async void Create()
    {
        await HostGameAsync();
    }

    public async void Join()
    {
        await JoinGameAsync();
    }

    private async System.Threading.Tasks.Task InitializeServicesAsync()
    {
        if (servicesInitialized)
            return;

        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            servicesInitialized = true;
            SetStatus("Services ready");
        }
        catch (System.Exception ex)
        {
            SetStatus("Service init failed: " + ex.Message);
            Debug.LogException(ex);
        }
    }

    private async System.Threading.Tasks.Task HostGameAsync()
    {
        try
        {
            await InitializeServicesAsync();

            if (NetworkManager.Singleton == null)
            {
                SetStatus("NetworkManager.Singleton is null");
                return;
            }

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                SetStatus("UnityTransport not found on NetworkManager");
                return;
            }

            transport.SetRelayServerData(allocation.ToRelayServerData("dtls"));

            CreateLobbyOptions lobbyOptions = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    { RelayJoinCodeKey, new DataObject(DataObject.VisibilityOptions.Public, joinCode) }
                }
            };

            currentLobby = await LobbyService.Instance.CreateLobbyAsync("PartyGames", maxConnections + 1, lobbyOptions);

            if (heartbeatCoroutine != null)
                StopCoroutine(heartbeatCoroutine);

            heartbeatCoroutine = StartCoroutine(HeartbeatLobbyCoroutine(currentLobby.Id));
            // display lobby code in the input field so others can join
            if (codeInput != null)
            {
                codeInput.text = currentLobby.LobbyCode;
                // optional: make it read-only while hosting
                codeInput.interactable = false;
            }

            if (NetworkManager.Singleton.StartHost())
            {
                SetStatus("Host started. Lobby Code: " + currentLobby.LobbyCode);
                Debug.Log("Relay Join Code: " + joinCode);
                Debug.Log("Lobby Code: " + currentLobby.LobbyCode);
            }
            else
            {
                SetStatus("Failed to start host");
                if (codeInput != null) codeInput.text = string.Empty;
            }
        }
        catch (System.Exception ex)
        {
            SetStatus("Host failed: " + ex.Message);
            Debug.LogException(ex);
        }
    }

    private async System.Threading.Tasks.Task JoinGameAsync()
    {
        try
        {
            await InitializeServicesAsync();

            if (codeInput == null)
            {
                SetStatus("Join code input is not assigned");
                return;
            }

            string lobbyCode = codeInput.text.Trim();
            if (string.IsNullOrEmpty(lobbyCode))
            {
                SetStatus("Enter a lobby code");
                return;
            }

            Lobby lobby = null;
            try
            {
                lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);
            }
            catch (LobbyServiceException lse)
            {
                // Handle case where the same authenticated player is already in the lobby
                if (lse.Message != null && lse.Message.Contains("already a member"))
                {
                    SetStatus("Player is already a member of the lobby — attempting to re-authenticate and retry");
                    Debug.Log("Join conflict: player already in lobby (code: " + lobbyCode + ") — reauthenticating and retrying");

                    try
                    {
                        // Sign out and sign in anonymously to obtain a new player id for this instance
                        try
                        {
                            AuthenticationService.Instance.SignOut();
                        }
                        catch (System.Exception signoutEx)
                        {
                            Debug.LogWarning("SignOut failed: " + signoutEx.Message);
                        }

                        await AuthenticationService.Instance.SignInAnonymouslyAsync();

                        // retry join once
                        lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);
                    }
                    catch (System.Exception retryEx)
                    {
                        SetStatus("Retry join failed: " + retryEx.Message);
                        Debug.LogException(retryEx);
                        return;
                    }
                }

                // rethrow other lobby errors so outer catch logs them
                else
                {
                    throw;
                }
            }

            if (lobby == null || lobby.Data == null || !lobby.Data.ContainsKey(RelayJoinCodeKey))
            {
                SetStatus("Lobby does not contain relay code");
                return;
            }

            string relayJoinCode = lobby.Data[RelayJoinCodeKey].Value;
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);

            if (NetworkManager.Singleton == null)
            {
                SetStatus("NetworkManager.Singleton is null");
                return;
            }

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                SetStatus("UnityTransport not found on NetworkManager");
                return;
            }

            transport.SetRelayServerData(joinAllocation.ToRelayServerData("dtls"));

            if (NetworkManager.Singleton.StartClient())
            {
                currentLobby = lobby;
                SetStatus("Joining lobby: " + lobbyCode);
            }
            else
            {
                SetStatus("Failed to start client");
            }
        }
        catch (System.Exception ex)
        {
            SetStatus("Join failed: " + ex.Message);
            Debug.LogException(ex);
        }
    }

    private IEnumerator HeartbeatLobbyCoroutine(string lobbyId)
    {
        var wait = new WaitForSeconds(15f);
        while (true)
        {
            yield return wait;

            if (!string.IsNullOrEmpty(lobbyId))
            {
                // call a safe fire-and-forget wrapper to ensure exceptions are caught and logged
                SafeSendHeartbeatPing(lobbyId);
            }
        }
    }

    // Fire-and-forget wrapper that catches exceptions from the async heartbeat call
    private async void SafeSendHeartbeatPing(string lobbyId)
    {
        try
        {
            await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            SetStatus("Heartbeat failed: " + ex.Message);
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }

        Debug.Log(message);
    }
}
