using AdagioMachineAgent.Services;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AdagioMachineAgent.Tests;

public sealed class SecurityPolicyTests
{
    [Fact]
    public void ValidateSecurityOptions_Throws_WhenApiKeyMissingOrPlaceholder()
    {
        var missing = new SecurityOptions
        {
            RequireApiKey = true,
            ApiKey = string.Empty,
        };

        var placeholder = new SecurityOptions
        {
            RequireApiKey = true,
            ApiKey = "CHANGE_ME",
        };

        Assert.Throws<InvalidOperationException>(() => SecurityPolicy.ValidateSecurityOptions(missing));
        Assert.Throws<InvalidOperationException>(() => SecurityPolicy.ValidateSecurityOptions(placeholder));
    }

    [Fact]
    public void ValidateSecurityOptions_AllowsMissingApiKey_WhenAuthDisabled()
    {
        var options = new SecurityOptions
        {
            RequireApiKey = false,
            ApiKey = string.Empty,
        };

        SecurityPolicy.ValidateSecurityOptions(options);
    }

    [Fact]
    public void IsApiKeyMatch_ReturnsExpectedResults()
    {
        Assert.True(SecurityPolicy.IsApiKeyMatch("secret", "secret"));
        Assert.False(SecurityPolicy.IsApiKeyMatch("secret", "Secret"));
        Assert.False(SecurityPolicy.IsApiKeyMatch("secret", "secret-extra"));
    }

    [Fact]
    public void ValidateTransportSecurity_Throws_WhenHttpUrlConfiguredAndHttpsRequired()
    {
        var options = new SecurityOptions
        {
            RequireHttps = true,
            HttpsCertificatePath = "C:/certs/agent.pfx",
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SecurityPolicy.ValidateTransportSecurity(options, "http://127.0.0.1:5000", isDevelopment: false));

        Assert.Contains("contains an HTTP endpoint", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateTransportSecurity_Throws_WhenCertPathMissingOutsideAllowedDevFallback()
    {
        var options = new SecurityOptions
        {
            RequireHttps = true,
            HttpsCertificatePath = string.Empty,
            AllowDevelopmentCertificateFallback = false,
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SecurityPolicy.ValidateTransportSecurity(options, "https://127.0.0.1:5443", isDevelopment: false));

        Assert.Contains("no certificate path is configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateTransportSecurity_AllowsDevFallback_WhenEnabled()
    {
        var options = new SecurityOptions
        {
            RequireHttps = true,
            HttpsCertificatePath = string.Empty,
            AllowDevelopmentCertificateFallback = true,
        };

        SecurityPolicy.ValidateTransportSecurity(options, "https://127.0.0.1:5443", isDevelopment: true);
    }

    [Fact]
    public void LoadHttpsCertificate_Throws_WhenFileMissing()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SecurityPolicy.LoadHttpsCertificate(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pfx"), "pw"));

        Assert.Contains("certificate file was not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadHttpsCertificate_LoadsValidPfx()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=AdagioMachineAgentTest",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));

        var tempPath = Path.Combine(Path.GetTempPath(), $"adagio-agent-{Guid.NewGuid():N}.pfx");
        const string password = "test-password";

        try
        {
            var bytes = cert.Export(X509ContentType.Pfx, password);
            File.WriteAllBytes(tempPath, bytes);

            using var loaded = SecurityPolicy.LoadHttpsCertificate(tempPath, password);
            Assert.Contains("AdagioMachineAgentTest", loaded.Subject, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
