# TLS TCP Transport for Riptide by Tom Weiland
Encrypt your Riptide connection using TLS with certificates packaged in PKCS12 (.pfx).

Full example implementations are in `Examples/ServerManager.cs` and `Examples/ClientManager.cs`.

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

