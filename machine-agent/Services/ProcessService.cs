using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace AdagioMachineAgent.Services;

/// <summary>
/// Runs executable processes on the VM, enforcing a command whitelist,
/// a concurrency limit, and an automatic timeout.
/// </summary>
public sealed class ProcessService : IDisposable
{
    private readonly AgentOptions _options;
    private readonly ILogger<ProcessService> _logger;

    /// <summary>Live processes tracked by PID.</summary>
    private readonly ConcurrentDictionary<int, TrackedProcess> _processes = new();

    public ProcessService(IOptions<AgentOptions> options, ILogger<ProcessService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Start an executable process and return its PID and start time.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   Thrown when the command is not in the allowed-paths whitelist,
    ///   or the concurrency limit has been reached.
    /// </exception>
    public TrackedProcess Start(string command, string? arguments, string? workingDirectory)
    {
        return Start(command, arguments, workingDirectory, SessionService.LegacyDefaultSessionId);
    }

    /// <summary>
    /// Start an executable process within a logical agent session.
    /// </summary>
    public TrackedProcess Start(string command, string? arguments, string? workingDirectory, string sessionId)
    {
        EnforceWhitelist(command);
        EnforceConcurrencyLimit();

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            startInfo.Arguments = arguments;
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var startedAt = DateTimeOffset.UtcNow;
        process.Start();

        var tracked = new TrackedProcess(process, startedAt, sessionId);
        _processes[process.Id] = tracked;

        // Schedule automatic kill after timeout (cast to long to avoid int overflow)
        var timeoutMs = (long)_options.ProcessTimeoutSeconds * 1000;
        _ = Task.Delay(TimeSpan.FromMilliseconds(timeoutMs)).ContinueWith(_ =>
        {
            if (_processes.TryGetValue(process.Id, out var tp) && !tp.Process.HasExited)
            {
                _logger.LogWarning(
                    "Process {Pid} exceeded timeout ({Timeout}s); killing.",
                    process.Id, _options.ProcessTimeoutSeconds);
                try { tp.Process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            }
        });

        _logger.LogInformation("Started process {Pid}: {Command}", process.Id, command);
        return tracked;
    }

    /// <summary>Retrieve a tracked process by PID.</summary>
    public TrackedProcess? Get(int pid) =>
        _processes.TryGetValue(pid, out var tp) ? tp : null;

    /// <summary>Retrieve a tracked process by PID within a specific session.</summary>
    public TrackedProcess? Get(int pid, string sessionId)
    {
        var tracked = Get(pid);
        return tracked is not null && string.Equals(tracked.SessionId, sessionId, StringComparison.Ordinal)
            ? tracked
            : null;
    }

    /// <summary>Count of currently tracked process entries.</summary>
    public int TrackedProcessCount => _processes.Count;

    /// <summary>Count of tracked processes that are still running.</summary>
    public int RunningProcessCount => _processes.Values.Count(tp => !tp.Process.HasExited);

    /// <summary>
    /// Best-effort termination of all still-running tracked processes.
    /// Returns the number of processes that were asked to terminate.
    /// </summary>
    public int TerminateAllRunningProcesses(string reason)
    {
        var terminated = 0;
        foreach (var tp in _processes.Values)
        {
            try
            {
                if (tp.Process.HasExited)
                {
                    continue;
                }

                tp.Process.Kill(entireProcessTree: true);
                terminated++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to terminate tracked process {Pid} during lifecycle cleanup. Reason: {Reason}",
                    tp.Process.Id,
                    reason);
            }
        }

        _logger.LogInformation(
            "Lifecycle cleanup requested. Reason: {Reason}. TerminatedProcesses={Terminated} RunningAfter={RunningAfter}",
            reason,
            terminated,
            RunningProcessCount);

        return terminated;
    }

    /// <summary>
    /// Remove exited processes from the tracked dictionary and return how many
    /// entries were removed.
    /// </summary>
    public int PruneExitedProcesses()
    {
        var removed = 0;
        foreach (var pair in _processes)
        {
            try
            {
                if (pair.Value.Process.HasExited && _processes.TryRemove(pair.Key, out _))
                {
                    removed++;
                }
            }
            catch
            {
                // Ignore stale process-access races and continue pruning.
            }
        }

        return removed;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void EnforceWhitelist(string command)
    {
        var allowed = PathPolicy.IsPathWithinAllowedDirectories(
            command,
            _options.AllowedExecutablePaths);

        if (!allowed)
        {
            throw new InvalidOperationException(
                $"Command '{command}' is not in an allowed executable path. " +
                $"Allowed paths: {string.Join(", ", _options.AllowedExecutablePaths)}");
        }
    }

    private void EnforceConcurrencyLimit()
    {
        var runningCount = _processes.Values.Count(tp => !tp.Process.HasExited);
        if (runningCount >= _options.MaxConcurrentProcesses)
        {
            throw new InvalidOperationException(
                $"Maximum concurrent process limit ({_options.MaxConcurrentProcesses}) reached.");
        }
    }

    public void Dispose()
    {
        TerminateAllRunningProcesses("ProcessService disposal");

        foreach (var tp in _processes.Values)
        {
            tp.Process.Dispose();
        }
    }
}

/// <summary>A process together with its start time and metadata.</summary>
public sealed record TrackedProcess(Process Process, DateTimeOffset StartedAt, string SessionId)
{
    public string Status => Process.HasExited ? "exited" : "running";
}
