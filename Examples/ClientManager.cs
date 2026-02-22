using UnityEngine;
using Riptide;
using Riptide.Transports.TlsTcp;
using System.Threading;
using System.Threading.Tasks;
using System;

public class ClientManager : MonoBehaviour
{
    [SerializeField]
    public string addressPort = "127.0.0.1:7777";

    private Client client;
    private volatile bool isConnecting;
    private volatile bool isConnected;
    private SynchronizationContext unityCtx;

    private void Awake()
    {
        unityCtx = SynchronizationContext.Current;

        TcpClient clientTransport = new TcpClient { ValidateServerCertificate = true, CheckCertificateRevocation = true }; // Development environment = false, Production = true

        client = new Client(clientTransport);
    }

    private void FixedUpdate()
    {
        if (client == null) return;

        if (isConnecting || !isConnected) return;

        client.Update();
    }

    private void OnDestroy()
    {
        client.ClientDisconnected -= OnClientDisconnected;
        client.ClientConnected -= OnClientConnected;
    }

    private void OnClientConnected(object sender, ClientConnectedEventArgs e)
    {
        // Called when another client connects to the server (Ie Player 2)
    }

    private void OnClientDisconnected(object sender, ClientDisconnectedEventArgs e)
    {
        // Called when another client disconnects from the server (Ie Player 2)
    }

    private void Disconnected(object sender, DisconnectedEventArgs e)
    {
        // Called when the local player disconnects from the server.
        isConnected = false;
    }

    public void ConnectTLSClientAsync()
    {
        if (!Application.isPlaying) { Debug.LogError("Cannot run outside of playmode."); return; }

        if (isConnecting || isConnected)
        {
            return;
        }

        isConnecting = true;

        Task.Run(() =>
        {
            bool ok = false;
            Exception err = null;

            try
            {
                ok = client.Connect(addressPort);
            }
            catch (Exception e)
            {
                err = e;
            }

            unityCtx.Post(_ =>
            {
                isConnecting = false;

                if(err != null)
                {
                    Debug.LogException(err);
                    isConnected = false;
                    return;
                }

                isConnected = ok;
                client.ClientConnected += OnClientConnected;
                client.ClientDisconnected += OnClientDisconnected;
                client.Disconnected += Disconnected;
            }, null);
        });
    }

    private void ConnectUdpClient()
    {
        if (!Application.isPlaying) { Debug.LogError("Cannot run outside of playmode."); return; }

        client = new Client();
        client.Connect(addressPort);
    }

    public void DisconnectClient()
    {
        if (!Application.isPlaying) { Debug.LogError("Cannot run outside of playmode."); return; }

        if(!client.IsConnected) { Debug.LogError("No active connection to disconnect."); return; }

        client.Disconnect();
    }
}

