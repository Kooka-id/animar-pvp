using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using TMPro;

public class NetworkUI : MonoBehaviour
{
    public Button hostButton;
    public Button clientButton;
    public TMP_Text roleText;
    public TMP_Text statusText;
    public TMP_InputField ipInput; // input IP buat client

    private void Start()
    {
        hostButton.onClick.AddListener(() =>
        {
            if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient)
            {
                NetworkManager.Singleton.StartHost();
                roleText.text = "You are HOST";
                statusText.text = "Clients Connected: 0";
                Debug.Log("[UI] Host started");
            }
        });

        clientButton.onClick.AddListener(() =>
        {
            if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient)
            {
                // ambil komponen UnityTransport
                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

                // kalau input kosong → fallback ke localhost
                string ip = string.IsNullOrEmpty(ipInput.text) ? "127.0.0.1" : ipInput.text;
                transport.ConnectionData.Address = ip;
                transport.ConnectionData.Port = 7777; // pastikan port sama

                NetworkManager.Singleton.StartClient();
                roleText.text = "You are CLIENT";
                statusText.text = $"Connecting to {ip}...";
                Debug.Log($"[UI] Client started, connecting to {ip}");
            }
        });

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton.IsServer)
        {
            int clientCount = NetworkManager.Singleton.ConnectedClientsIds.Count - 1; 
            statusText.text = $"Clients Connected: {clientCount}";
            Debug.Log($"[SERVER] Client connected: {clientId}");
        }
        else
        {
            statusText.text = "Connected to Host";
            Debug.Log($"[CLIENT] Connected to server with ID: {clientId}");
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton.IsServer)
        {
            int clientCount = NetworkManager.Singleton.ConnectedClientsIds.Count - 1;
            statusText.text = $"Clients Connected: {clientCount}";
            Debug.Log($"[SERVER] Client disconnected: {clientId}");
        }
        else
        {
            statusText.text = "Disconnected";
            Debug.Log($"[CLIENT] Disconnected from server (ID: {clientId})");
        }
    }
}
