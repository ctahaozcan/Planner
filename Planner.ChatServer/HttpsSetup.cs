using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Planner.Chat;

namespace Planner.ChatServer;

public static class HttpsSetup
{
    public const string PfxPassword = "YaverLocalHttps";

    public static X509Certificate2 EnsureCertificate(string pfxPath)
    {
        if (File.Exists(pfxPath))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(pfxPath, PfxPassword);
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Yaver ChatServer",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        var exportable = X509CertificateLoader.LoadPkcs12(
            cert.Export(X509ContentType.Pfx, PfxPassword),
            PfxPassword,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        File.WriteAllBytes(pfxPath, exportable.Export(X509ContentType.Pfx, PfxPassword));
        return exportable;
    }

    public static int HttpPortFromUrls(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
        {
            return ChatRoutes.DefaultPort;
        }

        foreach (var part in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Uri.TryCreate(part.Replace("0.0.0.0", "127.0.0.1").Replace("+", "127.0.0.1"), UriKind.Absolute, out var uri)
                && uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                return uri.Port;
            }
        }

        return ChatRoutes.DefaultPort;
    }
}
