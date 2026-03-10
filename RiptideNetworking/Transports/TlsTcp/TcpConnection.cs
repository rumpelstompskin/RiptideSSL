// This file is provided under The MIT License as part of RiptideNetworking.
// Copyright (c) Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub:
// https://github.com/RiptideNetworking/Riptide/blob/main/LICENSE.md

using Riptide.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Riptide.Transports.TlsTcp
{
    /// <summary>Represents a connection to a <see cref="TcpServer"/> or <see cref="TcpClient"/>.</summary>
    public class TcpConnection : Connection, IEquatable<TcpConnection>
    {
        /// <summary>The endpoint representing the other end of the connection.</summary>
        public readonly IPEndPoint RemoteEndPoint;

        /// <summary>The socket backing this connection (used for endpoint identity and closing).</summary>
        private readonly Socket socket;
        /// <summary>The local peer this connection is associated with.</summary>
        private readonly TcpPeer peer;

        /// <summary>The stream used for framed reads/writes (NetworkStream wrapped by SslStream).</summary>
        private Stream stream;

        private readonly object sendLock = new object();
        private readonly ConcurrentQueue<byte[]> receivedMessages = new ConcurrentQueue<byte[]>();

        private CancellationTokenSource receiveCts;
        private Task receiveTask;
        private volatile bool isClosed;

        /// <summary>Initializes the connection.</summary>
        /// <param name="socket">The socket backing this connection.</param>
        /// <param name="remoteEndPoint">The endpoint representing the other end of the connection.</param>
        /// <param name="peer">The local peer this connection is associated with.</param>
        internal TcpConnection(Socket socket, IPEndPoint remoteEndPoint, TcpPeer peer)
        {
            RemoteEndPoint = remoteEndPoint;
            this.socket = socket;
            this.peer = peer;
        }

        /// <summary>Sets the stream that will be used for communication (typically an <c>SslStream</c>).</summary>
        internal void SetStream(Stream tlsStream)
        {
            stream = tlsStream ?? throw new ArgumentNullException(nameof(tlsStream));
        }

        /// <summary>Starts the background receive loop that frames messages and enqueues them.</summary>
        internal void StartReceiving()
        {
            if (stream == null)
                throw new InvalidOperationException("Stream has not been set. Call SetStream(...) after TLS handshake.");

            if (receiveTask != null)
                return;

            receiveCts = new CancellationTokenSource();
            receiveTask = Task.Run(() => ReceiveLoopAsync(receiveCts.Token));
        }

        /// <inheritdoc/>
        protected internal override void Send(byte[] dataBuffer, int amount)
        {
            if (amount == 0)
                throw new ArgumentOutOfRangeException(nameof(amount), "Sending 0 bytes is not allowed!");
            if (isClosed || stream == null)
                return;

            try
            {
                // IMPORTANT: With TLS, concurrent writes can interleave and corrupt framing unless synchronized.
                lock (sendLock)
                {
                    Converter.FromInt(amount, peer.SendBuffer, 0);
                    Buffer.BlockCopy(dataBuffer, 0, peer.SendBuffer, sizeof(int), amount);

                    stream.Write(peer.SendBuffer, 0, sizeof(int) + amount);
                    // No Flush() here—SslStream/NetworkStream flush behavior is sufficient for TCP framing.
                }
            }
            catch (IOException)
            {
                peer.OnDisconnected(this, DisconnectReason.TransportError);
                Close();
            }
            catch (ObjectDisposedException)
            {
                peer.OnDisconnected(this, DisconnectReason.TransportError);
                Close();
            }
        }

        /// <summary>
        /// Pumps any fully received messages into <see cref="TcpPeer.ReceiveBuffer"/> and raises data events on the peer.
        /// Call this from the peer's <c>Poll()</c> loop (Unity main thread).
        /// </summary>
        internal void Receive()
        {
            while (receivedMessages.TryDequeue(out byte[] msg))
            {
                int count = msg.Length;
                if (count <= 0)
                    continue;

                Buffer.BlockCopy(msg, 0, peer.ReceiveBuffer, 0, count);
                peer.OnDataReceived(count, this);
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            // Frame format: [int32 length][payload...]
            byte[] header = new byte[sizeof(int)];

            try
            {
                while (!ct.IsCancellationRequested && !isClosed)
                {
                    int read = await ReadExactAsync(stream, header, 0, sizeof(int), ct).ConfigureAwait(false);
                    if (read == 0)
                        break; // clean EOF

                    int length = Converter.ToInt(header, 0);
                    if (length <= 0 || length > Message.MaxSize)
                        throw new IOException($"Invalid message length: {length}");

                    byte[] payload = new byte[length];
                    read = await ReadExactAsync(stream, payload, 0, length, ct).ConfigureAwait(false);
                    if (read == 0)
                        break;

                    receivedMessages.Enqueue(payload);
                }

                if (!isClosed)
                    peer.OnDisconnected(this, DisconnectReason.Disconnected);
            }
            catch (OperationCanceledException)
            {
                // normal during shutdown
            }
            catch (IOException)
            {
                if (!isClosed)
                    peer.OnDisconnected(this, DisconnectReason.TransportError);
            }
            catch (ObjectDisposedException)
            {
                if (!isClosed)
                    peer.OnDisconnected(this, DisconnectReason.TransportError);
            }
            finally
            {
                Close();
            }
        }

        private static async Task<int> ReadExactAsync(Stream s, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            int total = 0;
            while (total < count)
            {
                int n = await s.ReadAsync(buffer, offset + total, count - total, ct).ConfigureAwait(false);
                if (n == 0)
                    return 0;
                total += n;
            }
            return total;
        }

        /// <summary>Closes the connection.</summary>
        internal void Close()
        {
            if (isClosed)
                return;

            isClosed = true;

            try { receiveCts?.Cancel(); } catch { }
            try { stream?.Dispose(); } catch { }

            try
            {
                if (socket.Connected)
                    socket.Shutdown(SocketShutdown.Both);
            }
            catch { /* ignore */ }

            try { socket.Close(); } catch { }
        }

        /// <inheritdoc/>
        public override string ToString() => RemoteEndPoint.ToStringBasedOnIPFormat();

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as TcpConnection);
        /// <inheritdoc/>
        public bool Equals(TcpConnection other)
        {
            if (other is null)
                return false;

            if (ReferenceEquals(this, other))
                return true;

            return RemoteEndPoint.Equals(other.RemoteEndPoint);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return -288961498 + EqualityComparer<IPEndPoint>.Default.GetHashCode(RemoteEndPoint);
        }

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        public static bool operator ==(TcpConnection left, TcpConnection right)
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
        {
            if (left is null)
            {
                if (right is null)
                    return true;

                return false; // Only the left side is null
            }

            // Equals handles case of null on right side
            return left.Equals(right);
        }

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        public static bool operator !=(TcpConnection left, TcpConnection right) => !(left == right);
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    }
}
