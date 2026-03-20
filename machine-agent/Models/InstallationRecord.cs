namespace AdagioMachineAgent.Models;

/// <summary>
/// Stores the result of a single installation execution for post-deployment diagnostics.
/// Persisted to JSON for troubleshooting and compliance auditing.
/// </summary>
public sealed record InstallationRecord(
    /// <summary>Unique identifier for this installation record.</summary>
    string RecordId,

    /// <summary>SessionId from the agent session that performed this installation.</summary>
    string SessionId,

    /// <summary>When this installation started (UTC).</summary>
    DateTimeOffset StartedAtUtc,

    /// <summary>When this installation completed or timed out (UTC).</summary>
    DateTimeOffset CompletedAtUtc,

    /// <summary>Duration in milliseconds.</summary>
    long DurationMilliseconds,

    /// <summary>The command executed (e.g., installer path).</summary>
    string Command,

    /// <summary>Command arguments if any.</summary>
    string? Arguments,

    /// <summary>Working directory used for the process.</summary>
    string? WorkingDirectory,

    /// <summary>Whether the installation process exited (vs timed out).</summary>
    bool ProcessExited,

    /// <summary>Exit code if the process exited; null if timed out.</summary>
    int? ExitCode,

    /// <summary>True if outcome is considered successful (exit code match + assertions passed).</summary>
    bool Success,

    /// <summary>Overall result summary (e.g., "Passed", "Failed", "Timed Out").</summary>
    string Outcome,

    /// <summary>Optional error message if installation failed.</summary>
    string? ErrorMessage,

    /// <summary>Path of the log file that was collected (if any).</summary>
    string? LogPath,

    /// <summary>Number of trailing log lines captured.</summary>
    int? LogTailLineCount,

    /// <summary>Number of MSI event log entries captured.</summary>
    int? MsiEventCount,

    /// <summary>Assertions that were checked against the installation result.</summary>
    List<AssertionRecordItem> Assertions);

/// <summary>
/// Records a single assertion and whether it passed or failed.
/// </summary>
public sealed record AssertionRecordItem(
    /// <summary>Type of assertion: ExitCode, PathExists, LogContains.</summary>
    string AssertionType,

    /// <summary>Description/parameters of what was asserted.</summary>
    string Description,

    /// <summary>Whether the assertion passed.</summary>
    bool Passed,

    /// <summary>Detailed message explaining the result.</summary>
    string Message);

/// <summary>
/// Request to query installation history from the agent.
/// </summary>
public sealed record QueryInstallationHistoryRequest(
    /// <summary>Filter by session ID (empty = all sessions).</summary>
    string? SessionId = null,

    /// <summary>Filter by success/failure (null = all outcomes).</summary>
    bool? SuccessOnly = null,

    /// <summary>Maximum number of records to return (0 = all).</summary>
    int MaxResults = 50,

    /// <summary>Include full assertion details or just pass/fail summary.</summary>
    bool IncludeAssertionDetails = true);

/// <summary>
/// Response containing installation history query results.
/// </summary>
public sealed record QueryInstallationHistoryResponse(
    /// <summary>Matching installation records (most recent first).</summary>
    List<InstallationRecord> Records,

    /// <summary>Total records returned.</summary>
    int Count,

    /// <summary>Total records that matched the filter in the database.</summary>
    int TotalAvailable);

/// <summary>
/// Response for exporting installation history.
/// </summary>
public sealed record ExportInstallationHistoryResponse(
    /// <summary>Export format used (json or csv).</summary>
    string Format,

    /// <summary>Exported data as string (JSON array or CSV text).</summary>
    string Data,

    /// <summary>Number of records exported.</summary>
    int RecordCount,

    /// <summary>Size in bytes.</summary>
    int SizeBytes,

    /// <summary>Suggested filename for download.</summary>
    string SuggestedFilename);
