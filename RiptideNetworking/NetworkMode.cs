// This file is provided under The MIT License as part of RiptideNetworking.
// Copyright (c) Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub:
// https://github.com/RiptideNetworking/Riptide/blob/main/LICENSE.md

namespace Riptide
{
    /// <summary>Represents the network role the local peer is currently acting as.</summary>
    public enum NetworkMode
    {
        /// <summary>The local peer is running as a server, accepting connections from clients.</summary>
        Server,
        /// <summary>The local peer is running as a client, connected to (or connecting to) a server.</summary>
        Client,
    }
}
