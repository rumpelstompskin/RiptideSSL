# TLS TCP Transport for Riptide by Tom Weiland
Encrypt your connection using certificates packaged in PKCS12.

Example implementation in ServerManager.cs and ClientManager.cs

ClientManager.cs
```csharp
TcpClient clientTransport = new TcpClient { ValidateServerCertificate = true, CheckCertificateRevocation = true }; 
// Development environment = false, Production = true
client = new Client(clientTransport);
```
config.json Example
```json
{
    "certificateFile": "certfilename",
    "password": "certpw"
}
```

## Known quirks
If looking to host and act as a client as well, use async to connect the client. Otherwise, unity main thread locks up when you try to connect.
pfx file must have private key.

Example pfx creation
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
