// This file is provided under The MIT License as part of RiptideNetworking.
// Copyright (c) Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub:
// https://github.com/RiptideNetworking/Riptide/blob/main/LICENSE.md

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Riptide.Utils;

namespace Riptide.Transports.TlsTcp
{
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
        public int MaxPendingConnections { get; set; } = 512;
        /// <summary>The maximum number of TLS handshakes to perform concurrently. Limits ThreadPool saturation during connection bursts.</summary>
        public int MaxConcurrentHandshakes { get; set; } = 32;

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
        private readonly ConcurrentQueue<IPEndPoint> closedConnections = new ConcurrentQueue<IPEndPoint>();
        /// <summary>The IP address to bind the socket to.</summary>
        private readonly IPAddress listenAddress;

        private readonly ConcurrentQueue<TcpConnection> authenticatedConnections = new ConcurrentQueue<TcpConnection>();
        private SemaphoreSlim _handshakeSemaphore;

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
            _handshakeSemaphore = new SemaphoreSlim(MaxConcurrentHandshakes, MaxConcurrentHandshakes);

            StartListening(port);
        }

        /// <summary>
        /// Ensures the certs directory and config file exist, then loads the certificate.
        /// Delegates file and certificate work to <see cref="CertLoader"/>.
        /// Writes status messages to <see cref="Console"/>.
        /// </summary>
        /// <param name="basePath">Base directory under which the <c>certs/</c> folder and config file are located.</param>
        public void Initialize(string basePath)
        {
            string certDir  = Path.Combine(basePath, "certs");
            string confPath = Path.Combine(basePath, CONFIG_PATH);

            bool configExisted = CertLoader.EnsureScaffold(certDir, confPath);
            if (!configExisted)
            {
                RiptideLogger.Log(LogType.Info, "TLS Server", $"Certificate directory and configuration file created in {certDir}. Fill in {confPath} and restart.");
                return;
            }

            CertificateValidated = ValidateCertificateConfig(confPath);
        }

        /// <summary>Loads and validates the certificate described by the config file at <paramref name="confPath"/>.</summary>
        /// <param name="confPath">Absolute path to the config JSON file.</param>
        /// <returns><c>true</c> if the certificate was loaded successfully; otherwise <c>false</c>.</returns>
        public bool ValidateCertificateConfig(string confPath)
        {
            string certDir = Path.GetDirectoryName(confPath);
            bool ok = CertLoader.LoadFromConfig(confPath, certDir, out var cert, out string name, out string pw, out string error);
            if (ok)
            {
                ServerCertificate = cert;
                CERT_NAME = name;
                CERT_PW = pw;
            }
            else
            {
                RiptideLogger.Log(LogType.Error, "TLS Server", $"Failed to load certificate: {error}");
            }
            return ok;
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
            {
                if (connection.HasPendingMessages)
                    connection.Receive();
            }

            while (closedConnections.TryDequeue(out IPEndPoint endPoint))
                connections.Remove(endPoint);
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
                    await _handshakeSemaphore.WaitAsync().ConfigureAwait(false);
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
                    catch (Exception ex)
                    {
                        RiptideLogger.Log(LogType.Error, "TLS Server", $"TLS handshake failed with {fromEndPoint}: {ex.Message}");
                        try { acceptedSocket.Close(); } catch { }
                    }
                    finally
                    {
                        _handshakeSemaphore.Release();
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
                closedConnections.Enqueue(tcp.RemoteEndPoint);
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
            _handshakeSemaphore?.Dispose();
            _handshakeSemaphore = null;
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
                closedConnections.Enqueue(tcp.RemoteEndPoint);
        }
    }
}
