using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
using System.Text.Json;
using AdagioMachineAgent.Models;
using AdagioMachineAgent.Services;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Support running as a Windows Service (no-op when launched as a console app).
#if WINDOWS
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "AdagioMachineAgent";
});
builder.Logging.AddEventLog();
#endif

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var correlationId = ResolveCorrelationId(context.HttpContext);
            var details = context.ModelState
                .Where(pair => pair.Value?.Errors.Count > 0)
                .SelectMany(pair => pair.Value!.Errors.Select(error =>
                    string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? $"{pair.Key}: invalid value"
                        : $"{pair.Key}: {error.ErrorMessage}"))
                .ToList();

            var detailText = details.Count == 0 ? null : string.Join("; ", details);
            return new BadRequestObjectResult(new ErrorResponse(
                Error: "Request validation failed.",
                Detail: detailText,
                CorrelationId: correlationId,
                ErrorCode: AgentErrorCodes.ValidationFailed,
                RemediationHint: "Fix the request parameters/body to match the API contract and retry."));
        };
    });
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
try
{
    ConfigureTransportSecurity(builder, securityOptions);
    SecurityPolicy.ValidateSecurityOptions(securityOptions);
}
catch (Exception ex)
{
    WriteStartupFailureDiagnostics(ex, securityOptions);
    throw;
}

var app = builder.Build();

app.Lifetime.ApplicationStopping.Register(() =>
{
    using var scope = app.Services.CreateScope();
    var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
    var terminated = processService.TerminateAllRunningProcesses("ApplicationStopping");
    var pruned = processService.PruneExitedProcesses();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Lifecycle");
    logger.LogInformation(
        "Application stopping cleanup completed. Terminated={Terminated} Pruned={Pruned}",
        terminated,
        pruned);
});

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
    const string correlationHeader = "X-Correlation-ID";
    var correlationId = context.Request.Headers.TryGetValue(correlationHeader, out var supplied)
        && !string.IsNullOrWhiteSpace(supplied.ToString())
        ? supplied.ToString()
        : context.TraceIdentifier;

    context.Items[correlationHeader] = correlationId;
    context.Response.Headers[correlationHeader] = correlationId;

    await next();
});

app.Use(async (context, next) =>
{
    var logger = context.RequestServices
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("RequestLogging");
    var stopwatch = Stopwatch.StartNew();
    var correlationId = ResolveCorrelationId(context);

    try
    {
        await next();
    }
    finally
    {
        stopwatch.Stop();
        logger.LogInformation(
            "Request completed Method={Method} Path={Path} StatusCode={StatusCode} DurationMs={DurationMs} CorrelationId={CorrelationId}",
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds,
            correlationId);
    }
});

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GlobalException");
        var correlationId = ResolveCorrelationId(context);

        logger.LogError(ex,
            "Unhandled exception for request {Method} {Path}. CorrelationId={CorrelationId}",
            context.Request.Method,
            context.Request.Path.Value,
            correlationId);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ErrorResponse(
            Error: "An unexpected error occurred.",
            Detail: app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")
                ? ex.Message
                : null,
            CorrelationId: correlationId,
            ErrorCode: AgentErrorCodes.InternalError,
            RemediationHint: "Retry the request. If the issue persists, capture diagnostics and contact support."));
    }
});

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
        await context.Response.WriteAsJsonAsync(new ErrorResponse(
            Error: "Server API key is not configured.",
            CorrelationId: ResolveCorrelationId(context),
            ErrorCode: AgentErrorCodes.InternalError,
            RemediationHint: "Configure SecurityOptions:ApiKey in appsettings and restart the service."));
        return;
    }

    if (!context.Request.Headers.TryGetValue(securityOptions.ApiKeyHeaderName, out var suppliedKey))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new ErrorResponse(
            Error: $"Missing required header '{securityOptions.ApiKeyHeaderName}'.",
            CorrelationId: ResolveCorrelationId(context),
            ErrorCode: AgentErrorCodes.Unauthorized,
            RemediationHint: $"Send a valid API key in the '{securityOptions.ApiKeyHeaderName}' header."));
        return;
    }

    if (!SecurityPolicy.IsApiKeyMatch(suppliedKey.ToString(), securityOptions.ApiKey))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new ErrorResponse(
            Error: "Invalid API key.",
            CorrelationId: ResolveCorrelationId(context),
            ErrorCode: AgentErrorCodes.Unauthorized,
            RemediationHint: "Verify the API key value and retry the request."));
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

static string ResolveCorrelationId(HttpContext context)
{
    const string correlationHeader = "X-Correlation-ID";
    if (context.Items.TryGetValue(correlationHeader, out var value) && value is string id && !string.IsNullOrWhiteSpace(id))
    {
        return id;
    }

    return context.TraceIdentifier;
}

static void WriteStartupFailureDiagnostics(Exception ex, SecurityOptions securityOptions)
{
    try
    {
        var diagnosticsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AdagioMachineAgent");
        Directory.CreateDirectory(diagnosticsRoot);

        var startupFailurePath = Path.Combine(diagnosticsRoot, "startup-failure.json");
        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("u"),
            error = ex.Message,
            exceptionType = ex.GetType().FullName,
            requireHttps = securityOptions.RequireHttps,
            certificatePath = securityOptions.HttpsCertificatePath,
            requireApiKey = securityOptions.RequireApiKey,
        };

        File.WriteAllText(startupFailurePath, JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }
    catch
    {
        // Best-effort diagnostics only; do not mask the original startup error.
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

// Expose the implicit Program class so integration tests can reference it
// through WebApplicationFactory<Program>.
public partial class Program { }
