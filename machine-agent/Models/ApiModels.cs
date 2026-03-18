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

/// <summary>Collect install diagnostics: wait result, optional log tail, and optional MSI event-log entries.</summary>
public sealed record CollectInstallArtifactsRequest(
    int Pid,
    int TimeoutMilliseconds = 30000,
    string? LogPath = null,
    int TailLines = 200,
    bool IncludeMsiEvents = true,
    int EventEntryCount = 20);

/// <summary>Launch an installer process and collect diagnostics after it exits or times out.</summary>
public sealed record RunInstallerAndCollectArtifactsRequest(
    string Command,
    string? Arguments = null,
    string? WorkingDirectory = null,
    int TimeoutMilliseconds = 30000,
    string? LogPath = null,
    int TailLines = 200,
    bool IncludeMsiEvents = true,
    int EventEntryCount = 20);

/// <summary>Launch an installer, collect artifacts, and evaluate common workflow assertions.</summary>
public sealed record RunInstallerAndAssertRequest(
    string Command,
    string? Arguments = null,
    string? WorkingDirectory = null,
    int TimeoutMilliseconds = 30000,
    string? LogPath = null,
    int TailLines = 200,
    bool IncludeMsiEvents = true,
    int EventEntryCount = 20,
    int? ExpectedExitCode = null,
    string? ExpectedPath = null,
    bool ExpectedPathMustBeDirectory = false,
    string? LogMustContainText = null,
    bool LogContainsIgnoreCase = true);

/// <summary>Terminate a tracked process.</summary>
public sealed record TerminateProcessRequest(int Pid);

/// <summary>Read a text file from the target machine.</summary>
public sealed record ReadTextFileRequest(string Path);

/// <summary>Read the last N lines from a text file.</summary>
public sealed record TailFileRequest(string Path, int Lines = 200);

/// <summary>List files and directories under a target directory.</summary>
public sealed record ListDirectoryRequest(string Path);

/// <summary>Check whether a file or directory exists at a path.</summary>
public sealed record FileExistsRequest(string Path);

/// <summary>Assert that a tracked process exits (and optionally with a specific exit code).</summary>
public sealed record AssertProcessExitedRequest(
    int Pid,
    int TimeoutMilliseconds = 30000,
    int? ExpectedExitCode = null);

/// <summary>Assert that a path exists (and optionally is a directory).</summary>
public sealed record AssertPathExistsRequest(string Path, bool MustBeDirectory = false);

/// <summary>Assert that a text file contains an expected fragment.</summary>
public sealed record AssertLogContainsRequest(
    string Path,
    string ContainsText,
    bool IgnoreCase = false);

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

/// <summary>Press a key combination in the focused application window.</summary>
public sealed record PressHotkeyRequest(int Pid, List<string> Keys);

/// <summary>Toggle a checkbox or radio button to the specified checked state.</summary>
public sealed record SetCheckboxRequest(int Pid, string ElementId, bool IsChecked);

/// <summary>Select an option in a combo box or list by text or by zero-based index.</summary>
public sealed record SelectOptionRequest(
    int Pid,
    string ElementId,
    string? OptionText = null,
    int? OptionIndex = null);

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
public sealed record HealthResponse(
    string Status,
    string Version,
    int ApiVersion,
    string? MinSupportedClientVersion = null,
    string? MaxSupportedClientVersion = null);

/// <summary>Readiness response used for install/bootstrap verification.</summary>
public sealed record ReadinessResponse(
    string Status,
    string Version,
    int ApiVersion,
    string Platform,
    bool UiAutomationAvailable,
    List<string> Issues);

/// <summary>Summarized startup/runtime diagnostics for support and onboarding.</summary>
public sealed record DiagnosticsStatusResponse(
    string Status,
    string Version,
    int ApiVersion,
    string Platform,
    bool UiAutomationAvailable,
    List<string> Issues,
    int RunningProcessCount,
    int TrackedProcessCount,
    DateTimeOffset TimestampUtc);

/// <summary>Problem details returned on error (RFC 7807-style).</summary>
public sealed record ErrorResponse(string Error, string? Detail = null);

/// <summary>Result of copying a file.</summary>
public sealed record CopyFileResponse(string DestinationPath, int BytesWritten);

/// <summary>Result of waiting for a process to exit.</summary>
public sealed record WaitForExitResponse(bool Exited, ProcessStatusResponse Process);

/// <summary>A single installer-related event log entry.</summary>
public sealed record InstallEventLogEntry(
    DateTimeOffset TimeCreated,
    int EventId,
    string Level,
    string Source,
    string Message);

/// <summary>Text file content read from the target machine.</summary>
public sealed record ReadTextFileResponse(string Path, string Content);

/// <summary>Last lines read from a text file.</summary>
public sealed record TailFileResponse(string Path, int Lines, string Content);

/// <summary>A single filesystem entry in a directory listing.</summary>
public sealed record DirectoryEntry(string Name, string Path, bool IsDirectory);

/// <summary>Directory listing response.</summary>
public sealed record ListDirectoryResponse(string Path, List<DirectoryEntry> Entries);

/// <summary>Path existence response.</summary>
public sealed record FileExistsResponse(string Path, bool Exists, bool IsDirectory);

/// <summary>Result of a boolean assertion check.</summary>
public sealed record AssertionResponse(bool Passed, string Message);

/// <summary>Combined installer artifact collection response.</summary>
public sealed record CollectInstallArtifactsResponse(
    bool Exited,
    ProcessStatusResponse Process,
    TailFileResponse? LogTail,
    List<InstallEventLogEntry> MsiEvents,
    List<string> Warnings);

/// <summary>Combined installer launch and artifact collection response.</summary>
public sealed record RunInstallerAndCollectArtifactsResponse(
    int Pid,
    DateTimeOffset StartedAt,
    CollectInstallArtifactsResponse Artifacts);

/// <summary>Combined installer launch, artifacts, and assertion summary.</summary>
public sealed record RunInstallerAndAssertResponse(
    int Pid,
    DateTimeOffset StartedAt,
    CollectInstallArtifactsResponse Artifacts,
    List<AssertionResponse> Assertions,
    bool Passed);

/// <summary>Result of waiting for a UI element.</summary>
public sealed record WaitForElementResponse(bool Found, ElementStateResponse? Element);
