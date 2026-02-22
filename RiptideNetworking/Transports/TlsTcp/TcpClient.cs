// This file is provided under The MIT License as part of RiptideNetworking.
// Copyright (c) Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub:
// https://github.com/RiptideNetworking/Riptide/blob/main/LICENSE.md

using System;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.IO;

namespace Riptide.Transports.TlsTcp
{
    /// <summary>A client which can connect to a <see cref="TcpServer"/> using TLS.</summary>
    public class TcpClient : TcpPeer, IClient
    {
        /// <inheritdoc/>
        public event EventHandler Connected;
        /// <inheritdoc/>
        public event EventHandler ConnectionFailed;
        /// <inheritdoc/>
        public event EventHandler<DataReceivedEventArgs> DataReceived;

        /// <summary>The connection to the server.</summary>
        private TcpConnection tcpConnection;

        /// <summary>Which TLS protocol versions to allow.</summary>
        public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.Tls12;

        /// <summary>If true, uses strict certificate validation (recommended for production).</summary>
        public bool ValidateServerCertificate { get; set; } = false;

        /// <summary>If true, check whether the certificate revocation list is checked during authentication</summary>
        public bool CheckCertificateRevocation { get; set; } = false;

        /// <summary>Optional callback to validate the server certificate.</summary>
        public RemoteCertificateValidationCallback ServerCertificateValidationCallback { get; set; }

        /// <summary>Optional client certificates for mutual TLS (mTLS).</summary>
        public X509CertificateCollection ClientCertificates { get; } = new X509CertificateCollection();

        /// <inheritdoc/>
        /// <remarks>Expects the host address to consist of an IP/hostname and port, separated by a colon. For example: <c>127.0.0.1:7777</c>.</remarks>
        public bool Connect(string hostAddress, out Connection connection, out string connectError)
        {
            connectError = $"Invalid host address '{hostAddress}'! Host and port should be separated by a colon, for example: '127.0.0.1:7777'.";
            if (!ParseHostAddress(hostAddress, out string host, out IPAddress ip, out ushort port))
            {
                connection = null;
                return false;
            }

            IPEndPoint remoteEndPoint = new IPEndPoint(ip, port);
            socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                SendBufferSize = socketBufferSize,
                ReceiveBufferSize = socketBufferSize,
                NoDelay = true,
            };

            try
            {
                socket.Connect(remoteEndPoint); // TODO: do something about the fact that this is a blocking call
            }
            catch (SocketException)
            {
                connection = null;
                ConnectionFailed?.Invoke(this, EventArgs.Empty);
                return false;
            }

            try
            {
                var networkStream = new NetworkStream(socket, ownsSocket: false);

                RemoteCertificateValidationCallback cb =
                    ServerCertificateValidationCallback ??
                    ((sender, cert, chain, errors) => !ValidateServerCertificate || errors == SslPolicyErrors.None);

                var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false, cb);

                // Use the provided host for SNI / name validation. If host is an IP and you enable validation, SAN must include that IP.
                sslStream.AuthenticateAsClient(host, ClientCertificates, EnabledSslProtocols, CheckCertificateRevocation);

                tcpConnection = new TcpConnection(socket, remoteEndPoint, this);
                tcpConnection.SetStream(sslStream);
                tcpConnection.StartReceiving();

                connection = tcpConnection;
                connectError = string.Empty;

                Connected?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex) when (ex is AuthenticationException || ex is IOException)
            {
                try { socket.Close(); } catch { }
                connection = null;
                ConnectionFailed?.Invoke(this, EventArgs.Empty);
                connectError = ex.Message;
                return false;
            }
        }

        /// <inheritdoc/>
        public void Poll()
        {
            tcpConnection?.Receive();
        }

        /// <inheritdoc/>
        public void Disconnect()
        {
            tcpConnection?.Close();
        }

        /// <inheritdoc/>
        public void Send(byte[] dataBuffer, int amount, Connection connection)
        {
            if (connection is TcpConnection tcp)
                tcp.Send(dataBuffer, amount);
        }

        /// <inheritdoc/>
        protected internal override void OnDataReceived(int amount, TcpConnection fromConnection)
        {
            DataReceived?.Invoke(this, new DataReceivedEventArgs(ReceiveBuffer, amount, fromConnection));
        }

        /// <summary>Parses "host:port" into host string, ip, and port.</summary>
        private static bool ParseHostAddress(string hostAddress, out string host, out IPAddress ip, out ushort port)
        {
            host = null;
            ip = null;
            port = 0;

            string[] addressParts = hostAddress.Split(':');
            if (addressParts.Length != 2)
                return false;

            host = addressParts[0];

            if (!ushort.TryParse(addressParts[1], out port))
                return false;

            // Try IP first; if hostname, resolve.
            if (IPAddress.TryParse(host, out ip))
                return true;

            try
            {
                ip = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork || a.AddressFamily == AddressFamily.InterNetworkV6);
                return ip != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
