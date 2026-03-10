using Riptide;
using Riptide.Transports.TlsTcp;
using Riptide.Utils;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using UnityEngine;


public class ServerManager : MonoBehaviour
{
    [field: SerializeField, Tooltip("Port the server will listen to for connections."), Header("Configuration")]
    public ushort PORT { get; private set; } = 7777;

    [field: SerializeField, Tooltip("Maximum number of connected clients.")]
    public ushort MAX_CLIENTCOUNT { get; private set; } = 10;

    Server server;
    TcpServer transport;
    public TcpServer GetTransport => transport;

    public void StartTlsTcpServer()
    {
        if(!Application.isPlaying) { Debug.LogError("Cannot run outside of playmode."); return; }

        transport = new TcpServer();
        transport.Initialize(Application.dataPath);

        if (transport.CertificateValidated)
        {
            server = new Server(transport, logName: "TLS Server");
            server.Start(PORT, MAX_CLIENTCOUNT);
            server.ClientConnected += OnClientConnected;
            server.ClientDisconnected += OnClientDisconnected;
        }
    }

    public void StopServer()
    {
        if (!Application.isPlaying) { Debug.LogError("Cannot run outside of playmode."); return; }

        if (server.IsRunning)
        {
            server.ClientConnected -= OnClientConnected;
            server.ClientDisconnected -= OnClientDisconnected;
            server.Stop();
        } else
        {
            Debug.LogError("Server is currently not running.");
        }
    }

    private void OnConnectionFailed(object sender, ConnectionFailedEventArgs e)
    {
        Debug.LogError($"Connection failure reason: {e.Reason}");
    }

    private void OnClientConnected(object sender, ServerConnectedEventArgs e)
    {
        Debug.Log($"{e.Client} has connected.");
    }

    private void OnClientDisconnected(object sender, ServerDisconnectedEventArgs e)
    {
        Debug.Log($"{e.Client} has disconnected.");
    }

    private void Awake()
    {
        RiptideLogger.Initialize(
            debugMethod: Debug.Log,
            infoMethod: Debug.Log,
            warningMethod: Debug.LogWarning,
            errorMethod: Debug.LogError,
            true);
    }

    private void FixedUpdate()
    {
        if (server == null) return;

        if (server.IsRunning)
        {
            server.Update();
        }
    }
}
