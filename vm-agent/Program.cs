using AdagioVmAgent.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddSingleton<ProcessService>();
builder.Services.AddSingleton<UiAutomationService>();

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
    /// <summary>Directories from which installers are allowed to run.</summary>
    public List<string> AllowedInstallerPaths { get; set; } = ["C:\\Installers"];

    /// <summary>Seconds before a managed process is forcibly killed.</summary>
    public int ProcessTimeoutSeconds { get; set; } = 300;

    /// <summary>Maximum number of simultaneously tracked processes.</summary>
    public int MaxConcurrentProcesses { get; set; } = 5;
}
