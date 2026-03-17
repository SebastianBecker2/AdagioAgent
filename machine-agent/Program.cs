using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AdagioMachineAgent.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Support running as a Windows Service (no-op when launched as a console app).
#if WINDOWS
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "AdagioMachineAgent";
});
#endif

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddSingleton<ProcessService>();

// Register the correct UI-automation backend for the host platform.
#if WINDOWS
builder.Services.AddSingleton<IUiAutomationService, WindowsUiAutomationService>();
#elif LINUX
builder.Services.AddSingleton<IUiAutomationService, LinuxUiAutomationService>();
#else
builder.Services.AddSingleton<IUiAutomationService>(_ =>
    throw new PlatformNotSupportedException(
        $"Platform '{RuntimeInformation.OSDescription}' is not supported. " +
        "UI automation is only available on Windows (FlaUI/UIA3) and Linux (AT-SPI2)."));
#endif

// ── Configuration ─────────────────────────────────────────────────────────────
// Bind optional "AgentOptions" section from appsettings.json
builder.Services.Configure<AgentOptions>(
    builder.Configuration.GetSection("AgentOptions"));
var securityOptionsSection = builder.Configuration.GetSection("SecurityOptions");
builder.Services.Configure<SecurityOptions>(
    securityOptionsSection);

var securityOptions = securityOptionsSection.Get<SecurityOptions>() ?? new SecurityOptions();
ConfigureTransportSecurity(builder, securityOptions);
ValidateSecurityOptions(securityOptions);

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────
if (securityOptions.RequireHttps)
{
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseHttpsRedirection();
}
app.UseRouting();

app.Use(async (context, next) =>
{
    var securityOptions = context.RequestServices
        .GetRequiredService<IOptions<SecurityOptions>>()
        .Value;

    if (!securityOptions.RequireApiKey)
    {
        await next();
        return;
    }

    if (string.IsNullOrWhiteSpace(securityOptions.ApiKey))
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Server API key is not configured.",
        });
        return;
    }

    if (!context.Request.Headers.TryGetValue(securityOptions.ApiKeyHeaderName, out var suppliedKey))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            error = $"Missing required header '{securityOptions.ApiKeyHeaderName}'.",
        });
        return;
    }

    if (!IsApiKeyMatch(suppliedKey.ToString(), securityOptions.ApiKey))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Invalid API key.",
        });
        return;
    }

    await next();
});

app.MapControllers();

app.Run();

static bool IsApiKeyMatch(string candidate, string configured)
{
    var candidateBytes = Encoding.UTF8.GetBytes(candidate);
    var configuredBytes = Encoding.UTF8.GetBytes(configured);

    return candidateBytes.Length == configuredBytes.Length &&
           CryptographicOperations.FixedTimeEquals(candidateBytes, configuredBytes);
}

static void ConfigureTransportSecurity(WebApplicationBuilder builder, SecurityOptions securityOptions)
{
    if (!securityOptions.RequireHttps)
    {
        return;
    }

    var configuredUrls = builder.Configuration["Urls"];
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

    if (string.IsNullOrWhiteSpace(securityOptions.HttpsCertificatePath))
    {
        if (builder.Environment.IsDevelopment() && securityOptions.AllowDevelopmentCertificateFallback)
        {
            return;
        }

        throw new InvalidOperationException(
            "SecurityOptions.RequireHttps is enabled but no certificate path is configured. " +
            "Set SecurityOptions.HttpsCertificatePath and SecurityOptions.HttpsCertificatePassword.");
    }

    var certificatePath = Path.GetFullPath(securityOptions.HttpsCertificatePath);
    if (!File.Exists(certificatePath))
    {
        throw new InvalidOperationException(
            $"Configured HTTPS certificate file was not found: '{certificatePath}'.");
    }

    X509Certificate2 certificate;
    try
    {
        certificate = new X509Certificate2(certificatePath, securityOptions.HttpsCertificatePassword);
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException(
            $"Failed to load HTTPS certificate from '{certificatePath}'. " +
            "Verify the certificate path and password.",
            ex);
    }

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ConfigureHttpsDefaults(httpsOptions =>
        {
            httpsOptions.ServerCertificate = certificate;
        });
    });
}

static void ValidateSecurityOptions(SecurityOptions securityOptions)
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

// ─── Configuration model ──────────────────────────────────────────────────────

/// <summary>Agent runtime configuration (appsettings.json / env vars).</summary>
public sealed class AgentOptions
{
    /// <summary>Directories from which executables are allowed to run.</summary>
    public List<string> AllowedExecutablePaths { get; set; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ["C:\\Apps"]
            : ["/usr/local/bin"];

    /// <summary>Directories where the agent may write files.</summary>
    public List<string> AllowedWritablePaths { get; set; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ["C:\\Apps"]
            : ["/tmp"];

    /// <summary>Directories where the agent may read files/logs.</summary>
    public List<string> AllowedReadablePaths { get; set; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ["C:\\Apps"]
            : ["/var/log", "/tmp"];

    /// <summary>Seconds before a managed process is forcibly killed.</summary>
    public int ProcessTimeoutSeconds { get; set; } = 300;

    /// <summary>Maximum number of simultaneously tracked processes.</summary>
    public int MaxConcurrentProcesses { get; set; } = 5;
}

/// <summary>Transport and authentication settings for the REST API.</summary>
public sealed class SecurityOptions
{
    /// <summary>Whether HTTPS is required for all endpoints.</summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>Path to the HTTPS server certificate (.pfx).</summary>
    public string HttpsCertificatePath { get; set; } = string.Empty;

    /// <summary>Password for the HTTPS server certificate file.</summary>
    public string HttpsCertificatePassword { get; set; } = string.Empty;

    /// <summary>Allow using dev certificate when explicit cert is not configured.</summary>
    public bool AllowDevelopmentCertificateFallback { get; set; } = false;

    /// <summary>Whether every API request must include a valid API key header.</summary>
    public bool RequireApiKey { get; set; } = true;

    /// <summary>Header name used to carry the API key.</summary>
    public string ApiKeyHeaderName { get; set; } = "X-API-Key";

    /// <summary>The expected API key value.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
