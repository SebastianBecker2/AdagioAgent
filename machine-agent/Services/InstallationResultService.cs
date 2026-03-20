using System.Text.Json;
using System.Text.Json.Serialization;
using AdagioMachineAgent.Models;

namespace AdagioMachineAgent.Services;

/// <summary>
/// Manages persistence and querying of installation execution records.
/// Stores results to a JSON file in the configuration directory for post-deployment diagnostics.
/// </summary>
public sealed class InstallationResultService : IDisposable
{
    private readonly string _storagePath;
    private readonly ILogger<InstallationResultService> _logger;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

    /// <summary>In-memory cache of records for fast queries; synced to disk on write.</summary>
    private List<InstallationRecord> _records = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public InstallationResultService(ILogger<InstallationResultService> logger)
    {
        _logger = logger;

        // Store in a predictable location: agent working directory / installation-history.json
        var baseDir = AppContext.BaseDirectory;
        _storagePath = Path.Combine(baseDir, "installation-history.json");

        _logger.LogInformation("Installation result service initialized. Storage: {Path}", _storagePath);

        // Load existing records from disk on startup.
        LoadRecords();
    }

    /// <summary>
    /// Record the result of an installation execution.
    /// </summary>
    public async Task RecordInstallationAsync(
        string sessionId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string command,
        string? arguments,
        string? workingDirectory,
        bool processExited,
        int? exitCode,
        bool success,
        string outcome,
        string? errorMessage,
        string? logPath,
        int? logTailLineCount,
        int? msiEventCount,
        List<AssertionRecordItem> assertions)
    {
        try
        {
            await _lock.WaitAsync();

            var record = new InstallationRecord(
                RecordId: Guid.NewGuid().ToString("N"),
                SessionId: sessionId,
                StartedAtUtc: startedAt,
                CompletedAtUtc: completedAt,
                DurationMilliseconds: (long)(completedAt - startedAt).TotalMilliseconds,
                Command: command,
                Arguments: arguments,
                WorkingDirectory: workingDirectory,
                ProcessExited: processExited,
                ExitCode: exitCode,
                Success: success,
                Outcome: outcome,
                ErrorMessage: errorMessage,
                LogPath: logPath,
                LogTailLineCount: logTailLineCount,
                MsiEventCount: msiEventCount,
                Assertions: assertions);

            _records.Add(record);

            // Keep only the last 500 records to avoid unbounded growth.
            if (_records.Count > 500)
            {
                _records = _records.TakeLast(500).ToList();
            }

            await SaveRecordsAsync();

            _logger.LogInformation(
                "Recorded installation: RecordId={RecordId}, Command={Command}, Success={Success}, Duration={DurationMs}ms",
                record.RecordId,
                record.Command,
                record.Success,
                record.DurationMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record installation result.");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Query installation history with optional filters.
    /// </summary>
    public async Task<QueryInstallationHistoryResponse> QueryHistoryAsync(
        QueryInstallationHistoryRequest request)
    {
        try
        {
            await _lock.WaitAsync();

            var results = _records.AsEnumerable();

            // Apply filters
            if (!string.IsNullOrWhiteSpace(request.SessionId))
            {
                results = results.Where(r => string.Equals(r.SessionId, request.SessionId, StringComparison.Ordinal));
            }

            if (request.SuccessOnly.HasValue)
            {
                results = results.Where(r => r.Success == request.SuccessOnly.Value);
            }

            // Sort most recent first and apply limit
            var filtered = results
                .OrderByDescending(r => r.CompletedAtUtc)
                .ToList();

            var total = filtered.Count;
            var limited = request.MaxResults > 0
                ? filtered.Take(request.MaxResults).ToList()
                : filtered;

            // Optionally strip assertion details for privacy/brevity
            if (!request.IncludeAssertionDetails)
            {
                limited = limited
                    .Select(r => r with { Assertions = [] })
                    .ToList();
            }

            return new QueryInstallationHistoryResponse(limited, limited.Count, total);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Export installation history as JSON or CSV.
    /// </summary>
    public async Task<ExportInstallationHistoryResponse> ExportHistoryAsync(string format)
    {
        try
        {
            await _lock.WaitAsync();

            return format.ToLowerInvariant() switch
            {
                "json" => await ExportAsJsonAsync(),
                "csv" => await ExportAsCsvAsync(),
                _ => throw new ArgumentException($"Unsupported export format: {format}. Use 'json' or 'csv'.")
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Clear all installation records (for testing/reset).
    /// </summary>
    public async Task ClearHistoryAsync()
    {
        try
        {
            await _lock.WaitAsync();
            _records.Clear();
            await SaveRecordsAsync();
            _logger.LogWarning("Installation history cleared.");
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void LoadRecords()
    {
        if (!File.Exists(_storagePath))
        {
            _logger.LogInformation("No existing installation history file found; starting fresh.");
            return;
        }

        try
        {
            var json = File.ReadAllText(_storagePath);
            _records = JsonSerializer.Deserialize<List<InstallationRecord>>(json, JsonOptions) ?? [];
            _logger.LogInformation("Loaded {Count} installation records from {Path}.", _records.Count, _storagePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load installation history from {Path}; starting fresh.", _storagePath);
            _records = [];
        }
    }

    private async Task SaveRecordsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_records, JsonOptions);
            await File.WriteAllTextAsync(_storagePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist installation records to {Path}.", _storagePath);
        }
    }

    private Task<ExportInstallationHistoryResponse> ExportAsJsonAsync()
    {
        var json = JsonSerializer.Serialize(_records, JsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetByteCount(json);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");

        var response = new ExportInstallationHistoryResponse(
            Format: "json",
            Data: json,
            RecordCount: _records.Count,
            SizeBytes: bytes,
            SuggestedFilename: $"installation-history-{timestamp}.json");

        return Task.FromResult(response);
    }

    private Task<ExportInstallationHistoryResponse> ExportAsCsvAsync()
    {
        var csv = new System.Text.StringBuilder();

        // CSV header
        csv.AppendLine("RecordId,SessionId,StartedAtUtc,CompletedAtUtc,DurationMs,Command,Success,Outcome,ExitCode,LogPath,AssertionCount");

        // CSV rows
        foreach (var record in _records.OrderByDescending(r => r.CompletedAtUtc))
        {
            var command = CsvEscape(record.Command);
            var errorMsg = CsvEscape(record.ErrorMessage ?? "");
            var logPath = CsvEscape(record.LogPath ?? "");
            var outcome = CsvEscape(record.Outcome);

            csv.AppendLine(
                $"{record.RecordId}," +
                $"{record.SessionId}," +
                $"{record.StartedAtUtc:O}," +
                $"{record.CompletedAtUtc:O}," +
                $"{record.DurationMilliseconds}," +
                $"\"{command}\"," +
                $"{(record.Success ? "true" : "false")}," +
                $"\"{outcome}\"," +
                $"{record.ExitCode?.ToString() ?? ""}," +
                $"\"{logPath}\"," +
                $"{record.Assertions.Count}");
        }

        var data = csv.ToString();
        var bytes = System.Text.Encoding.UTF8.GetByteCount(data);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");

        var response = new ExportInstallationHistoryResponse(
            Format: "csv",
            Data: data,
            RecordCount: _records.Count,
            SizeBytes: bytes,
            SuggestedFilename: $"installation-history-{timestamp}.csv");

        return Task.FromResult(response);
    }

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
        {
            return value.Replace("\"", "\"\"");
        }

        return value;
    }

    public void Dispose()
    {
        _lock?.Dispose();
    }
}
