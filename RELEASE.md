# RiptideSSL — TLS TCP Transport for Riptide Networking

**Version:** `2.2.1-TLSv1.2a`
**Date:** 2026-03-17
**License:** MIT
**Extensions by:** Rumpelstompskin (TLS transport, NetworkManager)
**Built on:** [Riptide Networking](https://riptide.tomweiland.net/) by Tom Weiland

---

## Overview

RiptideSSL is a TLS-encrypted TCP transport plugin for [Riptide Networking](https://riptide.tomweiland.net/). It wraps the standard `SslStream` API to provide drop-in TLS support for Riptide's TCP layer, targeting `netstandard2.0` for full Unity/Mono compatibility.

---

## What's New in TLSv1.2a

### Global quality-disconnect threshold configuration
- `Peer` exposes five new abstract write-only properties — `MaxAvgSendAttempts`, `AvgSendAttemptsResilience`, `MaxSendAttempts`, `MaxNotifyLoss`, `NotifyLossResilience` — following the same pattern as the existing `TimeoutTime` setter
- `Server` implementations propagate each value to all currently connected clients in addition to storing the new default for future connections
- `Client` implementations update the live connection if one exists
- `Connection.Initialize()` now reads all five quality defaults from the `Peer` at connect time, so any pre-start configuration on `Server` or `Client` is automatically applied to every new connection without requiring a `ClientConnected` event handler

### Diagnostic logging for quality-disconnect triggers
- `Connection.UpdateSendAttemptsViolations()` now logs the rolling mean, the configured limit, and the consecutive violation count before disconnecting — previously this path was silent
- `Connection.UpdateLossViolations()` now logs the rolling loss rate, the configured limit, and the consecutive violation count before disconnecting — previously this path was also silent
- Both log lines use `LogType.Info` and include the connection endpoint, matching the style of the existing `PendingMessage` log

---

## What's New in TLSv1.2

### Zero-allocation send/receive (ArrayPool)
- Send path now rents a frame buffer from `ArrayPool<byte>.Shared` instead of writing into a shared `SendBuffer`, then returns it in a `finally` block — eliminates per-send heap allocation and removes the shared buffer from `TcpPeer`
- Receive path enqueues a `RentedSegment` struct (rented buffer + length) instead of allocating a `new byte[]` per message; buffers are returned to the pool after processing or on connection close
- Added `System.Buffers 4.5.1` package reference to support `ArrayPool` on all target frameworks

### Message pool — Stack instead of List
- `Message` and `PendingMessage` internal pools changed from `List<T>` to `Stack<T>`
- `RemoveAt(0)` (O(n) shift) replaced by `Pop()` (O(1))
- `pool.Contains(this)` duplicate guard (O(n) scan) replaced by an `_inPool` bool flag (O(1))
- `Message` gains an explicit `_maxPoolSize` field since `Stack<T>` has no `Capacity` property

### Poll-path skip when idle
- `TcpConnection` adds a `_hasPendingMessages` volatile int flag (0/1) set by the background receive loop and cleared at the start of each `Receive()` drain
- `TcpServer.Poll()` skips calling `Receive()` on connections where the flag is not set, eliminating unnecessary ConcurrentQueue checks on quiet connections each tick

### Handshake concurrency control
- New `MaxConcurrentHandshakes` property on `TcpServer` (default: `32`) backed by a `SemaphoreSlim`
- Each background TLS handshake task waits on the semaphore before starting, preventing ThreadPool saturation during connection bursts
- Excess connections queue inside the semaphore and proceed as slots free — throughput is not lost, only parallelism is capped
- Semaphore is created in `Start()` and disposed in `Shutdown()`

### Increased default connection backlog
- `MaxPendingConnections` default raised from `5` to `512`
- Both `MaxPendingConnections` and `MaxConcurrentHandshakes` are public settable properties — override them before calling `Start()` for your target scale

### Thread-safety fix for closedConnections
- `TcpServer.closedConnections` changed from `List<IPEndPoint>` to `ConcurrentQueue<IPEndPoint>`
- `Close()` and `OnDisconnected()` (called from background threads) now safely enqueue without locking
- `Poll()` drains the queue with `TryDequeue` in a `while` loop, replacing the previous `foreach` + `Clear()` pattern

---

## Public API Changes

### `TcpServer` — new properties

| Property | Type | Default | Description |
|---|---|---|---|
| `MaxPendingConnections` | `int` | `512` | TCP kernel backlog passed to `socket.Listen()`. Set before `Start()`. |
| `MaxConcurrentHandshakes` | `int` | `32` | Max parallel TLS handshakes. Set before `Start()`. |

### `TcpPeer` — removed

| Member | Reason |
|---|---|
| `SendBuffer` | Replaced by per-call `ArrayPool<byte>.Shared.Rent()` in `TcpConnection` |

---

## Full API Summary

### `NetworkManager` (static)

| Member | Type | Description |
|---|---|---|
| `IsServer` | `bool` | `true` if a server is running |
| `IsClient` | `bool` | `true` if a client is connecting or connected |
| `Mode` | `NetworkMode` | Current network role |
| `Server` | `Server` | The active `Server` instance |
| `Client` | `Client` | The active `Client` instance |

### `TcpServer`

| Member | Description |
|---|---|
| `Initialize(basePath)` | Loads cert config from `{basePath}/certs/config.json`, scaffolds if missing |
| `CertificateValidated` | `true` if cert loaded successfully |
| `ServerCertificate` | `X509Certificate2` loaded cert object |
| `CERT_NAME` | Cert filename from config (no extension) |
| `CERT_PW` | Cert password from config |
| `EnabledSslProtocols` | SSL protocols allowed (default: Tls12) |
| `RequireClientCertificate` | Enable mTLS |
| `MaxPendingConnections` | TCP backlog size (default: 512) |
| `MaxConcurrentHandshakes` | Concurrent TLS handshake limit (default: 32) |

### `TcpClient`

| Member | Description |
|---|---|
| `Connect(addressPort)` | Blocking connect — run on background thread |
| `ValidateServerCertificate` | Validate server cert (false in dev, true in prod) |
| `CheckCertificateRevocation` | Check CRL |
| `ClientCertificates` | `X509CertificateCollection` for mTLS |
| `EnabledSslProtocols` | SSL protocols allowed |

### `CertLoader` (static utility)

| Member | Description |
|---|---|
| `LoadCertificate(bytes, password)` | Load `X509Certificate2` from PFX bytes, with Mono/Unity compat |
| `EnsureScaffold(basePath)` | Create `certs/` dir and `config.json` if absent |
| `LoadFromConfig(basePath)` | Read config JSON and load the certificate |

---

## Compatibility

| Property | Value |
|---|---|
| Target framework | `netstandard2.0` |
| Unity/Mono | Compatible (legacy PFX format required — see README) |
| Language version | C# 11 |
| Dependencies | `Newtonsoft.Json 13.0.4`, `System.Buffers 4.5.1` |

---

## Known Constraints

- PFX must use legacy SHA-1/3DES container (OpenSSL `-legacy` flag) for Mono/Unity compatibility
- `TcpClient.Connect()` blocks the calling thread — always call from `Task.Run()`
- `MaxPendingConnections` and `MaxConcurrentHandshakes` must be set before `Start()` — changing them after has no effect on the running server
