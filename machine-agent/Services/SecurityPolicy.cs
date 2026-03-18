using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AdagioMachineAgent.Services;

/// <summary>
/// Centralized transport and API-key security checks used during startup and request handling.
/// </summary>
public static class SecurityPolicy
{
    public const int RecommendedApiKeyMinLength = 24;
    public const int CertificateExpiryWarningDays = 30;

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

    public static List<string> GetReadinessIssues(
        SecurityOptions securityOptions,
        global::AgentOptions agentOptions,
        DateTimeOffset nowUtc)
    {
        var issues = new List<string>();

        issues.AddRange(GetSecurityReadinessIssues(securityOptions, nowUtc));
        issues.AddRange(GetAgentPolicyReadinessIssues(agentOptions));

        return issues;
    }

    private static List<string> GetSecurityReadinessIssues(SecurityOptions options, DateTimeOffset nowUtc)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKeyHeaderName))
        {
            issues.Add("ApiKeyHeaderName is required.");
        }

        if (options.RequireApiKey)
        {
            if (string.IsNullOrWhiteSpace(options.ApiKey) ||
                string.Equals(options.ApiKey, "CHANGE_ME", StringComparison.Ordinal))
            {
                issues.Add("API key authentication is required but ApiKey is unset.");
            }
            else if (options.ApiKey.Trim().Length < RecommendedApiKeyMinLength)
            {
                issues.Add($"API key is shorter than the recommended minimum length ({RecommendedApiKeyMinLength}).");
            }
        }

        if (!options.RequireHttps)
        {
            return issues;
        }

        if (string.IsNullOrWhiteSpace(options.HttpsCertificatePath))
        {
            issues.Add("HTTPS is required but no certificate path is configured.");
            return issues;
        }

        var fullPath = Path.GetFullPath(options.HttpsCertificatePath);
        if (!File.Exists(fullPath))
        {
            issues.Add($"Configured HTTPS certificate file was not found: '{fullPath}'.");
            return issues;
        }

        DateTime expiresAt;
        try
        {
            using var certificate = new X509Certificate2(fullPath, options.HttpsCertificatePassword);
            expiresAt = certificate.NotAfter;
        }
        catch
        {
            issues.Add($"Failed to load HTTPS certificate from '{fullPath}'. Verify path and password.");
            return issues;
        }

        if (expiresAt <= nowUtc.UtcDateTime)
        {
            issues.Add($"HTTPS certificate has expired ({expiresAt:O}).");
            return issues;
        }

        if (expiresAt <= nowUtc.UtcDateTime.AddDays(CertificateExpiryWarningDays))
        {
            issues.Add($"HTTPS certificate expires soon ({expiresAt:O}).");
        }

        return issues;
    }

    private static List<string> GetAgentPolicyReadinessIssues(global::AgentOptions options)
    {
        var issues = new List<string>();

        if (options.AllowedExecutablePaths.Count == 0)
        {
            issues.Add("AllowedExecutablePaths is empty.");
        }

        if (options.AllowedReadablePaths.Count == 0)
        {
            issues.Add("AllowedReadablePaths is empty.");
        }

        if (options.AllowedWritablePaths.Count == 0)
        {
            issues.Add("AllowedWritablePaths is empty.");
        }

        if (options.ProcessTimeoutSeconds <= 0)
        {
            issues.Add("ProcessTimeoutSeconds must be greater than zero.");
        }

        if (options.MaxConcurrentProcesses <= 0)
        {
            issues.Add("MaxConcurrentProcesses must be greater than zero.");
        }

        return issues;
    }
}
