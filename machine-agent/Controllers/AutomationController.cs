using AdagioMachineAgent.Models;
using AdagioMachineAgent.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AdagioMachineAgent.Controllers;

/// <summary>
/// REST API surface exposed to the VS Code controller extension.
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class AutomationController : ControllerBase
{
    private readonly ProcessService _processService;
    private readonly IUiAutomationService _uiService;
    private readonly ILogger<AutomationController> _logger;

    private static readonly string AgentVersion =
        typeof(AutomationController).Assembly.GetName().Version?.ToString() ?? "0.1.0";

    public AutomationController(
        ProcessService processService,
        IUiAutomationService uiService,
        ILogger<AutomationController> logger)
    {
        _processService = processService;
        _uiService = uiService;
        _logger = logger;
    }

    private BadRequestObjectResult ValidationError(
        string error,
        string remediationHint,
        string errorCode = AgentErrorCodes.ValidationFailed,
        string? detail = null)
    {
        return BadRequest(new ErrorResponse(
            error,
            detail,
            ErrorCode: errorCode,
            RemediationHint: remediationHint));
    }

    // ── GET /health ──────────────────────────────────────────────────────────

    [HttpGet("/health")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public IActionResult Health()
    {
        return Ok(new HealthResponse(
            Status: "healthy",
            Version: AgentVersion,
            ApiVersion: 1,
            MinSupportedClientVersion: "0.1.0"));
    }

    [HttpGet("/ready")]
    [ProducesResponseType(typeof(ReadinessResponse), StatusCodes.Status200OK)]
    public IActionResult Ready()
    {
        // Prune stale process entries on every readiness check so the tracked
        // process count stays accurate and the concurrency check stays fast.
        _processService.PruneExitedProcesses();

        var issues = new List<string>();
        var platform = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsLinux()
                ? "linux"
                : "unsupported";

        if (platform == "unsupported")
        {
            issues.Add("Platform is not supported. Only Windows and Linux are supported.");
        }

        var services = HttpContext?.RequestServices;
        var securityOptions = services?.GetService(typeof(IOptions<global::SecurityOptions>)) as IOptions<global::SecurityOptions>;
        var agentOptions = services?.GetService(typeof(IOptions<global::AgentOptions>)) as IOptions<global::AgentOptions>;

        if (securityOptions is null)
        {
            issues.Add("SecurityOptions are not available in DI.");
        }

        if (agentOptions is null)
        {
            issues.Add("AgentOptions are not available in DI.");
        }

        if (securityOptions is not null && agentOptions is not null)
        {
            issues.AddRange(SecurityPolicy.GetReadinessIssues(
                securityOptions.Value,
                agentOptions.Value,
                DateTimeOffset.UtcNow));
        }

        var uiAutomationIssues = GetUiAutomationReadinessIssues(platform);
        issues.AddRange(uiAutomationIssues);
        var uiAutomationAvailable = uiAutomationIssues.Count == 0;

        return Ok(new ReadinessResponse(
            Status: issues.Count == 0 ? "ready" : "degraded",
            Version: AgentVersion,
            ApiVersion: 1,
            Platform: platform,
            UiAutomationAvailable: uiAutomationAvailable,
            Issues: issues));
    }

    [HttpGet("/diagnostics/status")]
    [ProducesResponseType(typeof(DiagnosticsStatusResponse), StatusCodes.Status200OK)]
    public IActionResult DiagnosticsStatus()
    {
        var readyResult = Ready();
        var readyPayload = (readyResult as OkObjectResult)?.Value as ReadinessResponse;

        if (readyPayload is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to compute diagnostics status."));
        }

        return Ok(new DiagnosticsStatusResponse(
            Status: readyPayload.Status,
            Version: readyPayload.Version,
            ApiVersion: readyPayload.ApiVersion,
            Platform: readyPayload.Platform,
            UiAutomationAvailable: readyPayload.UiAutomationAvailable,
            Issues: readyPayload.Issues,
            RunningProcessCount: _processService.RunningProcessCount,
            TrackedProcessCount: _processService.TrackedProcessCount,
            TimestampUtc: DateTimeOffset.UtcNow));
    }

    [HttpGet("/diagnostics/export-metadata")]
    [ProducesResponseType(typeof(SupportBundleMetadataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public IActionResult DiagnosticsExportMetadata()
    {
        var diagnosticsResult = DiagnosticsStatus();
        var diagnosticsPayload = (diagnosticsResult as OkObjectResult)?.Value as DiagnosticsStatusResponse;

        if (diagnosticsPayload is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to compute diagnostics export metadata."));
        }

        var services = HttpContext?.RequestServices;
        var securityOptions = services?.GetService(typeof(IOptions<global::SecurityOptions>)) as IOptions<global::SecurityOptions>;
        var agentOptions = services?.GetService(typeof(IOptions<global::AgentOptions>)) as IOptions<global::AgentOptions>;

        if (securityOptions is null || agentOptions is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Required options were not available for diagnostics export metadata."));
        }

        var metadata = new SupportBundleMetadataResponse(
            Version: diagnosticsPayload.Version,
            ApiVersion: diagnosticsPayload.ApiVersion,
            Platform: diagnosticsPayload.Platform,
            ReadinessStatus: diagnosticsPayload.Status,
            IssueCount: diagnosticsPayload.Issues.Count,
            RunningProcessCount: diagnosticsPayload.RunningProcessCount,
            TrackedProcessCount: diagnosticsPayload.TrackedProcessCount,
            HttpsRequired: securityOptions.Value.RequireHttps,
            ApiKeyRequired: securityOptions.Value.RequireApiKey,
            ApiKeyHeaderName: securityOptions.Value.ApiKeyHeaderName,
            AllowedExecutablePathCount: agentOptions.Value.AllowedExecutablePaths.Count,
            AllowedReadablePathCount: agentOptions.Value.AllowedReadablePaths.Count,
            AllowedWritablePathCount: agentOptions.Value.AllowedWritablePaths.Count,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            RecommendedArtifacts:
            [
                "Service startup logs",
                "Readiness payload from /ready",
                "Diagnostics payload from /diagnostics/status",
                "Installer logs and event excerpts",
            ]);

        return Ok(metadata);
    }

    private List<string> GetUiAutomationReadinessIssues(string platform)
    {
        var issues = new List<string>();

        if (platform == "unsupported")
        {
            issues.Add("UI automation backend is not available on unsupported platform.");
            return issues;
        }

        if (platform == "linux")
        {
            var display = Environment.GetEnvironmentVariable("DISPLAY");
            var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            var dbus = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");

            if (string.IsNullOrWhiteSpace(display) && string.IsNullOrWhiteSpace(waylandDisplay))
            {
                issues.Add("Linux UI automation requires DISPLAY or WAYLAND_DISPLAY to be set.");
            }

            if (string.IsNullOrWhiteSpace(dbus))
            {
                issues.Add("Linux UI automation requires DBUS_SESSION_BUS_ADDRESS to be set.");
            }
        }

        if (_uiService is null)
        {
            issues.Add("UI automation service is not initialized.");
        }

        return issues;
    }

    // ── POST /run ────────────────────────────────────────────────────────────

    /// <summary>Start an executable process and return its PID.</summary>
    [HttpPost("/run")]
    [ProducesResponseType(typeof(RunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public IActionResult Run([FromBody] RunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return ValidationError("Command is required.", "Provide the executable path or command name in the request.");
        }

        try
        {
            var tracked = _processService.Start(
                request.Command,
                request.Arguments,
                request.WorkingDirectory);

            return Ok(new RunResponse(
                tracked.Process.Id,
                tracked.Status,
                tracked.StartedAt));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Run rejected: {Message}", ex.Message);
            return BadRequest(new ErrorResponse(
                ex.Message,
                ErrorCode: AgentErrorCodes.CommandRejected,
                RemediationHint: "Verify the command is within an allowed executable path and the concurrency limit has not been reached."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start process.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to start process.", ex.Message, ErrorCode: AgentErrorCodes.InternalError));
        }
    }

    // ── POST /run-installer-and-collect-artifacts ────────────────────────

    /// <summary>
    /// Start an installer process, wait for it, and collect diagnostic artifacts
    /// such as log tail and recent MSI event-log entries.
    /// </summary>
    [HttpPost("/run-installer-and-collect-artifacts")]
    [HttpPost("/run-and-collect-artifacts")]
    [ProducesResponseType(typeof(RunInstallerAndCollectArtifactsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public IActionResult RunInstallerAndCollectArtifacts([FromBody] RunInstallerAndCollectArtifactsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return ValidationError("Command is required.", "Provide the executable path or command name in the request.");
        }

        if (request.TimeoutMilliseconds <= 0)
        {
            return ValidationError("timeoutMilliseconds must be a positive integer.", "Set timeoutMilliseconds to a value greater than zero.");
        }

        if (request.TailLines <= 0)
        {
            return ValidationError("tailLines must be a positive integer.", "Set tailLines to a value greater than zero.");
        }

        if (request.EventEntryCount <= 0)
        {
            return ValidationError("eventEntryCount must be a positive integer.", "Set eventEntryCount to a value greater than zero.");
        }

        try
        {
            var tracked = _processService.Start(
                request.Command,
                request.Arguments,
                request.WorkingDirectory);

            var artifacts = CollectArtifactsForTrackedProcess(
                tracked,
                request.TimeoutMilliseconds,
                request.LogPath,
                request.TailLines,
                request.IncludeMsiEvents,
                request.EventEntryCount);

            return Ok(new RunInstallerAndCollectArtifactsResponse(
                tracked.Process.Id,
                tracked.StartedAt,
                artifacts));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "RunInstallerAndCollectArtifacts rejected: {Message}", ex.Message);
            return ValidationError(
                ex.Message,
                "Verify installer command path policy and active process concurrency limits.",
                AgentErrorCodes.CommandRejected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run installer and collect artifacts.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to run installer and collect artifacts.", ex.Message));
        }
    }

    // ── POST /run-installer-and-assert ─────────────────────────────────────

    /// <summary>
    /// Start an installer process, collect artifacts, and evaluate common
    /// assertions such as exit status, expected output path, and expected log text.
    /// </summary>
    [HttpPost("/run-installer-and-assert")]
    [HttpPost("/run-and-assert")]
    [ProducesResponseType(typeof(RunInstallerAndAssertResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public IActionResult RunInstallerAndAssert([FromBody] RunInstallerAndAssertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return ValidationError("Command is required.", "Provide the executable path or command name in the request.");
        }

        if (request.TimeoutMilliseconds <= 0)
        {
            return ValidationError("timeoutMilliseconds must be a positive integer.", "Set timeoutMilliseconds to a value greater than zero.");
        }

        if (request.TailLines <= 0)
        {
            return ValidationError("tailLines must be a positive integer.", "Set tailLines to a value greater than zero.");
        }

        if (request.EventEntryCount <= 0)
        {
            return ValidationError("eventEntryCount must be a positive integer.", "Set eventEntryCount to a value greater than zero.");
        }

        if (!string.IsNullOrWhiteSpace(request.LogMustContainText) && string.IsNullOrWhiteSpace(request.LogPath))
        {
            return ValidationError("logPath is required when logMustContainText is provided.", "Set logPath when using logMustContainText assertions.");
        }

        try
        {
            var tracked = _processService.Start(
                request.Command,
                request.Arguments,
                request.WorkingDirectory);

            var artifacts = CollectArtifactsForTrackedProcess(
                tracked,
                request.TimeoutMilliseconds,
                request.LogPath,
                request.TailLines,
                request.IncludeMsiEvents,
                request.EventEntryCount);

            var assertions = new List<AssertionResponse>
            {
                EvaluateProcessExitAssertion(artifacts, tracked.Process.Id, request.ExpectedExitCode),
            };

            if (!string.IsNullOrWhiteSpace(request.ExpectedPath))
            {
                assertions.Add(EvaluatePathExistsAssertion(request.ExpectedPath, request.ExpectedPathMustBeDirectory));
            }

            if (!string.IsNullOrWhiteSpace(request.LogMustContainText) && !string.IsNullOrWhiteSpace(request.LogPath))
            {
                assertions.Add(EvaluateLogContainsAssertion(
                    request.LogPath,
                    request.LogMustContainText,
                    request.LogContainsIgnoreCase));
            }

            return Ok(new RunInstallerAndAssertResponse(
                tracked.Process.Id,
                tracked.StartedAt,
                artifacts,
                assertions,
                assertions.All(a => a.Passed)));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "RunInstallerAndAssert rejected: {Message}", ex.Message);
            return ValidationError(
                ex.Message,
                "Verify installer command path policy and active process concurrency limits.",
                AgentErrorCodes.CommandRejected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run installer and assert workflow.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to run installer and assert workflow.", ex.Message));
        }
    }

    // ── GET /process-status ────────────────────────────────────────────────

    /// <summary>Get status for a tracked process.</summary>
    [HttpGet("/process-status")]
    [ProducesResponseType(typeof(ProcessStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult GetProcessStatus([FromQuery] int pid)
    {
        if (pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        var tracked = _processService.Get(pid);
        if (tracked is null)
        {
            return NotFound(new ErrorResponse(
                $"Process {pid} is not tracked.",
                ErrorCode: AgentErrorCodes.ProcessNotFound,
                RemediationHint: "Confirm the PID was returned by a previous /run response in this session."));
        }

        return Ok(ToProcessStatus(tracked));
    }

    // ── POST /wait-for-exit ────────────────────────────────────────────────

    /// <summary>Wait for a tracked process to exit or timeout.</summary>
    [HttpPost("/wait-for-exit")]
    [ProducesResponseType(typeof(WaitForExitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> WaitForExit([FromBody] WaitForExitRequest request)
    {
        if (request.Pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        if (request.TimeoutMilliseconds <= 0)
        {
            return ValidationError("timeoutMilliseconds must be a positive integer.", "Set timeoutMilliseconds to a value greater than zero.");
        }

        var tracked = _processService.Get(request.Pid);
        if (tracked is null)
        {
            return NotFound(new ErrorResponse(
                $"Process {request.Pid} is not tracked.",
                ErrorCode: AgentErrorCodes.ProcessNotFound,
                RemediationHint: "Confirm the PID was returned by a previous /run response in this session."));
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(request.TimeoutMilliseconds);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token, HttpContext.RequestAborted);

            await tracked.Process.WaitForExitAsync(linkedCts.Token);
            return Ok(new WaitForExitResponse(true, ToProcessStatus(tracked)));
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            // The caller-supplied timeout expired before the process exited.
            return Ok(new WaitForExitResponse(false, ToProcessStatus(tracked)));
        }
        catch (OperationCanceledException)
        {
            // The HTTP request was cancelled by the client.
            return StatusCode(499,
                new ErrorResponse("Request was cancelled.",
                    ErrorCode: AgentErrorCodes.RequestCancelled));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed while waiting for process {Pid} exit.", request.Pid);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed while waiting for process exit.", ex.Message,
                    ErrorCode: AgentErrorCodes.InternalError));
        }
    }

    // ── POST /collect-install-artifacts ──────────────────────────────────

    /// <summary>
    /// Wait for a tracked process, then collect install diagnostics such as log tail
    /// and recent Windows MSI event-log entries.
    /// </summary>
    [HttpPost("/collect-install-artifacts")]
    [HttpPost("/collect-process-artifacts")]
    [ProducesResponseType(typeof(CollectInstallArtifactsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult CollectInstallArtifacts([FromBody] CollectInstallArtifactsRequest request)
    {
        if (request.Pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        if (request.TimeoutMilliseconds <= 0)
        {
            return ValidationError("timeoutMilliseconds must be a positive integer.", "Set timeoutMilliseconds to a value greater than zero.");
        }

        if (request.TailLines <= 0)
        {
            return ValidationError("tailLines must be a positive integer.", "Set tailLines to a value greater than zero.");
        }

        if (request.EventEntryCount <= 0)
        {
            return ValidationError("eventEntryCount must be a positive integer.", "Set eventEntryCount to a value greater than zero.");
        }

        var tracked = _processService.Get(request.Pid);
        if (tracked is null)
        {
            return NotFound(new ErrorResponse(
                $"Process {request.Pid} is not tracked.",
                ErrorCode: AgentErrorCodes.ProcessNotFound,
                RemediationHint: "Confirm the PID was returned by a previous /run response in this session."));
        }

        try
        {
            return Ok(CollectArtifactsForTrackedProcess(
                tracked,
                request.TimeoutMilliseconds,
                request.LogPath,
                request.TailLines,
                request.IncludeMsiEvents,
                request.EventEntryCount));
        }
        catch (InvalidOperationException ex)
        {
            return ValidationError(
                ex.Message,
                "Review artifact collection inputs, including optional log path and line limits.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect install artifacts for pid {Pid}.", request.Pid);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to collect install artifacts.", ex.Message,
                    ErrorCode: AgentErrorCodes.InternalError));
        }
    }

    // ── POST /terminate ─────────────────────────────────────────────────────

    /// <summary>Terminate a tracked process.</summary>
    [HttpPost("/terminate")]
    [ProducesResponseType(typeof(StatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult Terminate([FromBody] TerminateProcessRequest request)
    {
        if (request.Pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        var tracked = _processService.Get(request.Pid);
        if (tracked is null)
        {
            return NotFound(new ErrorResponse(
                $"Process {request.Pid} is not tracked.",
                ErrorCode: AgentErrorCodes.ProcessNotFound,
                RemediationHint: "Confirm the PID was returned by a previous /run response in this session."));
        }

        try
        {
            if (!tracked.Process.HasExited)
            {
                tracked.Process.Kill(entireProcessTree: true);
                tracked.Process.WaitForExit(5000);
            }

            return Ok(new StatusResponse("ok", $"Process {request.Pid} terminated."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to terminate process {Pid}.", request.Pid);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to terminate process.", ex.Message,
                    ErrorCode: AgentErrorCodes.InternalError));
        }
    }

    // ── GET /ui-tree ─────────────────────────────────────────────────────────

    /// <summary>Return the UI element tree for a running process.</summary>
    [HttpGet("/ui-tree")]
    [ProducesResponseType(typeof(UiTreeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult GetUiTree([FromQuery] int pid)
    {
        if (pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        try
        {
            var tree = _uiService.GetUiTree(pid);
            return Ok(tree);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message,
                ErrorCode: AgentErrorCodes.ElementNotFound,
                RemediationHint: "Ensure the process is running and has a visible main window."));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message, ErrorCode: AgentErrorCodes.PlatformNotSupported));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetUiTree failed for pid {Pid}.", pid);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to retrieve UI tree.", ex.Message,
                    ErrorCode: AgentErrorCodes.InternalError));
        }
    }

    // ── POST /element-state ────────────────────────────────────────────────

    /// <summary>Return the current state of a UI element.</summary>
    [HttpPost("/element-state")]
    [ProducesResponseType(typeof(ElementStateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult GetElementState([FromBody] ElementStateRequest request)
    {
        if (request.Pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.ElementId))
        {
            return ValidationError("elementId is required.", "Provide a non-empty elementId from /ui-tree.");
        }

        try
        {
            return Ok(_uiService.GetElementState(request.Pid, request.ElementId));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message,
                ErrorCode: AgentErrorCodes.ElementNotFound,
                RemediationHint: "Use /ui-tree to enumerate available elements and verify the element ID."));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message, ErrorCode: AgentErrorCodes.PlatformNotSupported));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetElementState failed for element {ElementId}.", request.ElementId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to retrieve element state.", ex.Message,
                    ErrorCode: AgentErrorCodes.InternalError));
        }
    }

    // ── POST /wait-for-element ─────────────────────────────────────────────

    /// <summary>Wait until a UI element becomes available or timeout is reached.</summary>
    [HttpPost("/wait-for-element")]
    [ProducesResponseType(typeof(WaitForElementResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> WaitForElement([FromBody] WaitForElementRequest request)
    {
        if (request.Pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.ElementId))
        {
            return ValidationError("elementId is required.", "Provide a non-empty elementId from /ui-tree.");
        }

        if (request.TimeoutMilliseconds <= 0)
        {
            return ValidationError("timeoutMilliseconds must be a positive integer.", "Set timeoutMilliseconds to a value greater than zero.");
        }

        if (request.PollIntervalMilliseconds <= 0)
        {
            return ValidationError("pollIntervalMilliseconds must be a positive integer.", "Set pollIntervalMilliseconds to a value greater than zero.");
        }

        try
        {
            var result = await Task.Run(
                () => _uiService.WaitForElement(
                    request.Pid,
                    request.ElementId,
                    request.TimeoutMilliseconds,
                    request.PollIntervalMilliseconds,
                    HttpContext.RequestAborted),
                HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return StatusCode(499,
                new ErrorResponse("Request was cancelled.",
                    ErrorCode: AgentErrorCodes.RequestCancelled));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message,
                ErrorCode: AgentErrorCodes.ElementNotFound,
                RemediationHint: "Use /ui-tree to enumerate available elements and verify the element ID."));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message, ErrorCode: AgentErrorCodes.PlatformNotSupported));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WaitForElement failed for element {ElementId}.", request.ElementId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed while waiting for element.", ex.Message,
                    ErrorCode: AgentErrorCodes.InternalError));
        }
    }

    // ── POST /focus ────────────────────────────────────────────────────────

    /// <summary>Focus a UI element.</summary>
    [HttpPost("/focus")]
    [ProducesResponseType(typeof(StatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult SetFocus([FromBody] SetFocusRequest request)
    {
        if (request.Pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.ElementId))
        {
            return ValidationError("elementId is required.", "Provide a non-empty elementId from /ui-tree.");
        }

        try
        {
            _uiService.SetFocus(request.Pid, request.ElementId);
            return Ok(new StatusResponse("ok"));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message,
                ErrorCode: AgentErrorCodes.ElementNotFound,
                RemediationHint: "Use /ui-tree to enumerate available elements and verify the element ID."));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message, ErrorCode: AgentErrorCodes.PlatformNotSupported));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetFocus failed for element {ElementId}.", request.ElementId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to focus element.", ex.Message,
                    ErrorCode: AgentErrorCodes.InternalError));
        }
    }

    // ── POST /send-keys ────────────────────────────────────────────────────

    /// <summary>Send keystrokes to the application window.</summary>
    [HttpPost("/send-keys")]
    [ProducesResponseType(typeof(StatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public IActionResult SendKeys([FromBody] SendKeysRequest request)
    {
        if (request.Pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return ValidationError("text is required.", "Provide non-empty text content in the request.");
        }

        try
        {
            _uiService.SendKeys(request.Pid, request.Text);
            return Ok(new StatusResponse("ok"));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message,
                ErrorCode: AgentErrorCodes.ElementNotFound,
                RemediationHint: "Ensure the process has a focused main window before sending keys."));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message, ErrorCode: AgentErrorCodes.PlatformNotSupported));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendKeys failed for pid {Pid}.", request.Pid);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to send keys.", ex.Message,
                    ErrorCode: AgentErrorCodes.InternalError));
        }
    }

    // ── POST /press-hotkey ────────────────────────────────────────────────

    /// <summary>Press a key combination in the application window.</summary>
    [HttpPost("/press-hotkey")]
    [ProducesResponseType(typeof(StatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public IActionResult PressHotkey([FromBody] PressHotkeyRequest request)
    {
        if (request.Pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        if (request.Keys is null || request.Keys.Count == 0)
        {
            return ValidationError("keys must contain at least one key.", "Provide at least one key name in keys.");
        }

        try
        {
            _uiService.PressHotkey(request.Pid, request.Keys);
            return Ok(new StatusResponse("ok"));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message,
                ErrorCode: AgentErrorCodes.ElementNotFound,
                RemediationHint: "Ensure the process has a focused main window and all key names are supported."));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message, ErrorCode: AgentErrorCodes.PlatformNotSupported));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PressHotkey failed for pid {Pid}.", request.Pid);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to press hotkey.", ex.Message,
                    ErrorCode: AgentErrorCodes.InternalError));
        }
    }

    // ── POST /set-checkbox ────────────────────────────────────────────────

    /// <summary>Toggle a checkbox or radio button to the requested checked state.</summary>
    [HttpPost("/set-checkbox")]
    [ProducesResponseType(typeof(StatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult SetCheckbox([FromBody] SetCheckboxRequest request)
    {
        if (request.Pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.ElementId))
        {
            return ValidationError("elementId is required.", "Provide a non-empty elementId from /ui-tree.");
        }

        try
        {
            _uiService.SetCheckbox(request.Pid, request.ElementId, request.IsChecked);
            return Ok(new StatusResponse("ok"));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message,
                ErrorCode: AgentErrorCodes.ElementNotFound,
                RemediationHint: "Verify the element ID and that the element supports the Toggle pattern."));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message, ErrorCode: AgentErrorCodes.PlatformNotSupported));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetCheckbox failed for element {ElementId}.", request.ElementId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to set checkbox state.", ex.Message,
                    ErrorCode: AgentErrorCodes.InternalError));
        }
    }

    // ── POST /select-option ───────────────────────────────────────────────

    /// <summary>Select an option in a combo box or list by text label or zero-based index.</summary>
    [HttpPost("/select-option")]
    [ProducesResponseType(typeof(StatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult SelectOption([FromBody] SelectOptionRequest request)
    {
        if (request.Pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.ElementId))
        {
            return ValidationError("elementId is required.", "Provide a non-empty elementId from /ui-tree.");
        }

        if (request.OptionText is null && request.OptionIndex is null)
        {
            return ValidationError("Either optionText or optionIndex must be provided.", "Set optionText or optionIndex so the option can be resolved.");
        }

        try
        {
            _uiService.SelectOption(request.Pid, request.ElementId, request.OptionText, request.OptionIndex);
            return Ok(new StatusResponse("ok"));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message,
                ErrorCode: AgentErrorCodes.ElementNotFound,
                RemediationHint: "Verify the element ID, option text, and that the option index is within range."));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message, ErrorCode: AgentErrorCodes.PlatformNotSupported));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SelectOption failed for element {ElementId}.", request.ElementId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to select option.", ex.Message,
                    ErrorCode: AgentErrorCodes.InternalError));
        }
    }

    // ── GET /screenshot ──────────────────────────────────────────────────────

    /// <summary>Capture a screenshot of the process window.</summary>
    [HttpGet("/screenshot")]
    [ProducesResponseType(typeof(ScreenshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult GetScreenshot([FromQuery] int pid)
    {
        if (pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        try
        {
            var base64 = _uiService.CaptureScreenshot(pid);
            return Ok(new ScreenshotResponse(base64));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message,
                ErrorCode: AgentErrorCodes.ElementNotFound,
                RemediationHint: "Ensure the process has a visible main window."));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message, ErrorCode: AgentErrorCodes.PlatformNotSupported));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Screenshot failed for pid {Pid}.", pid);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to capture screenshot.", ex.Message,
                    ErrorCode: AgentErrorCodes.InternalError));
        }
    }

    // ── POST /click ──────────────────────────────────────────────────────────

    /// <summary>Click a UI element by its element ID.</summary>
    [HttpPost("/click")]
    [ProducesResponseType(typeof(StatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult Click([FromBody] ClickRequest request)
    {
        if (request.Pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.ElementId))
        {
            return ValidationError("elementId is required.", "Provide a non-empty elementId from /ui-tree.");
        }

        try
        {
            _uiService.Click(request.Pid, request.ElementId);
            return Ok(new StatusResponse("ok"));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message,
                ErrorCode: AgentErrorCodes.ElementNotFound,
                RemediationHint: "Use /ui-tree to enumerate available elements and verify the element ID."));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message, ErrorCode: AgentErrorCodes.PlatformNotSupported));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Click failed for element {ElementId}.", request.ElementId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to click element.", ex.Message,
                    ErrorCode: AgentErrorCodes.InternalError));
        }
    }

    // ── POST /type ───────────────────────────────────────────────────────────

    /// <summary>Type text into a UI element.</summary>
    [HttpPost("/type")]
    [ProducesResponseType(typeof(StatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult TypeText([FromBody] TypeRequest request)
    {
        if (request.Pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.ElementId))
        {
            return ValidationError("elementId is required.", "Provide a non-empty elementId from /ui-tree.");
        }

        if (request.Text is null)
        {
            return ValidationError("text is required.", "Provide non-empty text content in the request.");
        }

        try
        {
            _uiService.Type(request.Pid, request.ElementId, request.Text);
            return Ok(new StatusResponse("ok"));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message,
                ErrorCode: AgentErrorCodes.ElementNotFound,
                RemediationHint: "Use /ui-tree to enumerate available elements and verify the element ID."));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message, ErrorCode: AgentErrorCodes.PlatformNotSupported));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Type failed for element {ElementId}.", request.ElementId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to type text.", ex.Message,
                    ErrorCode: AgentErrorCodes.InternalError));
        }
    }

    // ── POST /copy-file ──────────────────────────────────────────────────────

    /// <summary>Copy a file to the target system.</summary>
    [HttpPost("/copy-file")]
    [ProducesResponseType(typeof(CopyFileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public IActionResult CopyFile([FromBody] CopyFileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            return ValidationError("DestinationPath is required.", "Provide a destinationPath in an allowed writable directory.");
        }

        if (string.IsNullOrWhiteSpace(request.FileContentBase64))
        {
            return ValidationError("FileContentBase64 is required.", "Provide base64-encoded file content.");
        }

        try
        {
            var destinationPath = Path.GetFullPath(request.DestinationPath);

            // Validate destination path against writable-path policy.
            var options = HttpContext.RequestServices.GetRequiredService<IOptions<AgentOptions>>();
            var allowed = PathPolicy.IsPathWithinAllowedDirectories(
                destinationPath,
                options.Value.AllowedWritablePaths);

            if (!allowed)
            {
                return ValidationError(
                    $"Destination path '{request.DestinationPath}' is not in an allowed directory. " +
                    $"Allowed paths: {string.Join(", ", options.Value.AllowedWritablePaths)}",
                    "Choose a destination under an allowed writable path.",
                    AgentErrorCodes.PathNotAllowed);
            }

            // Check if file exists and overwrite flag
            if (System.IO.File.Exists(destinationPath) && !request.OverwriteIfExists)
            {
                return ValidationError(
                    $"File already exists at '{request.DestinationPath}' and overwrite is not enabled.",
                    "Set overwriteIfExists to true or choose a different destination path.");
            }

            // Decode and write file
            byte[] fileBytes = Convert.FromBase64String(request.FileContentBase64);
            
            // Ensure directory exists
            var directory = System.IO.Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            System.IO.File.WriteAllBytes(destinationPath, fileBytes);

            _logger.LogInformation("File copied to {Path} ({Bytes} bytes)", destinationPath, fileBytes.Length);
            return Ok(new CopyFileResponse(destinationPath, fileBytes.Length));
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Invalid base64 format for file content.");
            return ValidationError("FileContentBase64 must be valid base64.", "Ensure fileContentBase64 is valid Base64 text.", detail: ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy file.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to copy file.", ex.Message));
        }
    }

    // ── POST /read-text-file ───────────────────────────────────────────────

    /// <summary>Read a UTF-8 text file from the target machine.</summary>
    [HttpPost("/read-text-file")]
    [ProducesResponseType(typeof(ReadTextFileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public IActionResult ReadTextFile([FromBody] ReadTextFileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return ValidationError("Path is required.", "Provide a non-empty path within an allowed directory.");
        }

        try
        {
            var fullPath = Path.GetFullPath(request.Path);
            var options = HttpContext.RequestServices.GetRequiredService<IOptions<AgentOptions>>();
            var allowed = PathPolicy.IsPathWithinAllowedDirectories(
                fullPath,
                options.Value.AllowedReadablePaths);

            if (!allowed)
            {
                return ValidationError(
                    $"Path '{request.Path}' is not in an allowed directory. " +
                    $"Allowed paths: {string.Join(", ", options.Value.AllowedReadablePaths)}",
                    "Choose a path under an allowed readable directory.",
                    AgentErrorCodes.PathNotAllowed);
            }

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new ErrorResponse($"File '{request.Path}' does not exist."));
            }

            var content = System.IO.File.ReadAllText(fullPath);
            return Ok(new ReadTextFileResponse(fullPath, content));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read file '{Path}'.", request.Path);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to read text file.", ex.Message));
        }
    }

    // ── POST /tail-file ─────────────────────────────────────────────────────

    /// <summary>Read the last N lines from a UTF-8 text file.</summary>
    [HttpPost("/tail-file")]
    [ProducesResponseType(typeof(TailFileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public IActionResult TailFile([FromBody] TailFileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return ValidationError("Path is required.", "Provide a non-empty path within an allowed directory.");
        }

        if (request.Lines <= 0)
        {
            return ValidationError("Lines must be a positive integer.", "Set lines to a value greater than zero.");
        }

        try
        {
            var fullPath = Path.GetFullPath(request.Path);
            var options = HttpContext.RequestServices.GetRequiredService<IOptions<AgentOptions>>();
            var allowed = PathPolicy.IsPathWithinAllowedDirectories(
                fullPath,
                options.Value.AllowedReadablePaths);

            if (!allowed)
            {
                return ValidationError(
                    $"Path '{request.Path}' is not in an allowed directory. " +
                    $"Allowed paths: {string.Join(", ", options.Value.AllowedReadablePaths)}",
                    "Choose a path under an allowed readable directory.",
                    AgentErrorCodes.PathNotAllowed);
            }

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new ErrorResponse($"File '{request.Path}' does not exist."));
            }

            return Ok(ReadTailFile(fullPath, request.Lines));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to tail file '{Path}'.", request.Path);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to tail file.", ex.Message));
        }
    }

    // ── POST /list-directory ──────────────────────────────────────────────

    /// <summary>List files and directories under a target path.</summary>
    [HttpPost("/list-directory")]
    [ProducesResponseType(typeof(ListDirectoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult ListDirectory([FromBody] ListDirectoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return ValidationError("Path is required.", "Provide a non-empty path within an allowed directory.");
        }

        try
        {
            var fullPath = Path.GetFullPath(request.Path);
            var options = HttpContext.RequestServices.GetRequiredService<IOptions<AgentOptions>>();
            var allowed = PathPolicy.IsPathWithinAllowedDirectories(
                fullPath,
                options.Value.AllowedReadablePaths);

            if (!allowed)
            {
                return ValidationError(
                    $"Path '{request.Path}' is not in an allowed directory. " +
                    $"Allowed paths: {string.Join(", ", options.Value.AllowedReadablePaths)}",
                    "Choose a path under an allowed readable directory.",
                    AgentErrorCodes.PathNotAllowed);
            }

            if (!Directory.Exists(fullPath))
            {
                return NotFound(new ErrorResponse($"Directory '{request.Path}' does not exist."));
            }

            var entries = Directory
                .EnumerateFileSystemEntries(fullPath)
                .OrderBy(Path.GetFileName)
                .Select(path => new DirectoryEntry(
                    Name: Path.GetFileName(path),
                    Path: path,
                    IsDirectory: Directory.Exists(path)))
                .ToList();

            return Ok(new ListDirectoryResponse(fullPath, entries));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list directory '{Path}'.", request.Path);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to list directory.", ex.Message));
        }
    }

    // ── POST /file-exists ─────────────────────────────────────────────────

    /// <summary>Check whether a file or directory exists at the given path.</summary>
    [HttpPost("/file-exists")]
    [ProducesResponseType(typeof(FileExistsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public IActionResult FileExists([FromBody] FileExistsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return ValidationError("Path is required.", "Provide a non-empty path within an allowed directory.");
        }

        try
        {
            var fullPath = Path.GetFullPath(request.Path);
            var options = HttpContext.RequestServices.GetRequiredService<IOptions<AgentOptions>>();
            var allowed = PathPolicy.IsPathWithinAllowedDirectories(
                fullPath,
                options.Value.AllowedReadablePaths);

            if (!allowed)
            {
                return ValidationError(
                    $"Path '{request.Path}' is not in an allowed directory. " +
                    $"Allowed paths: {string.Join(", ", options.Value.AllowedReadablePaths)}",
                    "Choose a path under an allowed readable directory.",
                    AgentErrorCodes.PathNotAllowed);
            }

            var isDirectory = Directory.Exists(fullPath);
            var exists = isDirectory || System.IO.File.Exists(fullPath);
            return Ok(new FileExistsResponse(fullPath, exists, isDirectory));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check path existence '{Path}'.", request.Path);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to check path existence.", ex.Message));
        }
    }

    // ── POST /assert-process-exited ───────────────────────────────────────

    /// <summary>Assert that a tracked process exits (and optionally with a specific exit code).</summary>
    [HttpPost("/assert-process-exited")]
    [ProducesResponseType(typeof(AssertionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult AssertProcessExited([FromBody] AssertProcessExitedRequest request)
    {
        if (request.Pid <= 0)
        {
            return ValidationError("pid must be a positive integer.", "Set pid to a running process ID greater than zero.");
        }

        if (request.TimeoutMilliseconds <= 0)
        {
            return ValidationError("timeoutMilliseconds must be a positive integer.", "Set timeoutMilliseconds to a value greater than zero.");
        }

        var tracked = _processService.Get(request.Pid);
        if (tracked is null)
        {
            return NotFound(new ErrorResponse(
                $"Process {request.Pid} is not tracked.",
                ErrorCode: AgentErrorCodes.ProcessNotFound,
                RemediationHint: "Confirm the PID was returned by a previous /run response in this session."));
        }

        try
        {
            var exited = tracked.Process.WaitForExit(request.TimeoutMilliseconds);
            if (!exited)
            {
                return ValidationError(
                    $"Process {request.Pid} did not exit within {request.TimeoutMilliseconds}ms.",
                    "Increase timeoutMilliseconds or verify the process can exit within the configured timeout.");
            }

            if (request.ExpectedExitCode.HasValue &&
                tracked.Process.ExitCode != request.ExpectedExitCode.Value)
            {
                return ValidationError(
                    $"Process {request.Pid} exited with code {tracked.Process.ExitCode}, " +
                    $"expected {request.ExpectedExitCode.Value}.",
                    "Update expectedExitCode to match the process outcome or inspect process logs for failures.");
            }

            return Ok(new AssertionResponse(
                true,
                request.ExpectedExitCode.HasValue
                    ? $"Process {request.Pid} exited with expected code {request.ExpectedExitCode.Value}."
                    : $"Process {request.Pid} exited."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AssertProcessExited failed for pid {Pid}.", request.Pid);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to assert process exit.", ex.Message));
        }
    }

    // ── POST /assert-path-exists ─────────────────────────────────────────

    /// <summary>Assert that a path exists (and optionally is a directory).</summary>
    [HttpPost("/assert-path-exists")]
    [ProducesResponseType(typeof(AssertionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public IActionResult AssertPathExists([FromBody] AssertPathExistsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return ValidationError("Path is required.", "Provide a non-empty path within an allowed directory.");
        }

        try
        {
            var fullPath = ValidateReadablePath(request.Path);
            var isDirectory = Directory.Exists(fullPath);
            var exists = isDirectory || System.IO.File.Exists(fullPath);

            if (!exists)
            {
                return ValidationError(
                    $"Path '{request.Path}' does not exist.",
                    "Verify the path and ensure the file or directory has been created before asserting.");
            }

            if (request.MustBeDirectory && !isDirectory)
            {
                return ValidationError(
                    $"Path '{request.Path}' exists but is not a directory.",
                    "Set mustBeDirectory to false or provide a directory path.");
            }

            return Ok(new AssertionResponse(
                true,
                request.MustBeDirectory
                    ? $"Directory '{fullPath}' exists."
                    : $"Path '{fullPath}' exists."));
        }
        catch (InvalidOperationException ex)
        {
            return ValidationError(
                ex.Message,
                "Ensure the requested path is inside the allowed readable directories.",
                AgentErrorCodes.PathNotAllowed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AssertPathExists failed for path '{Path}'.", request.Path);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to assert path existence.", ex.Message));
        }
    }

    // ── POST /assert-log-contains ────────────────────────────────────────

    /// <summary>Assert that a text file contains the expected text fragment.</summary>
    [HttpPost("/assert-log-contains")]
    [ProducesResponseType(typeof(AssertionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult AssertLogContains([FromBody] AssertLogContainsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return ValidationError("Path is required.", "Provide a non-empty path within an allowed directory.");
        }

        if (string.IsNullOrWhiteSpace(request.ContainsText))
        {
            return ValidationError("containsText is required.", "Provide the text fragment to assert in the file.");
        }

        try
        {
            var fullPath = ValidateReadablePath(request.Path);

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new ErrorResponse($"File '{request.Path}' does not exist."));
            }

            var content = System.IO.File.ReadAllText(fullPath);
            var comparison = request.IgnoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            var matched = content.Contains(request.ContainsText, comparison);
            if (!matched)
            {
                return ValidationError(
                    $"File '{request.Path}' does not contain expected text '{request.ContainsText}'.",
                    "Check the file content and expected text, or enable ignoreCase when appropriate.");
            }

            return Ok(new AssertionResponse(
                true,
                $"File '{fullPath}' contains expected text '{request.ContainsText}'."));
        }
        catch (InvalidOperationException ex)
        {
            return ValidationError(
                ex.Message,
                "Ensure the requested path is inside the allowed readable directories.",
                AgentErrorCodes.PathNotAllowed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AssertLogContains failed for path '{Path}'.", request.Path);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to assert log content.", ex.Message));
        }
    }

    private static ProcessStatusResponse ToProcessStatus(TrackedProcess tracked)
    {
        DateTimeOffset? exitedAt = null;
        int? exitCode = null;

        if (tracked.Process.HasExited)
        {
            exitedAt = new DateTimeOffset(tracked.Process.ExitTime.ToUniversalTime());
            exitCode = tracked.Process.ExitCode;
        }

        return new ProcessStatusResponse(
            tracked.Process.Id,
            tracked.Status,
            tracked.StartedAt,
            exitedAt,
            exitCode);
    }

    private static AssertionResponse EvaluateProcessExitAssertion(
        CollectInstallArtifactsResponse artifacts,
        int pid,
        int? expectedExitCode)
    {
        if (!artifacts.Exited)
        {
            return new AssertionResponse(false, $"Process {pid} did not exit before timeout.");
        }

        if (expectedExitCode.HasValue && artifacts.Process.ExitCode != expectedExitCode.Value)
        {
            return new AssertionResponse(
                false,
                $"Process {pid} exited with code {artifacts.Process.ExitCode}, expected {expectedExitCode.Value}.");
        }

        return new AssertionResponse(
            true,
            expectedExitCode.HasValue
                ? $"Process {pid} exited with expected code {expectedExitCode.Value}."
                : $"Process {pid} exited.");
    }

    private AssertionResponse EvaluatePathExistsAssertion(string path, bool mustBeDirectory)
    {
        try
        {
            var fullPath = ValidateReadablePath(path);
            var isDirectory = Directory.Exists(fullPath);
            var exists = isDirectory || System.IO.File.Exists(fullPath);

            if (!exists)
            {
                return new AssertionResponse(false, $"Path '{path}' does not exist.");
            }

            if (mustBeDirectory && !isDirectory)
            {
                return new AssertionResponse(false, $"Path '{path}' exists but is not a directory.");
            }

            return new AssertionResponse(
                true,
                mustBeDirectory
                    ? $"Directory '{fullPath}' exists."
                    : $"Path '{fullPath}' exists.");
        }
        catch (Exception ex)
        {
            return new AssertionResponse(false, ex.Message);
        }
    }

    private AssertionResponse EvaluateLogContainsAssertion(string path, string containsText, bool ignoreCase)
    {
        try
        {
            var fullPath = ValidateReadablePath(path);
            if (!System.IO.File.Exists(fullPath))
            {
                return new AssertionResponse(false, $"File '{path}' does not exist.");
            }

            var content = System.IO.File.ReadAllText(fullPath);
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            if (!content.Contains(containsText, comparison))
            {
                return new AssertionResponse(
                    false,
                    $"File '{path}' does not contain expected text '{containsText}'.");
            }

            return new AssertionResponse(true, $"File '{fullPath}' contains expected text '{containsText}'.");
        }
        catch (Exception ex)
        {
            return new AssertionResponse(false, ex.Message);
        }
    }

    private CollectInstallArtifactsResponse CollectArtifactsForTrackedProcess(
        TrackedProcess tracked,
        int timeoutMilliseconds,
        string? logPath,
        int tailLines,
        bool includeMsiEvents,
        int eventEntryCount)
    {
        var exited = tracked.Process.WaitForExit(timeoutMilliseconds);
        var process = ToProcessStatus(tracked);
        var warnings = new List<string>();
        TailFileResponse? logTail = null;
        var msiEvents = new List<InstallEventLogEntry>();

        if (!string.IsNullOrWhiteSpace(logPath))
        {
            var validatedPath = ValidateReadablePath(logPath);

            if (System.IO.File.Exists(validatedPath))
            {
                logTail = ReadTailFile(validatedPath, tailLines);
            }
            else
            {
                warnings.Add($"Log file '{logPath}' does not exist.");
            }
        }

        if (includeMsiEvents)
        {
            var (entries, warning) = ReadInstallerEvents(tracked.StartedAt, eventEntryCount);
            msiEvents = entries;
            if (!string.IsNullOrWhiteSpace(warning))
            {
                warnings.Add(warning);
            }
        }

        return new CollectInstallArtifactsResponse(exited, process, logTail, msiEvents, warnings);
    }

    private string ValidateReadablePath(string requestedPath)
    {
        var fullPath = Path.GetFullPath(requestedPath);
        var options = HttpContext.RequestServices.GetRequiredService<IOptions<AgentOptions>>();
        var allowed = PathPolicy.IsPathWithinAllowedDirectories(
            fullPath,
            options.Value.AllowedReadablePaths);

        if (!allowed)
        {
            throw new InvalidOperationException(
                $"Path '{requestedPath}' is not in an allowed directory. " +
                $"Allowed paths: {string.Join(", ", options.Value.AllowedReadablePaths)}");
        }

        return fullPath;
    }

    private static TailFileResponse ReadTailFile(string fullPath, int lines)
    {
        var allLines = System.IO.File.ReadAllLines(fullPath);
        var start = Math.Max(0, allLines.Length - lines);
        var content = string.Join(Environment.NewLine, allLines.Skip(start));
        return new TailFileResponse(fullPath, lines, content);
    }

    private static (List<InstallEventLogEntry> Entries, string? Warning) ReadInstallerEvents(
        DateTimeOffset since,
        int maxEntries)
    {
#if WINDOWS
        try
        {
            var query = new System.Diagnostics.Eventing.Reader.EventLogQuery(
                "Application",
                System.Diagnostics.Eventing.Reader.PathType.LogName,
                $"*[System[Provider[@Name='MsiInstaller'] and TimeCreated[@SystemTime >= '{since.UtcDateTime:O}']]]")
            {
                ReverseDirection = true,
            };

            using var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(query);
            var events = new List<InstallEventLogEntry>();

            for (var record = reader.ReadEvent(); record is not null && events.Count < maxEntries; record = reader.ReadEvent())
            {
                using (record)
                {
                    events.Add(new InstallEventLogEntry(
                        TimeCreated: record.TimeCreated is DateTime time
                            ? new DateTimeOffset(time.ToUniversalTime())
                            : since,
                        EventId: record.Id,
                        Level: record.LevelDisplayName ?? "Information",
                        Source: record.ProviderName ?? "MsiInstaller",
                        Message: SafeFormatDescription(record)));
                }
            }

            events.Reverse();
            return (events, null);
        }
        catch (Exception ex)
        {
            return ([], $"MSI event log collection failed: {ex.Message}");
        }
#else
        return ([], "MSI event log collection is only available on Windows.");
#endif
    }

#if WINDOWS
    private static string SafeFormatDescription(System.Diagnostics.Eventing.Reader.EventRecord record)
    {
        try
        {
            return record.FormatDescription() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
#endif
}

