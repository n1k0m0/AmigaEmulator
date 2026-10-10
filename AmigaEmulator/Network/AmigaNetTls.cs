/*
   Copyright 2026 Nils Kopal <Nils.Kopal<at>kopaldev.de

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using AmigaEmulator.Core;
namespace AmigaEmulator.Network
{
    public static class AmigaNetTls
    {
        private const string ServerCertificateFileName = "network-server.pfx";
        private const string TrustedCertificatesFileName = "network-trust.json";
        private const string ServerCertificateSubject = "CN=AmigaEmulatorNetworkServer";
        private const string ServerCertificateDnsName = "AmigaEmulatorNetworkServer";
        private const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";
        private const int CertificateKeySizeBits = 2048;
        private static readonly SslProtocols AllowedProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
        private static readonly object CertificateSyncRoot = new object();
        private static readonly object TrustSyncRoot = new object();
        private static X509Certificate2 _serverCertificate;
        public static SslStream AuthenticateServer(TcpClient tcpClient)
        {
            if (tcpClient == null)
            {
                throw new ArgumentNullException(nameof(tcpClient));
            }
            var stream = new SslStream(tcpClient.GetStream(), false);
            try
            {
                stream.AuthenticateAsServer(LoadOrCreateServerCertificate(), false, AllowedProtocols, false);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        public static SslStream AuthenticateClient(TcpClient tcpClient, string host, int port, out string status)
        {
            if (tcpClient == null)
            {
                throw new ArgumentNullException(nameof(tcpClient));
            }
            string validationStatus = "TLS CONNECTING";
            AmigaNetTlsPinChange detectedPinChange = null;
            var stream = new SslStream(
                tcpClient.GetStream(),
                false,
                (sender, certificate, chain, errors) => ValidateServerCertificate(host, port, certificate, out validationStatus, out detectedPinChange));
            try
            {
                stream.AuthenticateAsClient(ServerCertificateDnsName, null, AllowedProtocols, false);
                status = validationStatus;
                return stream;
            }
            catch (AuthenticationException ex)
            {
                stream.Dispose();
                string failureStatus = string.Equals(validationStatus, "TLS CONNECTING", StringComparison.Ordinal)
                    ? "TLS FAILED"
                    : validationStatus;
                throw new AmigaNetTlsException(failureStatus, ex, detectedPinChange);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        public static string GetServerCertificateFingerprint()
        {
            return ComputeFingerprint(LoadOrCreateServerCertificate());
        }
        public static string GetServerCertificateShortFingerprint()
        {
            return FormatShortFingerprint(GetServerCertificateFingerprint());
        }
        public static string GetTrustedServerShortFingerprint(string host, int port)
        {
            string trustKey = BuildTrustKey(host, port);
            lock (TrustSyncRoot)
            {
                NetworkTrustFile trustFile = LoadTrustFile();
                if (trustFile.PinnedCertificates.TryGetValue(trustKey, out string fingerprint))
                {
                    return FormatShortFingerprint(fingerprint);
                }
            }
            return "UNKNOWN";
        }
        public static string FormatFingerprintForDisplay(string fingerprint)
        {
            return FormatShortFingerprint(fingerprint);
        }
        public static void ReplaceTrustedServerCertificate(string host, int port, string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                throw new ArgumentException("A certificate fingerprint is required.", nameof(fingerprint));
            }
            string trustKey = BuildTrustKey(host, port);
            lock (TrustSyncRoot)
            {
                NetworkTrustFile trustFile = LoadTrustFile();
                trustFile.PinnedCertificates[trustKey] = fingerprint.Trim().ToUpperInvariant();
                SaveTrustFile(trustFile);
            }
        }
        private static X509Certificate2 LoadOrCreateServerCertificate()
        {
            lock (CertificateSyncRoot)
            {
                if (_serverCertificate != null && _serverCertificate.HasPrivateKey)
                {
                    return _serverCertificate;
                }
                string path = GetServerCertificatePath();
                try
                {
                    if (File.Exists(path))
                    {
                        var certificate = X509CertificateLoader.LoadPkcs12(
                            File.ReadAllBytes(path),
                            (string)null,
                            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable,
                            null);
                        if (certificate.HasPrivateKey && certificate.NotAfter > DateTime.UtcNow.AddDays(7))
                        {
                            _serverCertificate = certificate;
                            return _serverCertificate;
                        }
                        certificate.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                _serverCertificate = CreateServerCertificate();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, _serverCertificate.Export(X509ContentType.Pfx));
                return _serverCertificate;
            }
        }
        private static X509Certificate2 CreateServerCertificate()
        {
            using (RSA rsa = RSA.Create(CertificateKeySizeBits))
            {
                var request = new CertificateRequest(
                    ServerCertificateSubject,
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    false));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid(ServerAuthOid) },
                    false));
                var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
                subjectAlternativeNames.AddDnsName(ServerCertificateDnsName);
                request.CertificateExtensions.Add(subjectAlternativeNames.Build());
                using (X509Certificate2 temporaryCertificate = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddYears(10)))
                {
                    return X509CertificateLoader.LoadPkcs12(
                        temporaryCertificate.Export(X509ContentType.Pfx),
                        (string)null,
                        X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable,
                        null);
                }
            }
        }
        private static bool ValidateServerCertificate(string host, int port, X509Certificate certificate, out string status, out AmigaNetTlsPinChange pinChange)
        {
            pinChange = null;
            if (certificate == null)
            {
                status = "TLS NO CERT";
                return false;
            }
            string fingerprint;
            using (X509Certificate2 serverCertificate = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()))
            {
                fingerprint = ComputeFingerprint(serverCertificate);
            }
            string trustKey = BuildTrustKey(host, port);
            lock (TrustSyncRoot)
            {
                NetworkTrustFile trustFile = LoadTrustFile();
                if (trustFile.PinnedCertificates.TryGetValue(trustKey, out string pinnedFingerprint))
                {
                    if (string.Equals(pinnedFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                    {
                        status = "TLS PIN OK " + FormatShortFingerprint(fingerprint);
                        return true;
                    }
                    pinChange = new AmigaNetTlsPinChange(host, port, pinnedFingerprint, fingerprint);
                    status = "TLS CERT CHANGED " + pinChange.OldShortFingerprint + " " + pinChange.NewShortFingerprint;
                    return false;
                }
                trustFile.PinnedCertificates[trustKey] = fingerprint;
                SaveTrustFile(trustFile);
                status = "TLS PINNED " + FormatShortFingerprint(fingerprint);
                return true;
            }
        }
        private static string ComputeFingerprint(X509Certificate certificate)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(certificate.GetRawCertData());
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToUpperInvariant();
            }
        }
        private static string FormatShortFingerprint(string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length < 12)
            {
                return "UNKNOWN";
            }
            return fingerprint.Substring(0, 4) + "-" + fingerprint.Substring(4, 4) + "-" + fingerprint.Substring(8, 4);
        }
        private static string BuildTrustKey(string host, int port)
        {
            string normalizedHost = string.IsNullOrWhiteSpace(host) ? "unknown" : host.Trim().ToLowerInvariant();
            return normalizedHost + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        private static NetworkTrustFile LoadTrustFile()
        {
            string path = GetTrustedCertificatesPath();
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    NetworkTrustFile trustFile = JsonSerializer.Deserialize<NetworkTrustFile>(json);
                    if (trustFile != null)
                    {
                        trustFile.EnsureInitialized();
                        return trustFile;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            return new NetworkTrustFile();
        }
        private static void SaveTrustFile(NetworkTrustFile trustFile)
        {
            string path = GetTrustedCertificatesPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(trustFile, options));
        }
        private static string GetServerCertificatePath()
        {
            return Path.Combine(UserDataPaths.GetBaseDirectory(), ServerCertificateFileName);
        }
        private static string GetTrustedCertificatesPath()
        {
            return Path.Combine(UserDataPaths.GetBaseDirectory(), TrustedCertificatesFileName);
        }
        private sealed class NetworkTrustFile
        {
            public int Version { get; set; } = 1;
            public Dictionary<string, string> PinnedCertificates { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public void EnsureInitialized()
            {
                if (PinnedCertificates == null)
                {
                    PinnedCertificates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }
    }
    public sealed class AmigaNetTlsPinChange
    {
        public AmigaNetTlsPinChange(string host, int port, string oldFingerprint, string newFingerprint)
        {
            Host = string.IsNullOrWhiteSpace(host) ? "unknown" : host.Trim();
            Port = port;
            OldFingerprint = string.IsNullOrWhiteSpace(oldFingerprint) ? string.Empty : oldFingerprint.Trim().ToUpperInvariant();
            NewFingerprint = string.IsNullOrWhiteSpace(newFingerprint) ? string.Empty : newFingerprint.Trim().ToUpperInvariant();
        }
        public string Host { get; private set; }
        public int Port { get; private set; }
        public string OldFingerprint { get; private set; }
        public string NewFingerprint { get; private set; }
        public string OldShortFingerprint
        {
            get { return AmigaNetTls.FormatFingerprintForDisplay(OldFingerprint); }
        }
        public string NewShortFingerprint
        {
            get { return AmigaNetTls.FormatFingerprintForDisplay(NewFingerprint); }
        }
    }
    public sealed class AmigaNetTlsException : Exception
    {
        public AmigaNetTlsException(string message, Exception innerException)
            : this(message, innerException, null)
        {
        }
        public AmigaNetTlsException(string message, Exception innerException, AmigaNetTlsPinChange pinChange)
            : base(string.IsNullOrWhiteSpace(message) ? "TLS FAILED" : message, innerException)
        {
            PinChange = pinChange;
        }
        public AmigaNetTlsPinChange PinChange { get; private set; }
        public bool IsCertificateChanged
        {
            get { return PinChange != null; }
        }
    }
}
