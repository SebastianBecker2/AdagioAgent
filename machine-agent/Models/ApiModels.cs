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

/// <summary>Copy a file to the target system.</summary>
public sealed record CopyFileRequest(
    string DestinationPath,
    string FileContentBase64,
    bool OverwriteIfExists = false);

/// <summary>Wait for a tracked process to exit.</summary>
public sealed record WaitForExitRequest(int Pid, int TimeoutMilliseconds = 30000);

/// <summary>Terminate a tracked process.</summary>
public sealed record TerminateProcessRequest(int Pid);

/// <summary>Read a text file from the target machine.</summary>
public sealed record ReadTextFileRequest(string Path);

/// <summary>Read the last N lines from a text file.</summary>
public sealed record TailFileRequest(string Path, int Lines = 200);

/// <summary>Get the current state of a UI element.</summary>
public sealed record ElementStateRequest(int Pid, string ElementId);

/// <summary>Wait until a UI element appears or timeout is reached.</summary>
public sealed record WaitForElementRequest(
    int Pid,
    string ElementId,
    int TimeoutMilliseconds = 30000,
    int PollIntervalMilliseconds = 250);

/// <summary>Focus a UI element.</summary>
public sealed record SetFocusRequest(int Pid, string ElementId);

/// <summary>Send keystrokes to the focused application window.</summary>
public sealed record SendKeysRequest(int Pid, string Text);

// ─── Responses ────────────────────────────────────────────────────────────────

/// <summary>Result of starting a process.</summary>
public sealed record RunResponse(int Pid, string Status, DateTimeOffset StartedAt);

/// <summary>Current state of a tracked process.</summary>
public sealed record ProcessStatusResponse(
    int Pid,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? ExitedAt,
    int? ExitCode);

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

/// <summary>Snapshot of a single UI element.</summary>
public sealed record ElementStateResponse(
    string Id,
    string Type,
    string Name,
    string AutomationId,
    Bounds? Bounds,
    bool Available);

/// <summary>Screenshot of the window as base64-encoded PNG.</summary>
public sealed record ScreenshotResponse(string ImageBase64);

/// <summary>Generic status response.</summary>
public sealed record StatusResponse(string Status, string? Message = null);

/// <summary>Health check response.</summary>
public sealed record HealthResponse(string Status, string Version);

/// <summary>Problem details returned on error (RFC 7807-style).</summary>
public sealed record ErrorResponse(string Error, string? Detail = null);

/// <summary>Result of copying a file.</summary>
public sealed record CopyFileResponse(string DestinationPath, int BytesWritten);

/// <summary>Result of waiting for a process to exit.</summary>
public sealed record WaitForExitResponse(bool Exited, ProcessStatusResponse Process);

/// <summary>Text file content read from the target machine.</summary>
public sealed record ReadTextFileResponse(string Path, string Content);

/// <summary>Last lines read from a text file.</summary>
public sealed record TailFileResponse(string Path, int Lines, string Content);

/// <summary>Result of waiting for a UI element.</summary>
public sealed record WaitForElementResponse(bool Found, ElementStateResponse? Element);
