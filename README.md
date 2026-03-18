# TLS TCP Transport for Riptide

Encrypt your Riptide connection using TLS with certificates packaged in PKCS12 (.pfx).

Full example implementations are in `Examples/ServerManager.cs` and `Examples/ClientManager.cs`.

---

## Why legacy certificates are required

Unity's Mono runtime cannot load PFX files created with **OpenSSL 3.x defaults**, which use SHA-256 HMAC and AES-256-CBC to encrypt the PFX container. Mono only supports the older **SHA-1/3DES** (PKCS12 legacy) container format.

The `-legacy` flag in the OpenSSL commands below selects this format. This is a **container-only** limitation — it affects how the certificate file is packaged on disk, not the security of your actual connection. The TLS 1.2 session established at runtime uses modern cipher suites regardless.

If you receive an error like `unsupported HMAC` or `Failed to load PFX`, your certificate was created without `-legacy`. Convert it with:

```powershell
openssl pkcs12 -legacy -in modern.pfx -out legacy.pfx -passin pass:PASSWORD -passout pass:PASSWORD
```

---

## Server setup

Call `Initialize(basePath)` on the transport before starting the server. This creates the `certs/` directory and `config.json` scaffold if they don't exist, then loads the certificate described by the config. If the scaffold was just created, fill in `config.json` and restart.

```csharp
TcpServer transport = new TcpServer();
transport.Initialize(Application.dataPath); // Unity: pass Application.dataPath

// Optional: tune connection and handshake capacity before Start()
transport.MaxPendingConnections = 2048;    // TCP kernel backlog (default: 512)
transport.MaxConcurrentHandshakes = 64;   // Concurrent TLS handshakes (default: 32)

if (transport.CertificateValidated)
{
    server = new Server(transport);
    server.Start(port, maxClients);
}
```

`MaxPendingConnections` controls how many unaccepted TCP connections the OS kernel will queue. For servers expecting large simultaneous connection bursts (e.g. 1000+ clients reconnecting at once), set this higher than the expected peak. `MaxConcurrentHandshakes` limits how many TLS handshakes run in parallel — excess connections queue and proceed as slots free up, so increasing this speeds up burst handling at the cost of more CPU/thread pressure.

---

## Connection quality thresholds

Riptide continuously monitors connection health using three rolling checks and disconnects any client that consistently exceeds them:

| Check | Default threshold | Resilience |
|---|---|---|
| Average send attempts (last 64 reliable messages) | `MaxAvgSendAttempts = 5` | Disconnect after `AvgSendAttemptsResilience = 64` consecutive violations |
| Single reliable message resend count | `MaxSendAttempts = 15` | Immediate disconnect |
| Notify message loss rate (last 64 notify messages) | `MaxNotifyLoss = 5%` | Disconnect after `NotifyLossResilience = 64` consecutive violations |

All five values are configurable on the `Server` before calling `Start()` and apply automatically to every new connection:

```csharp
server.MaxAvgSendAttempts = 8;           // (default: 5)
server.AvgSendAttemptsResilience = 128;  // (default: 64)
server.MaxSendAttempts = 25;             // (default: 15)
server.MaxNotifyLoss = 0.10f;            // (default: 0.05)
server.NotifyLossResilience = 128;       // (default: 64)

server.Start(port, maxClients);
```

Setting any of these after `Start()` propagates immediately to all currently connected clients.

To disable quality-disconnect for a specific connection entirely (e.g. a trusted local relay):

```csharp
// Inside Server.ClientConnected handler:
e.Client.CanQualityDisconnect = false;
```

---

## Client setup

The client connect call blocks the calling thread, so always connect on a background thread (e.g. `Task.Run`) to avoid locking the Unity main thread.

```csharp
TcpClient clientTransport = new TcpClient
{
    ValidateServerCertificate = true,   // false in development, true in production
    CheckCertificateRevocation = true
};

client = new Client(clientTransport);

await Task.Run(() => client.Connect("127.0.0.1:7777"));
```

---

## config.json

`config.json` lives in `certs/config.json` relative to the `basePath` you pass to `Initialize()`. In Unity, pass `Application.dataPath` — that resolves to `Assets/` in the Editor and `<Name>_Data/` in a build. Place your `.pfx` file in the same `certs/` directory.

```json
{
    "certificateFile": "yourcertfilename",
    "password": "yourcertpassword"
}
```

`certificateFile` is the name of the `.pfx` file without the extension.

---

## Example PFX creation

The PFX file must include the private key.

```powershell
openssl pkcs12 -export -legacy \
  -inkey yourdomain.key \
  -in yourdomain.pem \
  -certfile yourdomain_chain.pem \
  -out yourdomain.pfx \
  -certpbe PBE-SHA1-3DES \
  -keypbe PBE-SHA1-3DES \
  -macalg sha1 \
  -passout pass:changeit
```

---

## Accessing certificate data at runtime

After `Initialize()` succeeds and `CertificateValidated` is `true`, the `TcpServer` transport exposes the following properties:

| Property | Type | Description |
|---|---|---|
| `transport.CERT_NAME` | `string` | Certificate file name read from `config.json` (without `.pfx`) |
| `transport.CERT_PW` | `string` | Password read from `config.json` |
| `transport.ServerCertificate` | `X509Certificate2` | The loaded certificate object |

The `X509Certificate2` object provides additional details:

```csharp
X509Certificate2 cert = transport.ServerCertificate;

string subject     = cert.Subject;      // e.g. "CN=localhost"
string issuer      = cert.Issuer;       // e.g. "CN=MyCA"
string thumbprint  = cert.Thumbprint;   // SHA-1 fingerprint (hex)
string serial      = cert.SerialNumber; // Hex serial number
DateTime notBefore = cert.NotBefore;    // Validity start
DateTime notAfter  = cert.NotAfter;     // Validity end (expiry)
```

---

## NetworkManager

`NetworkManager` is a static class providing global access to the current network role and the active `Server`/`Client` instances. Instances register themselves automatically when constructed and clear themselves when stopped or disconnected — no manual setup required.

```csharp
bool isServer = NetworkManager.IsServer;
bool isClient = NetworkManager.IsClient;

Server server = NetworkManager.Server;
Client client = NetworkManager.Client;

switch (NetworkManager.Mode)
{
    case NetworkMode.Server:
        // server-side logic
        break;
    case NetworkMode.Client:
        // client-side logic
        break;
}
```

`NetworkManager.Server` is set to `null` automatically when `Server.Stop()` is called. `NetworkManager.Client` is set to `null` automatically when the client disconnects for any reason.

### NetworkMode

```csharp
public enum NetworkMode { Server, Client }
```

---

*TLS transport and NetworkManager extensions by Rumpelstompskin, developed with the assistance of [Claude](https://claude.ai) by Anthropic. Built on [Riptide Networking](https://riptide.tomweiland.net/) by Tom Weiland.*
