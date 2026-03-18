using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using AdagioMachineAgent.Services;
using Microsoft.OpenApi.Models;
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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Adagio Machine Agent API",
        Version = "v1",
        Description = "Canonical contract is rooted at /api/v1. Legacy unversioned aliases remain for compatibility.",
    });
});
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
SecurityPolicy.ValidateSecurityOptions(securityOptions);

var app = builder.Build();

app.UseSwagger(options =>
{
    options.PreSerializeFilters.Add((swagger, request) =>
    {
        swagger.Servers =
        [
            new OpenApiServer
            {
                Url = "/api/v1",
                Description = "Canonical versioned API base path",
            },
            new OpenApiServer
            {
                Url = "/",
                Description = "Legacy compatibility route aliases",
            },
        ];
    });
});
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Adagio Machine Agent API v1");
    options.RoutePrefix = "swagger";
});

// ── Middleware ────────────────────────────────────────────────────────────────
app.Use(async (context, next) =>
{
    // Support versioned API paths while keeping legacy routes available.
    if (context.Request.Path.StartsWithSegments("/api/v1", out var remainingPath))
    {
        context.Request.Path = remainingPath.HasValue ? remainingPath : "/";
    }

    await next();
});

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
    if (context.Request.Path.StartsWithSegments("/swagger"))
    {
        await next();
        return;
    }

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

    if (!SecurityPolicy.IsApiKeyMatch(suppliedKey.ToString(), securityOptions.ApiKey))
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

static void ConfigureTransportSecurity(WebApplicationBuilder builder, SecurityOptions securityOptions)
{
    SecurityPolicy.ValidateTransportSecurity(
        securityOptions,
        builder.Configuration["Urls"],
        builder.Environment.IsDevelopment());

    if (!securityOptions.RequireHttps ||
        SecurityPolicy.ShouldUseDevelopmentCertificateFallback(securityOptions, builder.Environment.IsDevelopment()))
    {
        return;
    }

    var certificate = SecurityPolicy.LoadHttpsCertificate(
        securityOptions.HttpsCertificatePath,
        securityOptions.HttpsCertificatePassword);

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ConfigureHttpsDefaults(httpsOptions =>
        {
            httpsOptions.ServerCertificate = certificate;
        });
    });
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

// Expose the implicit Program class so integration tests can reference it
// through WebApplicationFactory<Program>.
public partial class Program { }
