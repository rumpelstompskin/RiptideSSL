using Riptide;
using Riptide.Transports.TlsTcp;
using Riptide.Utils;
using Sirenix.OdinInspector;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using UnityEngine;

[HideMonoScript]
public class ServerManager : MonoBehaviour
{
    [field: SerializeField, Tooltip("Port the server will listen to for connections."), Header("Configuration")]
    public ushort PORT { get; private set; } = 7777;

    [field: SerializeField, Tooltip("Maximum number of connected clients.")]
    public ushort MAX_CLIENTCOUNT { get; private set; } = 10;

    [field: SerializeField, Tooltip("Filename of your certificate file.")]
    public string CERT_NAME { get; private set; } = string.Empty;

    [field: SerializeField, Tooltip("Password of your certificate file.")]
    public string CERT_PW { get; private set; } = string.Empty;

    private const string CONFIG_PATH = "certs/config.json";

    [SerializeField]
    private bool autoStartServer = false;

    Server server;
    [field: SerializeField]
    public X509Certificate2 Certificate { get; private set; }
    bool certificateValidated = false;

    [Serializable] private class CertConfig { public string certificateFile = ""; public string password = ""; }

    [Button, BoxGroup("Controls"), HorizontalGroup("Controls/Buttons")]
    public void StartTlsTcpServer()
    {
        if(!Application.isPlaying) { Debug.LogError("Cannot run outside of playmode."); return; }

        if(ValidateCertificateConfig(Path.Combine(Application.dataPath, CONFIG_PATH)) == false)
        { 
            Debug.Log("Populate your certificate configuration file first.");
            return;
        }

        var transport = new TcpServer
        {
            ServerCertificate = Certificate
        };

        server = new Server(transport, logName: "Tls Server");


        server.Start(PORT, MAX_CLIENTCOUNT);

        server.ClientConnected += OnClientConnected;
        server.ClientDisconnected += OnClientDisconnected;
    }

    [Button, HorizontalGroup("Controls/Buttons")]
    private void StartUdpServer()
    {
        if (!Application.isPlaying) { Debug.LogError("Cannot run outside of playmode."); return; }

        server = new Server();

        server.Start(PORT, MAX_CLIENTCOUNT);

        server.ClientConnected += OnClientConnected;
        server.ClientDisconnected += OnClientDisconnected;
    }

    [Button, HorizontalGroup("Controls/Buttons")]
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
        GetCertConfig();

        RiptideLogger.Initialize(logMethod: Debug.Log, true);
    }

    private void Start()
    {
        if(certificateValidated && autoStartServer)
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

    private void GetCertConfig()
    {
        string certDir = Path.Combine(Application.dataPath, "certs/");

        if (!Directory.Exists(certDir))
        {
            Debug.LogWarning("Certificate directory not found. Creating.");
            Directory.CreateDirectory(certDir);
        }

        string confPath = Path.Combine(Application.dataPath, CONFIG_PATH);

        if (!File.Exists(confPath))
        {
            Debug.LogWarning("Certificate configuration file not found. Creating.");
            var config = new CertConfig
            {
                certificateFile = "",
                password = ""
            };

            string json = JsonUtility.ToJson(config, true);
            File.WriteAllText(confPath, json);

            Debug.LogError($"Certificate directory and configuration file created in {certDir}.");
            return;
        } 
        else
        {
            certificateValidated = ValidateCertificateConfig(confPath);
        }
    }

    private bool ValidateCertificateConfig(string confPath)
    {
        string json = File.ReadAllText(confPath);
        var certConfig = JsonUtility.FromJson<CertConfig>(json);
        CERT_NAME = certConfig.certificateFile;
        CERT_PW = certConfig.password;

        try
        {
            var pfxPath = Path.Combine(Application.dataPath, $"certs/{CERT_NAME}.pfx");
            Certificate = new X509Certificate2(pfxPath, CERT_PW);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Invalid certificate. {e.Message}");
            return false;
        }
    }
}
