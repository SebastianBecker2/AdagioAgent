using System.Runtime.InteropServices;
using AdagioMachineAgent.Services;

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

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseRouting();
app.MapControllers();

app.Run();

// ─── Configuration model ──────────────────────────────────────────────────────

/// <summary>Agent runtime configuration (appsettings.json / env vars).</summary>
public sealed class AgentOptions
{
    /// <summary>Directories from which executables are allowed to run.</summary>
    public List<string> AllowedExecutablePaths { get; set; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ["C:\\Apps"]
            : ["/usr/local/bin"];

    /// <summary>Seconds before a managed process is forcibly killed.</summary>
    public int ProcessTimeoutSeconds { get; set; } = 300;

    /// <summary>Maximum number of simultaneously tracked processes.</summary>
    public int MaxConcurrentProcesses { get; set; } = 5;
}
