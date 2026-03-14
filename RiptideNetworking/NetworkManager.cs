// This file is provided under The MIT License as part of RiptideNetworking.
// Copyright (c) Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub:
// https://github.com/RiptideNetworking/Riptide/blob/main/LICENSE.md

namespace Riptide
{
    /// <summary>
    /// Provides global, static access to the current network role and the active <see cref="Riptide.Server"/>
    /// and <see cref="Riptide.Client"/> instances.
    /// </summary>
    /// <remarks>
    /// Instances register themselves automatically when constructed — no manual registration is required.
    /// Use <see cref="IsServer"/> and <see cref="IsClient"/> for simple conditionals, or <see cref="Mode"/>
    /// when a switch statement is more appropriate.
    /// </remarks>
    public static class NetworkManager
    {
        private static Server _server;
        private static Client _client;

        /// <summary>The active <see cref="Riptide.Server"/> instance, or <see langword="null"/> if none has been created.</summary>
        public static Server Server => _server;

        /// <summary>The active <see cref="Riptide.Client"/> instance, or <see langword="null"/> if none has been created.</summary>
        public static Client Client => _client;

        /// <summary>Whether the local peer is currently running as a server.</summary>
        /// <remarks><see langword="true"/> when a <see cref="Riptide.Server"/> has been created and <see cref="Riptide.Server.IsRunning"/> is <see langword="true"/>.</remarks>
        public static bool IsServer => _server != null && _server.IsRunning;

        /// <summary>Whether the local peer is currently running as a client.</summary>
        /// <remarks><see langword="true"/> when a <see cref="Riptide.Client"/> has been created and is connecting, pending, or connected.</remarks>
        public static bool IsClient => _client != null && !_client.IsNotConnected;

        /// <summary>
        /// The current <see cref="NetworkMode"/>. Returns <see cref="NetworkMode.Server"/> if the local peer
        /// is running as a server, otherwise <see cref="NetworkMode.Client"/>.
        /// </summary>
        /// <remarks>Only query this after a <see cref="Riptide.Server"/> or <see cref="Riptide.Client"/> has been instantiated.</remarks>
        public static NetworkMode Mode => IsServer ? NetworkMode.Server : NetworkMode.Client;

        /// <summary>Registers a <see cref="Riptide.Server"/> instance. Called automatically by the <see cref="Riptide.Server"/> constructor.</summary>
        /// <param name="server">The server instance to register.</param>
        internal static void SetServer(Server server) => _server = server;

        /// <summary>Registers a <see cref="Riptide.Client"/> instance. Called automatically by the <see cref="Riptide.Client"/> constructor.</summary>
        /// <param name="client">The client instance to register.</param>
        internal static void SetClient(Client client) => _client = client;

        /// <summary>Clears the registered <see cref="Riptide.Server"/> instance. Called automatically when the server stops.</summary>
        internal static void ClearServer() => _server = null;

        /// <summary>Clears the registered <see cref="Riptide.Client"/> instance. Called automatically when the client disconnects.</summary>
        internal static void ClearClient() => _client = null;
    }
}
