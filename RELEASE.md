# RiptideSSL — TLS TCP Transport for Riptide Networking

**Version:** `2.2.1-TLSv1.1a`
**Date:** 2026-03-14
**License:** MIT | **Author:** Tom Weiland

---

## Overview

RiptideSSL is a TLS-encrypted TCP transport plugin for [Riptide Networking](https://riptide.tomweiland.net/). It wraps the standard `SslStream` API to provide drop-in TLS support for Riptide's TCP layer, targeting `netstandard2.0` for full Unity/Mono compatibility.

---

## What's New in TLSv1.1a

### NetworkManager
- New static `NetworkManager` class providing global access to the active network role and peer instances
- `Server` and `Client` instances register themselves automatically on construction — no manual setup required
- `NetworkManager.Server` is cleared to `null` automatically when `Server.Stop()` is called
- `NetworkManager.Client` is cleared to `null` automatically when the client disconnects for any reason
- `IsServer` — `true` when a `Server` instance exists and `IsRunning` is `true`
- `IsClient` — `true` when a `Client` instance exists and is connecting, pending, or connected
- `Mode` — returns a `NetworkMode` value for use in switch statements
- `Server` and `Client` instance references accessible globally

### NetworkMode
- New `NetworkMode` enum with `Server` and `Client` values

### Build Output
- Build output organized by version: `bin/Debug/2.2.1-TLSv1.1a/` and `bin/Release/2.2.1-TLSv1.1a/`

---

## Public API Summary

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
| `MaxPendingConnections` | TCP backlog size |

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
| Dependency | `Newtonsoft.Json 13.0.4` |

---

## Known Constraints

- PFX must use legacy SHA-1/3DES container (OpenSSL `-legacy` flag) for Mono/Unity compatibility
- `TcpClient.Connect()` blocks the calling thread — always call from `Task.Run()`
