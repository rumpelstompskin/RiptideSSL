// This file is provided under The MIT License as part of RiptideNetworking.
// Copyright (c) Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub:
// https://github.com/RiptideNetworking/Riptide/blob/main/LICENSE.md

using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json;

namespace Riptide.Transports.TlsTcp
{
    /// <summary>Holds certificate configuration loaded from a JSON file.</summary>
    [Serializable]
    public class CertConfig
    {
        /// <summary>The base name of the PFX file (without extension).</summary>
        public string certificateFile = "";
        /// <summary>The password used to open the PFX file.</summary>
        public string password = "";
    }

    /// <summary>
    /// Static utility for loading TLS certificates and scaffolding the certs directory / config file.
    /// All methods are pure file-system operations with no logging; callers handle logging.
    /// </summary>
    public static class CertLoader
    {
        // EphemeralKeySet (value 32) is defined in netstandard2.1+ / .NET Core 2.1+.
        // Cast by value so the code compiles against netstandard2.0 but uses the flag on
        // modern runtimes (Unity 2021+, .NET 6+).
        private const X509KeyStorageFlags EphemeralKeySet = (X509KeyStorageFlags)32;

        /// <summary>
        /// Loads a PFX certificate from disk.
        /// </summary>
        /// <param name="pfxPath">Absolute path to the .pfx file.</param>
        /// <param name="password">Password protecting the PFX.</param>
        /// <returns>The loaded <see cref="X509Certificate2"/>.</returns>
        /// <exception cref="Exception">Rethrows any exception from the certificate constructor so callers can log it.</exception>
        public static X509Certificate2 LoadCertificate(string pfxPath, string password)
        {
            byte[] pfxBytes = File.ReadAllBytes(pfxPath);
            Exception e1 = null, e2 = null;
            try
            {
                return new X509Certificate2(pfxBytes, password, EphemeralKeySet | X509KeyStorageFlags.Exportable);
            }
            catch (Exception ex) { e1 = ex; }

            try
            {
                return new X509Certificate2(pfxBytes, password, X509KeyStorageFlags.Exportable);
            }
            catch (Exception ex) { e2 = ex; }

            // Both attempts failed. If both errors are "unsupported HMAC", the PFX was created
            // with OpenSSL 3.x defaults (SHA-256 HMAC / AES-256-CBC) which Unity/Mono cannot load.
            // Regenerate the certificate using the -legacy flag:
            //
            //   # Convert existing PFX:
            //   openssl pkcs12 -legacy -in modern.pfx -out legacy.pfx -passin pass:PASSWORD -passout pass:PASSWORD
            //
            //   # Or generate a new self-signed cert:
            //   openssl req -x509 -newkey rsa:2048 -keyout key.pem -out cert.pem -days 3650 -nodes -subj "/CN=localhost"
            //   openssl pkcs12 -export -legacy -in cert.pem -inkey key.pem -out server.pfx -passout pass:PASSWORD
            //
            throw new Exception(
                $"Failed to load PFX '{pfxPath}'. " +
                $"If you see 'unsupported HMAC', regenerate the certificate with 'openssl pkcs12 -export -legacy ...' " +
                $"to use SHA-1/3DES encryption required by Unity/Mono. " +
                $"Errors: [{e1?.Message}] [{e2?.Message}]");
        }

        /// <summary>
        /// Ensures the certs directory and a default config.json exist.
        /// </summary>
        /// <param name="certDir">Absolute path to the certs directory.</param>
        /// <param name="configPath">Absolute path to the config JSON file.</param>
        /// <returns>
        /// <c>true</c> if the config file already existed (caller may proceed to load it);
        /// <c>false</c> if the scaffold was just created (caller should prompt the user to fill it in).
        /// </returns>
        public static bool EnsureScaffold(string certDir, string configPath)
        {
            if (!Directory.Exists(certDir))
                Directory.CreateDirectory(certDir);

            if (!File.Exists(configPath))
            {
                var defaultConfig = new CertConfig { certificateFile = "", password = "" };
                string json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
                File.WriteAllText(configPath, json);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reads the config JSON, derives the PFX path, and loads the certificate.
        /// </summary>
        /// <param name="configPath">Absolute path to the config JSON file.</param>
        /// <param name="certDir">Absolute path to the certs directory (PFX files are resolved here).</param>
        /// <param name="cert">The loaded certificate, or <c>null</c> on failure.</param>
        /// <param name="certName">The certificate file name read from config.</param>
        /// <param name="certPw">The certificate password read from config.</param>
        /// <param name="error">The exception message if loading failed; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> on success; <c>false</c> if the certificate could not be loaded.</returns>
        public static bool LoadFromConfig(string configPath, string certDir,
            out X509Certificate2 cert, out string certName, out string certPw, out string error)
        {
            cert = null;
            certName = string.Empty;
            certPw = string.Empty;
            error = null;

            try
            {
                string json = File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<CertConfig>(json);
                certName = config.certificateFile;
                certPw = config.password;

                string pfxPath = Path.Combine(certDir, $"{certName}.pfx");
                cert = LoadCertificate(pfxPath, certPw);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
