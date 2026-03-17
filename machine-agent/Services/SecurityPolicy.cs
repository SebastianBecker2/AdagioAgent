using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AdagioMachineAgent.Services;

/// <summary>
/// Centralized transport and API-key security checks used during startup and request handling.
/// </summary>
public static class SecurityPolicy
{
    public static bool IsApiKeyMatch(string candidate, string configured)
    {
        var candidateBytes = System.Text.Encoding.UTF8.GetBytes(candidate);
        var configuredBytes = System.Text.Encoding.UTF8.GetBytes(configured);

        return candidateBytes.Length == configuredBytes.Length &&
               CryptographicOperations.FixedTimeEquals(candidateBytes, configuredBytes);
    }

    public static void ValidateSecurityOptions(SecurityOptions securityOptions)
    {
        if (!securityOptions.RequireApiKey)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(securityOptions.ApiKey) ||
            string.Equals(securityOptions.ApiKey, "CHANGE_ME", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "SecurityOptions.RequireApiKey is enabled but SecurityOptions.ApiKey is unset or left as CHANGE_ME.");
        }
    }

    public static bool ShouldUseDevelopmentCertificateFallback(SecurityOptions securityOptions, bool isDevelopment)
    {
        return isDevelopment &&
               securityOptions.AllowDevelopmentCertificateFallback &&
               string.IsNullOrWhiteSpace(securityOptions.HttpsCertificatePath);
    }

    public static void ValidateTransportSecurity(
        SecurityOptions securityOptions,
        string? configuredUrls,
        bool isDevelopment)
    {
        if (!securityOptions.RequireHttps)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(configuredUrls))
        {
            var urls = configuredUrls
                .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (urls.Any(url => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "SecurityOptions.RequireHttps is enabled but 'Urls' contains an HTTP endpoint. " +
                    "Use only HTTPS URLs when RequireHttps is true.");
            }
        }

        if (string.IsNullOrWhiteSpace(securityOptions.HttpsCertificatePath) &&
            !ShouldUseDevelopmentCertificateFallback(securityOptions, isDevelopment))
        {
            throw new InvalidOperationException(
                "SecurityOptions.RequireHttps is enabled but no certificate path is configured. " +
                "Set SecurityOptions.HttpsCertificatePath and SecurityOptions.HttpsCertificatePassword.");
        }
    }

    public static X509Certificate2 LoadHttpsCertificate(string certificatePath, string? certificatePassword)
    {
        if (string.IsNullOrWhiteSpace(certificatePath))
        {
            throw new InvalidOperationException(
                "SecurityOptions.HttpsCertificatePath must be set when HTTPS is required.");
        }

        var fullPath = Path.GetFullPath(certificatePath);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"Configured HTTPS certificate file was not found: '{fullPath}'.");
        }

        try
        {
            return new X509Certificate2(fullPath, certificatePassword);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to load HTTPS certificate from '{fullPath}'. Verify the certificate path and password.",
                ex);
        }
    }
}
