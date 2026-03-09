// This file is provided under The MIT License as part of RiptideNetworking.
// Copyright (c) Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub:
// https://github.com/RiptideNetworking/Riptide/blob/main/LICENSE.md

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Riptide.Transports.TlsTcp
{
    /// <summary></summary>
    [Serializable]
    public class CertConfig
    {
        /// <summary></summary>
        public string certificateFile = "";
        /// <summary></summary>
        public string password = "";
    }

    /// <summary>A server which can accept TLS-wrapped connections from <see cref="TcpClient"/>s.</summary>
    public class TcpServer : TcpPeer, IServer
    {
        /// <inheritdoc/>
        public event EventHandler<ConnectedEventArgs> Connected;
        /// <inheritdoc/>
        public event EventHandler<DataReceivedEventArgs> DataReceived;

        /// <inheritdoc/>
        public ushort Port { get; private set; }
        /// <summary>The maximum number of pending connections to allow at any given time.</summary>
        public int MaxPendingConnections { get; private set; } = 5;

        /// <summary></summary>
        public string CERT_NAME { get; set; } = string.Empty;

        /// <summary></summary>
        public string CERT_PW { get; set; } = string.Empty;

        /// <summary></summary>
        public string CONFIG_PATH { get; } = "certs/config.json";

        /// <summary>Server certificate used for TLS.</summary>
        public X509Certificate2 ServerCertificate { get; set; }

        /// <summary></summary>
        public bool CertificateValidated { get; set; } = false;

        /// <summary>Which TLS protocol versions to allow.</summary>
        public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.Tls12;

        /// <summary>Whether to require a client certificate (mTLS).</summary>
        public bool RequireClientCertificate { get; set; } = false;

        /// <summary>Optional validation callback for client certificates (mTLS).</summary>
        public RemoteCertificateValidationCallback ClientCertificateValidationCallback { get; set; }

        /// <summary>Whether or not the server is running.</summary>
        private bool isRunning = false;
        /// <summary>The currently open connections, accessible by their endpoints.</summary>
        private Dictionary<IPEndPoint, TcpConnection> connections;
        /// <summary>Connections that have been closed and need to be removed from <see cref="connections"/>.</summary>
        private readonly List<IPEndPoint> closedConnections = new List<IPEndPoint>();
        /// <summary>The IP address to bind the socket to.</summary>
        private readonly IPAddress listenAddress;

        private readonly ConcurrentQueue<TcpConnection> authenticatedConnections = new ConcurrentQueue<TcpConnection>();

        /// <inheritdoc/>
        public TcpServer(int socketBufferSize = DefaultSocketBufferSize) : this(IPAddress.IPv6Any, socketBufferSize) { }

        /// <summary>Initializes the transport, binding the socket to a specific IP address.</summary>
        /// <param name="listenAddress">The IP address to bind the socket to.</param>
        /// <param name="socketBufferSize">How big the socket's send and receive buffers should be.</param>
        public TcpServer(IPAddress listenAddress, int socketBufferSize = DefaultSocketBufferSize) : base(socketBufferSize)
        {
            this.listenAddress = listenAddress;
        }

        /// <inheritdoc/>
        public void Start(ushort port)
        {
            if (ServerCertificate == null)
                throw new InvalidOperationException("ServerCertificate must be set before starting the TLS server.");
            

            Port = port;
            connections = new Dictionary<IPEndPoint, TcpConnection>();

            StartListening(port);
        }

        /// <summary></summary>
        public void Initialize()
        {
            string certDir = Path.Combine(Directory.GetCurrentDirectory(), "certs/");

            if (!Directory.Exists(certDir))
            {
                Console.WriteLine("Certificate directory not found. Creating.");
                Directory.CreateDirectory(certDir);
            }

            string confPath = Path.Combine(Directory.GetCurrentDirectory(), CONFIG_PATH);

            if (!File.Exists(confPath))
            {
                Console.WriteLine("Certificate configuration file not found. Creating.");
                var config = new CertConfig
                {
                    certificateFile = "",
                    password = ""
                };

                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(confPath, json);

                Console.WriteLine($"Certificate directory and configuration file created in {certDir}.");
                return;
            }
            else
            {
                CertificateValidated = ValidateCertificateConfig(confPath);
            }
        }

        /// <summary></summary> 
        /// <param name="confPath"></param>
        /// <returns></returns>
        public bool ValidateCertificateConfig(string confPath)
        {
            string json = File.ReadAllText(confPath);
            var certConfig = JsonConvert.DeserializeObject<CertConfig>(json);
            CERT_NAME = certConfig.certificateFile;
            CERT_PW = certConfig.password;

            try
            {
                var pfxPath = Path.Combine(Directory.GetCurrentDirectory(), $"certs/{CERT_NAME}.pfx");
                // Load as bytes so the (byte[], string, flags) overload is used — available in netstandard2.0.
                // EphemeralKeySet (value 32) is defined in netstandard2.1+ / .NET Core 2.1+; cast by value so
                // the code compiles against netstandard2.0 but uses the flag on modern runtimes (Unity 2021+, .NET 6+).
                const X509KeyStorageFlags EphemeralKeySet = (X509KeyStorageFlags)32;
                ServerCertificate = new X509Certificate2(File.ReadAllBytes(pfxPath), CERT_PW,
                    EphemeralKeySet | X509KeyStorageFlags.Exportable);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Invalid certificate. {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        /// <summary>Starts listening for connections on the given port.</summary>
        /// <param name="port">The port to listen on.</param>
        private void StartListening(ushort port)
        {
            if (isRunning)
                StopListening();

            IPEndPoint localEndPoint = new IPEndPoint(listenAddress, port);
            socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                SendBufferSize = socketBufferSize,
                ReceiveBufferSize = socketBufferSize,
                NoDelay = true,
            };
            socket.Bind(localEndPoint);
            socket.Listen(MaxPendingConnections);

            isRunning = true;
        }

        /// <inheritdoc/>
        public void Poll()
        {
            if (!isRunning)
                return;

            Accept();

            // Promote newly authenticated connections on the Poll thread (Unity main thread).
            while (authenticatedConnections.TryDequeue(out TcpConnection c))
            {
                if (!connections.ContainsKey(c.RemoteEndPoint))
                {
                    connections.Add(c.RemoteEndPoint, c);
                    OnConnected(c);
                }
                else
                {
                    c.Close();
                }
            }

            foreach (TcpConnection connection in connections.Values)
                connection.Receive();

            foreach (IPEndPoint endPoint in closedConnections)
                connections.Remove(endPoint);

            closedConnections.Clear();
        }

        /// <summary>Accepts any pending connections and performs TLS handshake on a background task.</summary>
        private void Accept()
        {
            while (socket.Poll(0, SelectMode.SelectRead))
            {
                Socket acceptedSocket = socket.Accept();
                acceptedSocket.NoDelay = true;
                IPEndPoint fromEndPoint = (IPEndPoint)acceptedSocket.RemoteEndPoint;

                if (connections.ContainsKey(fromEndPoint))
                {
                    acceptedSocket.Close();
                    continue;
                }

                // Handshake off-thread so Poll isn't blocked.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var networkStream = new NetworkStream(acceptedSocket, ownsSocket: false);

                        RemoteCertificateValidationCallback cb = (sender, cert, chain, errors) =>
                        {
                            if (!RequireClientCertificate)
                                return true;

                            if (ClientCertificateValidationCallback != null)
                                return ClientCertificateValidationCallback(sender, cert, chain, errors);

                            return errors == SslPolicyErrors.None;
                        };

                        var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false, cb);
                        await sslStream.AuthenticateAsServerAsync(
                            ServerCertificate,
                            clientCertificateRequired: RequireClientCertificate,
                            enabledSslProtocols: EnabledSslProtocols,
                            checkCertificateRevocation: false
                        ).ConfigureAwait(false);

                        var conn = new TcpConnection(acceptedSocket, fromEndPoint, this);
                        conn.SetStream(sslStream);
                        conn.StartReceiving();

                        authenticatedConnections.Enqueue(conn);
                    }
                    catch
                    {
                        try { acceptedSocket.Close(); } catch { }
                    }
                });
            }
        }

        /// <summary>Stops listening for connections.</summary>
        private void StopListening()
        {
            if (!isRunning)
                return;

            isRunning = false;
            socket.Close();
        }

        /// <inheritdoc/>
        public void Close(Connection connection)
        {
            if (connection is TcpConnection tcp && tcp != null)
            {
                tcp.Close();
                closedConnections.Add(tcp.RemoteEndPoint);
            }
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            StopListening();
            if (connections != null)
            {
                foreach (var c in connections.Values)
                    c.Close();
                connections.Clear();
            }
        }

        /// <summary>Invokes the <see cref="Connected"/> event.</summary>
        private void OnConnected(TcpConnection connection)
        {
            Connected?.Invoke(this, new ConnectedEventArgs(connection));
        }

        /// <inheritdoc/>
        protected internal override void OnDataReceived(int amount, TcpConnection fromConnection)
        {
            DataReceived?.Invoke(this, new DataReceivedEventArgs(ReceiveBuffer, amount, fromConnection));
        }

        /// <inheritdoc/>
        protected internal override void OnDisconnected(Connection connection, DisconnectReason reason)
        {
            base.OnDisconnected(connection, reason);

            if (connection is TcpConnection tcp)
                closedConnections.Add(tcp.RemoteEndPoint);
        }
    }
}
