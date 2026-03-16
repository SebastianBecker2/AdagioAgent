namespace AdagioMachineAgent.Models;

// ─── Requests ─────────────────────────────────────────────────────────────────

/// <summary>Start a process on the VM.</summary>
public sealed record RunRequest(
    string Command,
    string? Arguments,
    string? WorkingDirectory);

/// <summary>Click a UI element by its element ID.</summary>
public sealed record ClickRequest(int Pid, string ElementId);

/// <summary>Type text into a UI element.</summary>
public sealed record TypeRequest(int Pid, string ElementId, string Text);

// ─── Responses ────────────────────────────────────────────────────────────────

/// <summary>Result of starting a process.</summary>
public sealed record RunResponse(int Pid, string Status, DateTimeOffset StartedAt);

/// <summary>Bounding rectangle of a UI element.</summary>
public sealed record Bounds(int X, int Y, int Width, int Height);

/// <summary>A single element in the UI automation tree.</summary>
public sealed record UiElement(
    string Id,
    string Type,
    string Name,
    string AutomationId,
    Bounds? Bounds,
    List<UiElement>? Children);

/// <summary>UI element tree for a window.</summary>
public sealed record UiTreeResponse(string WindowTitle, List<UiElement> Elements);

/// <summary>Screenshot of the window as base64-encoded PNG.</summary>
public sealed record ScreenshotResponse(string ImageBase64);

/// <summary>Generic status response.</summary>
public sealed record StatusResponse(string Status, string? Message = null);

/// <summary>Health check response.</summary>
public sealed record HealthResponse(string Status, string Version);

/// <summary>Problem details returned on error (RFC 7807-style).</summary>
public sealed record ErrorResponse(string Error, string? Detail = null);
