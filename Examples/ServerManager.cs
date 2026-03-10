using Riptide;
using Riptide.Transports.TlsTcp;
using Riptide.Utils;
using System;
using UnityEngine;

[HideMonoScript]
public class ServerManager : MonoBehaviour
{
    [field: SerializeField, Tooltip("Port the server will listen to for connections."), Header("Configuration")]
    public ushort PORT { get; private set; } = 7777;

    [field: SerializeField, Tooltip("Maximum number of connected clients.")]
    public ushort MAX_CLIENTCOUNT { get; private set; } = 10;

    [SerializeField]
    private bool autoStartServer = false;

    Server server;

    public void StartTlsTcpServer()
    {
        if (!Application.isPlaying) { Debug.LogError("Cannot run outside of playmode."); return; }

        var transport = new TcpServer();
        transport.Initialize(Application.dataPath);

        if (!transport.CertificateValidated)
        {
            Debug.Log("Populate your certificate configuration file first.");
            return;
        }

        server = new Server(transport, logName: "Tls Server");
        server.Start(PORT, MAX_CLIENTCOUNT);

        server.ClientConnected += OnClientConnected;
        server.ClientDisconnected += OnClientDisconnected;
    }

    private void StartUdpServer()
    {
        if (!Application.isPlaying) { Debug.LogError("Cannot run outside of playmode."); return; }

        server = new Server();
        server.Start(PORT, MAX_CLIENTCOUNT);

        server.ClientConnected += OnClientConnected;
        server.ClientDisconnected += OnClientDisconnected;
    }

    public void StopServer()
    {
        if (!Application.isPlaying) { Debug.LogError("Cannot run outside of playmode."); return; }

        if (server.IsRunning)
        {
            server.ClientConnected -= OnClientConnected;
            server.ClientDisconnected -= OnClientDisconnected;
            server.Stop();
        }
        else
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
        RiptideLogger.Initialize(logMethod: Debug.Log, true);
    }

    private void Start()
    {
        if (autoStartServer)
            StartTlsTcpServer();
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
