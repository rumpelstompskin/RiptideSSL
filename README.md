# TLS TCP Transport for Riptide by Tom Weiland
Encrypt your Riptide connection using TLS with certificates packaged in PKCS12 (.pfx).

Full example implementations are in `Examples/ServerManager.cs` and `Examples/ClientManager.cs`.

## Why legacy certificates are required

Unity's Mono runtime cannot load PFX files created with **OpenSSL 3.x defaults**, which use SHA-256 HMAC and AES-256-CBC to encrypt the PFX container. Mono only supports the older **SHA-1/3DES** (PKCS12 legacy) container format.

The `-legacy` flag in the OpenSSL commands below selects this format. This is a **container-only** limitation — it affects how the certificate file is packaged on disk, not the security of your actual connection. The TLS 1.2 session established at runtime uses modern cipher suites regardless.

If you receive an error like `unsupported HMAC` or `Failed to load PFX`, your certificate was created without `-legacy`. Convert it with:

```powershell
openssl pkcs12 -legacy -in modern.pfx -out legacy.pfx -passin pass:PASSWORD -passout pass:PASSWORD
```

## Server setup

Call `Initialize(basePath)` on the transport before starting the server. This creates the `certs/` directory and `config.json` scaffold if they don't exist, then loads the certificate described by the config. If the scaffold was just created, fill in `config.json` and restart.

```csharp
TcpServer transport = new TcpServer();
transport.Initialize(Application.dataPath); // Unity: pass Application.dataPath

if (transport.CertificateValidated)
{
    server = new Server(transport);
    server.Start(port, maxClients);
}
```

## Client setup

The client connect call blocks the calling thread, so always connect on a background thread (e.g. `Task.Run`) to avoid locking the Unity main thread.

```csharp
TcpClient clientTransport = new TcpClient
{
    ValidateServerCertificate = true,   // false in development, true in production
    CheckCertificateRevocation = true
};

client = new Client(clientTransport);

// Connect asynchronously
await Task.Run(() => client.Connect("127.0.0.1:7777"));
```

## config.json

`config.json` lives in `certs/config.json` relative to the `basePath` you pass to `Initialize()`. In Unity, pass `Application.dataPath` — that resolves to `Assets/` in the Editor and `<Name>_Data/` in a build. Place your `.pfx` file in the same `certs/` directory.

```json
{
    "certificateFile": "yourcertfilename",
    "password": "yourcertpassword"
}
```

`certificateFile` is the name of the `.pfx` file without the extension.

## Example pfx creation
The pfx file must include the private key.
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

string subject      = cert.Subject;       // e.g. "CN=localhost"
string issuer       = cert.Issuer;        // e.g. "CN=MyCA"
string thumbprint   = cert.Thumbprint;    // SHA-1 fingerprint (hex)
string serial       = cert.SerialNumber;  // Hex serial number
DateTime notBefore  = cert.NotBefore;     // Validity start
DateTime notAfter   = cert.NotAfter;      // Validity end (expiry)
```

*Developed with the assistance of [Claude](https://claude.ai) by Anthropic.*