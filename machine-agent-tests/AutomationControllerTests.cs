using AdagioMachineAgent.Controllers;
using AdagioMachineAgent.Models;
using AdagioMachineAgent.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AdagioMachineAgent.Tests;

public sealed class AutomationControllerTests
{
    [Fact]
    public void ConnectSession_ReturnsSessionMetadata()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var sessionService = new SessionService();
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object, sessionService: sessionService);

        var result = sut.ConnectSession(new ConnectSessionRequest("test-client"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<ConnectSessionResponse>(ok.Value);
        Assert.False(string.IsNullOrWhiteSpace(payload.SessionId));
        Assert.Equal(SessionService.SessionHeaderName, payload.SessionHeaderName);
    }

    [Fact]
    public void Health_ReturnsOkWithHealthyStatus()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var result = sut.Health();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<HealthResponse>(ok.Value);
        Assert.Equal("healthy", payload.Status);
        Assert.False(string.IsNullOrWhiteSpace(payload.Version));
        Assert.Equal(1, payload.ApiVersion);
    }

    [Fact]
    public void Ready_ReturnsOkWithReadinessStatus()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var result = sut.Ready();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<ReadinessResponse>(ok.Value);
        Assert.True(
            string.Equals(payload.Status, "ready", StringComparison.Ordinal) ||
            string.Equals(payload.Status, "degraded", StringComparison.Ordinal),
            $"Unexpected readiness status '{payload.Status}'.");
        Assert.False(string.IsNullOrWhiteSpace(payload.Version));
        Assert.Equal(1, payload.ApiVersion);
        if (string.Equals(payload.Status, "ready", StringComparison.Ordinal))
        {
            Assert.True(payload.UiAutomationAvailable);
            Assert.Empty(payload.Issues);
        }
        else
        {
            Assert.NotEmpty(payload.Issues);
        }
    }

    [Fact]
    public void Ready_ReturnsDegradedWhenSecurityConfigIsInvalid()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();

        var securityOptions = Options.Create(new global::SecurityOptions
        {
            RequireHttps = true,
            HttpsCertificatePath = string.Empty,
            RequireApiKey = true,
            ApiKey = "CHANGE_ME",
        });

        var sut = CreateController(processService, uiService.Object, securityOptions: securityOptions);

        var result = sut.Ready();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<ReadinessResponse>(ok.Value);
        Assert.Equal("degraded", payload.Status);
        Assert.Contains(payload.Issues, issue => issue.Contains("certificate path", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(payload.Issues, issue => issue.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiagnosticsStatus_ReturnsCountsAndTimestamp()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var before = DateTimeOffset.UtcNow;
        var result = sut.DiagnosticsStatus();
        var after = DateTimeOffset.UtcNow;

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<DiagnosticsStatusResponse>(ok.Value);
        Assert.True(
            string.Equals(payload.Status, "ready", StringComparison.Ordinal) ||
            string.Equals(payload.Status, "degraded", StringComparison.Ordinal),
            $"Unexpected diagnostics status '{payload.Status}'.");
        Assert.Equal(0, payload.RunningProcessCount);
        Assert.Equal(0, payload.TrackedProcessCount);
        Assert.True(payload.ActiveSessionCount >= 1); // at minimum the legacy default session
        Assert.Null(payload.OldestSessionAgeSeconds);  // no non-legacy sessions were created
        if (string.Equals(payload.Status, "ready", StringComparison.Ordinal))
        {
            Assert.Empty(payload.Issues);
        }
        else
        {
            Assert.NotEmpty(payload.Issues);
        }
        Assert.InRange(payload.TimestampUtc, before, after.AddSeconds(1));
    }

    [Fact]
    public void DiagnosticsExportMetadata_ReturnsNonSensitiveSummary()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var before = DateTimeOffset.UtcNow;
        var result = sut.DiagnosticsExportMetadata();
        var after = DateTimeOffset.UtcNow;

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<SupportBundleMetadataResponse>(ok.Value);
        Assert.Equal(1, payload.ApiVersion);
        Assert.True(
            string.Equals(payload.ReadinessStatus, "ready", StringComparison.Ordinal) ||
            string.Equals(payload.ReadinessStatus, "degraded", StringComparison.Ordinal),
            $"Unexpected export readiness status '{payload.ReadinessStatus}'.");
        if (string.Equals(payload.ReadinessStatus, "ready", StringComparison.Ordinal))
        {
            Assert.Equal(0, payload.IssueCount);
        }
        else
        {
            Assert.True(payload.IssueCount > 0);
        }
        Assert.Equal("X-API-Key", payload.ApiKeyHeaderName);
        Assert.True(payload.AllowedExecutablePathCount > 0);
        Assert.NotEmpty(payload.RecommendedArtifacts);
        Assert.InRange(payload.GeneratedAtUtc, before, after.AddSeconds(1));
    }

    [Fact]
    public void Run_ReturnsBadRequestWhenCommandMissing()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var result = sut.Run(new RunRequest("", null, null));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<ErrorResponse>(bad.Value);
        Assert.Equal("Command is required.", payload.Error);
        Assert.Equal(AgentErrorCodes.ValidationFailed, payload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(payload.RemediationHint));
    }

    [Fact]
    public void Run_ReturnsBadRequestWhenCommandNotWhitelisted()
    {
        var commandInfo = ResolveLongRunningCommand();
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var result = sut.Run(new RunRequest(commandInfo.Command, commandInfo.Arguments, null));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<ErrorResponse>(bad.Value);
        Assert.Contains("not in an allowed executable path", payload.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AgentErrorCodes.CommandRejected, payload.ErrorCode);
    }

    [Fact]
    public void Run_Returns500WhenStartThrowsUnexpected()
    {
        var allowedRoot = Path.GetTempPath();
        var missingExecutable = Path.Combine(allowedRoot, "this-file-does-not-exist.exe");

        using var processService = CreateProcessService(allowedExecutablePaths: [allowedRoot]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var result = sut.Run(new RunRequest(missingExecutable, null, null));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
        var payload = Assert.IsType<ErrorResponse>(objectResult.Value);
        Assert.Equal("Failed to start process.", payload.Error);
        Assert.False(string.IsNullOrWhiteSpace(payload.Detail));
        Assert.Equal(AgentErrorCodes.InternalError, payload.ErrorCode);
    }

    [Fact]
    public void RunInstallerAndCollectArtifacts_ValidatesInputs()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        Assert.IsType<BadRequestObjectResult>(sut.RunInstallerAndCollectArtifacts(new RunInstallerAndCollectArtifactsRequest("")));
        Assert.IsType<BadRequestObjectResult>(sut.RunInstallerAndCollectArtifacts(new RunInstallerAndCollectArtifactsRequest("C:/Apps/setup.exe", TimeoutMilliseconds: 0)));
        Assert.IsType<BadRequestObjectResult>(sut.RunInstallerAndCollectArtifacts(new RunInstallerAndCollectArtifactsRequest("C:/Apps/setup.exe", TailLines: 0)));
        Assert.IsType<BadRequestObjectResult>(sut.RunInstallerAndCollectArtifacts(new RunInstallerAndCollectArtifactsRequest("C:/Apps/setup.exe", EventEntryCount: 0)));
    }

    [Fact]
    public void RunInstallerAndCollectArtifacts_ReturnsArtifactsForStartedProcess()
    {
        var commandInfo = ResolveQuickExitCommand();
        var logPath = Path.Combine(Path.GetTempPath(), $"run-artifact-log-{Guid.NewGuid():N}.log");
        File.WriteAllLines(logPath, ["alpha", "beta", "gamma"]);

        using var processService = CreateProcessService(
            allowedExecutablePaths: [Path.GetDirectoryName(commandInfo.Command)!]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        try
        {
            var result = sut.RunInstallerAndCollectArtifacts(new RunInstallerAndCollectArtifactsRequest(
                Command: commandInfo.Command,
                Arguments: commandInfo.Arguments,
                TimeoutMilliseconds: 5000,
                LogPath: logPath,
                TailLines: 2,
                IncludeMsiEvents: false));

            var ok = Assert.IsType<OkObjectResult>(result);
            var payload = Assert.IsType<RunInstallerAndCollectArtifactsResponse>(ok.Value);
            Assert.True(payload.Pid > 0);
            Assert.True(payload.Artifacts.Exited);
            Assert.Equal(payload.Pid, payload.Artifacts.Process.Pid);
            Assert.NotNull(payload.Artifacts.LogTail);
            Assert.DoesNotContain("alpha", payload.Artifacts.LogTail!.Content);
            Assert.Contains("beta", payload.Artifacts.LogTail.Content);
            Assert.Contains("gamma", payload.Artifacts.LogTail.Content);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [Fact]
    public void GetProcessStatus_ReturnsNotFoundWhenPidNotTracked()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var result = sut.GetProcessStatus(999999);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var payload = Assert.IsType<ErrorResponse>(notFound.Value);
        Assert.Contains("not tracked", payload.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AgentErrorCodes.ProcessNotFound, payload.ErrorCode);
        Assert.NotNull(payload.RemediationHint);
    }

    [Fact]
    public void GetProcessStatus_ReturnsSessionNotFoundWhenExplicitSessionIsUnknown()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object, sessionId: "missing-session");

        var result = sut.GetProcessStatus(999999);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var payload = Assert.IsType<ErrorResponse>(notFound.Value);
        Assert.Equal(AgentErrorCodes.SessionNotFound, payload.ErrorCode);
        Assert.Contains("missing-session", payload.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectSession_Returns503WhenSessionLimitIsReached()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var options = Options.Create(new global::AgentOptions
        {
            AllowedExecutablePaths = [Path.GetTempPath()],
            MaxConcurrentSessions = 2,
        });
        var sessionService = new SessionService(options);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object, sessionService: sessionService);

        // Fill up the cap.
        Assert.IsType<OkObjectResult>(sut.ConnectSession(new ConnectSessionRequest("client-1")));
        Assert.IsType<OkObjectResult>(sut.ConnectSession(new ConnectSessionRequest("client-2")));

        // One more should be rejected.
        var result = sut.ConnectSession(new ConnectSessionRequest("client-3"));

        var serviceUnavailable = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, serviceUnavailable.StatusCode);
        var payload = Assert.IsType<ErrorResponse>(serviceUnavailable.Value);
        Assert.Equal(AgentErrorCodes.AgentBusy, payload.ErrorCode);
        Assert.Contains("2", payload.Error);
        Assert.False(string.IsNullOrWhiteSpace(payload.RemediationHint));
    }

    [Fact]
    public void GetProcessStatus_ReturnsRunningForTrackedProcess()
    {
        var commandInfo = ResolveLongRunningCommand();
        using var processService = CreateProcessService(
            allowedExecutablePaths: [Path.GetDirectoryName(commandInfo.Command)!]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var runResult = Assert.IsType<OkObjectResult>(
            sut.Run(new RunRequest(commandInfo.Command, commandInfo.Arguments, null)));
        var runPayload = Assert.IsType<RunResponse>(runResult.Value);

        try
        {
            var statusResult = sut.GetProcessStatus(runPayload.Pid);
            var ok = Assert.IsType<OkObjectResult>(statusResult);
            var payload = Assert.IsType<ProcessStatusResponse>(ok.Value);
            Assert.Equal(runPayload.Pid, payload.Pid);
            Assert.Equal("running", payload.Status);
            Assert.Null(payload.ExitCode);
        }
        finally
        {
            processService.Get(runPayload.Pid)?.Process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void GetProcessStatus_ReturnsNotFoundWhenProcessBelongsToDifferentSession()
    {
        var commandInfo = ResolveLongRunningCommand();
        using var processService = CreateProcessService(
            allowedExecutablePaths: [Path.GetDirectoryName(commandInfo.Command)!]);
        var sessionService = new SessionService();
        var sessionA = sessionService.Connect("session-a");
        var sessionB = sessionService.Connect("session-b");
        var uiService = new Mock<IUiAutomationService>();

        var runController = CreateController(
            processService,
            uiService.Object,
            sessionService: sessionService,
            sessionId: sessionA.SessionId);
        var statusController = CreateController(
            processService,
            uiService.Object,
            sessionService: sessionService,
            sessionId: sessionB.SessionId);

        var runResult = Assert.IsType<OkObjectResult>(
            runController.Run(new RunRequest(commandInfo.Command, commandInfo.Arguments, null)));
        var runPayload = Assert.IsType<RunResponse>(runResult.Value);

        try
        {
            var statusResult = statusController.GetProcessStatus(runPayload.Pid);
            var notFound = Assert.IsType<NotFoundObjectResult>(statusResult);
            var payload = Assert.IsType<ErrorResponse>(notFound.Value);
            Assert.Equal(AgentErrorCodes.ProcessNotFound, payload.ErrorCode);
        }
        finally
        {
            processService.Get(runPayload.Pid)?.Process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task WaitForExit_ReturnsExitedTrueForShortLivedProcess()
    {
        var commandInfo = ResolveQuickExitCommand();
        using var processService = CreateProcessService(
            allowedExecutablePaths: [Path.GetDirectoryName(commandInfo.Command)!]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var runResult = Assert.IsType<OkObjectResult>(
            sut.Run(new RunRequest(commandInfo.Command, commandInfo.Arguments, null)));
        var runPayload = Assert.IsType<RunResponse>(runResult.Value);

        var waitResult = await sut.WaitForExit(new WaitForExitRequest(runPayload.Pid, 5000));
        var ok = Assert.IsType<OkObjectResult>(waitResult);
        var payload = Assert.IsType<WaitForExitResponse>(ok.Value);

        Assert.True(payload.Exited);
        Assert.Equal("exited", payload.Process.Status);
    }

    [Fact]
    public async Task WaitForExit_ReturnsExitedFalseWhenTimeoutExpires()
    {
        var commandInfo = ResolveLongRunningCommand();
        using var processService = CreateProcessService(
            allowedExecutablePaths: [Path.GetDirectoryName(commandInfo.Command)!]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var runResult = Assert.IsType<OkObjectResult>(
            sut.Run(new RunRequest(commandInfo.Command, commandInfo.Arguments, null)));
        var runPayload = Assert.IsType<RunResponse>(runResult.Value);

        try
        {
            // Use a very short timeout — the long-running process should not exit in 50 ms.
            var waitResult = await sut.WaitForExit(new WaitForExitRequest(runPayload.Pid, 50));
            var ok = Assert.IsType<OkObjectResult>(waitResult);
            var payload = Assert.IsType<WaitForExitResponse>(ok.Value);

            Assert.False(payload.Exited);
        }
        finally
        {
            processService.Get(runPayload.Pid)?.Process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task WaitForExit_Returns499WhenRequestIsCancelled()
    {
        var commandInfo = ResolveLongRunningCommand();
        using var processService = CreateProcessService(
            allowedExecutablePaths: [Path.GetDirectoryName(commandInfo.Command)!]);
        var uiService = new Mock<IUiAutomationService>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = CreateController(processService, uiService.Object, requestAborted: cts.Token);

        var runResult = Assert.IsType<OkObjectResult>(
            sut.Run(new RunRequest(commandInfo.Command, commandInfo.Arguments, null)));
        var runPayload = Assert.IsType<RunResponse>(runResult.Value);

        try
        {
            var waitResult = await sut.WaitForExit(new WaitForExitRequest(runPayload.Pid, 5000));
            var objectResult = Assert.IsType<ObjectResult>(waitResult);
            Assert.Equal(499, objectResult.StatusCode);

            var payload = Assert.IsType<ErrorResponse>(objectResult.Value);
            Assert.Equal(AgentErrorCodes.RequestCancelled, payload.ErrorCode);
            Assert.False(string.IsNullOrWhiteSpace(payload.RemediationHint));
        }
        finally
        {
            processService.Get(runPayload.Pid)?.Process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void Terminate_ReturnsOkForTrackedRunningProcess()
    {
        var commandInfo = ResolveLongRunningCommand();
        using var processService = CreateProcessService(
            allowedExecutablePaths: [Path.GetDirectoryName(commandInfo.Command)!]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var runResult = Assert.IsType<OkObjectResult>(
            sut.Run(new RunRequest(commandInfo.Command, commandInfo.Arguments, null)));
        var runPayload = Assert.IsType<RunResponse>(runResult.Value);

        var terminateResult = sut.Terminate(new TerminateProcessRequest(runPayload.Pid));
        var ok = Assert.IsType<OkObjectResult>(terminateResult);
        var payload = Assert.IsType<StatusResponse>(ok.Value);

        Assert.Equal("ok", payload.Status);
        Assert.Contains("terminated", payload.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetUiTree_ReturnsBadRequestWhenPidInvalid()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var result = sut.GetUiTree(0);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<ErrorResponse>(bad.Value);
        Assert.Equal("pid must be a positive integer.", payload.Error);
    }

    [Fact]
    public void GetUiTree_MapsExceptionsToExpectedStatusCodes()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);

        {
            var uiService = new Mock<IUiAutomationService>();
            uiService.Setup(x => x.GetUiTree(123)).Throws(new InvalidOperationException("not found"));
            var sut = CreateController(processService, uiService.Object);
            var result = sut.GetUiTree(123);
            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            var payload = Assert.IsType<ErrorResponse>(notFound.Value);
            Assert.Equal("not found", payload.Error);
            Assert.Equal(AgentErrorCodes.ElementNotFound, payload.ErrorCode);
        }

        {
            var uiService = new Mock<IUiAutomationService>();
            uiService.Setup(x => x.GetUiTree(123)).Throws(new PlatformNotSupportedException("nope"));
            var sut = CreateController(processService, uiService.Object);
            var result = sut.GetUiTree(123);
            var notImplemented = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status501NotImplemented, notImplemented.StatusCode);
            Assert.Equal(AgentErrorCodes.PlatformNotSupported, Assert.IsType<ErrorResponse>(notImplemented.Value).ErrorCode);
        }

        {
            var uiService = new Mock<IUiAutomationService>();
            uiService.Setup(x => x.GetUiTree(123)).Throws(new Exception("boom"));
            var sut = CreateController(processService, uiService.Object);
            var result = sut.GetUiTree(123);
            var internalError = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status500InternalServerError, internalError.StatusCode);
            Assert.Equal(AgentErrorCodes.InternalError, Assert.IsType<ErrorResponse>(internalError.Value).ErrorCode);
        }
    }

    [Fact]
    public void Ready_PrunesExitedProcessEntries()
    {
        var commandInfo = ResolveQuickExitCommand();
        using var processService = CreateProcessService(
            allowedExecutablePaths: [Path.GetDirectoryName(commandInfo.Command)!]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var runResult = Assert.IsType<OkObjectResult>(
            sut.Run(new RunRequest(commandInfo.Command, commandInfo.Arguments, null)));
        var runPayload = Assert.IsType<RunResponse>(runResult.Value);

        // Wait for the quick-exit process to actually terminate before calling Ready.
        processService.Get(runPayload.Pid)?.Process.WaitForExit(5000);
        Assert.Equal(1, processService.TrackedProcessCount);

        sut.Ready();

        Assert.Equal(0, processService.TrackedProcessCount);
    }

    [Fact]
    public void GetElementState_ValidatesInputsAndReturnsState()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        Assert.IsType<BadRequestObjectResult>(sut.GetElementState(new ElementStateRequest(0, "button-ok")));
        Assert.IsType<BadRequestObjectResult>(sut.GetElementState(new ElementStateRequest(1, "")));

        uiService
            .Setup(x => x.GetElementState(42, "button-ok"))
            .Returns(new ElementStateResponse("button-ok", "button", "OK", "", null, true));

        var result = sut.GetElementState(new ElementStateRequest(42, "button-ok"));
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<ElementStateResponse>(ok.Value);
        Assert.Equal("button-ok", payload.Id);
        Assert.True(payload.Available);
    }

    [Fact]
    public async Task WaitForElement_ValidatesInputsAndReturnsResult()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        Assert.IsType<BadRequestObjectResult>(await sut.WaitForElement(new WaitForElementRequest(0, "button-ok")));
        Assert.IsType<BadRequestObjectResult>(await sut.WaitForElement(new WaitForElementRequest(1, "", 1000, 100)));
        Assert.IsType<BadRequestObjectResult>(await sut.WaitForElement(new WaitForElementRequest(1, "button-ok", 0, 100)));
        Assert.IsType<BadRequestObjectResult>(await sut.WaitForElement(new WaitForElementRequest(1, "button-ok", 1000, 0)));

        uiService
            .Setup(x => x.WaitForElement(42, "button-ok", 1000, 100, It.IsAny<CancellationToken>()))
            .Returns(new WaitForElementResponse(true, new ElementStateResponse("button-ok", "button", "OK", "", null, true)));

        var result = await sut.WaitForElement(new WaitForElementRequest(42, "button-ok", 1000, 100));
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<WaitForElementResponse>(ok.Value);
        Assert.True(payload.Found);
        Assert.NotNull(payload.Element);
    }

    [Fact]
    public async Task WaitForElement_ReturnsFoundFalseWhenTimedOut()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        uiService
            .Setup(x => x.WaitForElement(99, "button-never", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(new WaitForElementResponse(false, null));
        var sut = CreateController(processService, uiService.Object);

        var result = await sut.WaitForElement(new WaitForElementRequest(99, "button-never", 100, 10));
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<WaitForElementResponse>(ok.Value);
        Assert.False(payload.Found);
        Assert.Null(payload.Element);
    }

    [Fact]
    public async Task WaitForElement_Returns499WhenRequestIsCancelled()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        uiService
            .Setup(x => x.WaitForElement(99, "button-never", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Throws(new OperationCanceledException());

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = CreateController(processService, uiService.Object, requestAborted: cts.Token);

        var result = await sut.WaitForElement(new WaitForElementRequest(99, "button-never", 1000, 50));
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(499, objectResult.StatusCode);

        var payload = Assert.IsType<ErrorResponse>(objectResult.Value);
        Assert.Equal(AgentErrorCodes.RequestCancelled, payload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(payload.RemediationHint));
    }

    [Fact]
    public void SetFocus_ValidatesInputsAndReturnsOk()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        Assert.IsType<BadRequestObjectResult>(sut.SetFocus(new SetFocusRequest(0, "button-next")));
        Assert.IsType<BadRequestObjectResult>(sut.SetFocus(new SetFocusRequest(1, "")));

        var result = sut.SetFocus(new SetFocusRequest(42, "button-next"));
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<StatusResponse>(ok.Value);
        Assert.Equal("ok", payload.Status);
    }

    [Fact]
    public void SendKeys_ValidatesInputsAndMapsPlatformNotSupported()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        Assert.IsType<BadRequestObjectResult>(sut.SendKeys(new SendKeysRequest(0, "abc")));
        Assert.IsType<BadRequestObjectResult>(sut.SendKeys(new SendKeysRequest(1, "")));

        uiService
            .Setup(x => x.SendKeys(42, "abc"))
            .Throws(new PlatformNotSupportedException("unsupported"));

        var result = sut.SendKeys(new SendKeysRequest(42, "abc"));
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status501NotImplemented, objectResult.StatusCode);
        var payload = Assert.IsType<ErrorResponse>(objectResult.Value);
        Assert.Equal(AgentErrorCodes.PlatformNotSupported, payload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(payload.RemediationHint));
    }

    [Fact]
    public void PressHotkey_ValidatesInputsAndReturnsOk()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        Assert.IsType<BadRequestObjectResult>(sut.PressHotkey(new PressHotkeyRequest(0, ["alt", "n"])));
        Assert.IsType<BadRequestObjectResult>(sut.PressHotkey(new PressHotkeyRequest(1, [])));

        var result = sut.PressHotkey(new PressHotkeyRequest(42, ["alt", "n"]));
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<StatusResponse>(ok.Value);
        Assert.Equal("ok", payload.Status);
    }


    [Fact]
    public void SetCheckbox_ValidatesInputsAndReturnsOk()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        Assert.IsType<BadRequestObjectResult>(sut.SetCheckbox(new SetCheckboxRequest(0, "chk-eula", true)));
        Assert.IsType<BadRequestObjectResult>(sut.SetCheckbox(new SetCheckboxRequest(1, "", true)));

        var result = sut.SetCheckbox(new SetCheckboxRequest(42, "chk-eula", true));
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<StatusResponse>(ok.Value);
        Assert.Equal("ok", payload.Status);
        uiService.Verify(x => x.SetCheckbox(42, "chk-eula", true), Times.Once);
    }

    [Fact]
    public void SelectOption_ValidatesInputsAndReturnsOk()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        Assert.IsType<BadRequestObjectResult>(sut.SelectOption(new SelectOptionRequest(0, "cmb-type", "Full")));
        Assert.IsType<BadRequestObjectResult>(sut.SelectOption(new SelectOptionRequest(1, "")));
        Assert.IsType<BadRequestObjectResult>(sut.SelectOption(new SelectOptionRequest(1, "cmb-type")));

        var result = sut.SelectOption(new SelectOptionRequest(42, "cmb-type", "Full"));
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<StatusResponse>(ok.Value);
        Assert.Equal("ok", payload.Status);
        uiService.Verify(x => x.SelectOption(42, "cmb-type", "Full", null), Times.Once);
    }

    [Fact]
    public void Click_ValidatesInputsAndMapsNotFound()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var badPid = sut.Click(new ClickRequest(0, "btn"));
        Assert.IsType<BadRequestObjectResult>(badPid);

        var badElement = sut.Click(new ClickRequest(1, ""));
        Assert.IsType<BadRequestObjectResult>(badElement);

        uiService.Setup(x => x.Click(42, "missing")).Throws(new InvalidOperationException("missing"));
        var notFound = sut.Click(new ClickRequest(42, "missing"));
        var nf = Assert.IsType<NotFoundObjectResult>(notFound);
        var payload = Assert.IsType<ErrorResponse>(nf.Value);
        Assert.Equal("missing", payload.Error);
    }

    [Fact]
    public void TypeText_ValidatesInputs()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        Assert.IsType<BadRequestObjectResult>(sut.TypeText(new TypeRequest(0, "a", "b")));
        Assert.IsType<BadRequestObjectResult>(sut.TypeText(new TypeRequest(1, "", "b")));
        Assert.IsType<BadRequestObjectResult>(sut.TypeText(new TypeRequest(1, "a", null!)));
    }

    [Fact]
    public void Screenshot_MapsPlatformNotSupportedTo501()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        uiService.Setup(x => x.CaptureScreenshot(77)).Throws(new PlatformNotSupportedException("unsupported"));
        var sut = CreateController(processService, uiService.Object);

        var result = sut.GetScreenshot(77);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status501NotImplemented, objectResult.StatusCode);
    }

    [Fact]
    public void CopyFile_ReturnsBadRequestWhenDestinationPathMissing()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var result = sut.CopyFile(new CopyFileRequest("", "base64data", false));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<ErrorResponse>(bad.Value);
        Assert.Equal("DestinationPath is required.", payload.Error);
    }

    [Fact]
    public void CopyFile_ReturnsBadRequestWhenFileContentMissing()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var result = sut.CopyFile(new CopyFileRequest(Path.Combine(Path.GetTempPath(), "file.txt"), "", false));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<ErrorResponse>(bad.Value);
        Assert.Equal("FileContentBase64 is required.", payload.Error);
    }

    [Fact]
    public void CopyFile_ReturnsBadRequestWhenPathNotWhitelisted()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var outsidePath = OperatingSystem.IsWindows()
            ? "C:\\Windows\\System32\\test.txt"
            : "/etc/test.txt";

        var result = sut.CopyFile(new CopyFileRequest(outsidePath, "SGVsbG8=", false));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<ErrorResponse>(bad.Value);
        Assert.Contains("not in an allowed directory", payload.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CopyFile_RejectsPathPrefixBypass()
    {
        var allowedRoot = Path.Combine(Path.GetTempPath(), "allowed-copy-root");
        Directory.CreateDirectory(allowedRoot);
        var bypassPath = Path.Combine(allowedRoot + "-evil", "file.txt");

        using var processService = CreateProcessService(allowedExecutablePaths: [allowedRoot]);
        var uiService = new Mock<IUiAutomationService>();
        var options = Options.Create(new global::AgentOptions
        {
            AllowedExecutablePaths = [allowedRoot],
            AllowedWritablePaths = [allowedRoot],
            AllowedReadablePaths = [allowedRoot],
            MaxConcurrentProcesses = 2,
            ProcessTimeoutSeconds = 60,
        });
        var sut = CreateController(processService, uiService.Object, options);

        var result = sut.CopyFile(new CopyFileRequest(bypassPath, "SGVsbG8=", false));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<ErrorResponse>(bad.Value);
        Assert.Contains("not in an allowed directory", payload.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CopyFile_ReturnsBadRequestWhenFileExistsAndOverwriteFalse()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "test-copy.txt");
        File.WriteAllText(tempFile, "existing");
        try
        {
            using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
            var uiService = new Mock<IUiAutomationService>();
            var sut = CreateController(processService, uiService.Object);

            var result = sut.CopyFile(new CopyFileRequest(tempFile, "SGVsbG8=", false));

            var bad = Assert.IsType<BadRequestObjectResult>(result);
            var payload = Assert.IsType<ErrorResponse>(bad.Value);
            Assert.Contains("already exists", payload.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CopyFile_ReturnsOkWhenSuccessful()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "test-new-copy.txt");
        try
        {
            using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
            var uiService = new Mock<IUiAutomationService>();
            var sut = CreateController(processService, uiService.Object);

            var testContent = "Hello, World!";
            var base64Content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(testContent));
            var result = sut.CopyFile(new CopyFileRequest(tempFile, base64Content, false));

            var ok = Assert.IsType<OkObjectResult>(result);
            var payload = Assert.IsType<CopyFileResponse>(ok.Value);
            Assert.Equal(tempFile, payload.DestinationPath);
            Assert.Equal(testContent.Length, payload.BytesWritten);
            Assert.True(File.Exists(tempFile));
            Assert.Equal(testContent, File.ReadAllText(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CopyFile_ReturnsBadRequestWhenBase64Invalid()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var result = sut.CopyFile(new CopyFileRequest(Path.Combine(Path.GetTempPath(), "file.txt"), "not-valid-base64!!!", false));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<ErrorResponse>(bad.Value);
        Assert.Equal("FileContentBase64 must be valid base64.", payload.Error);
    }

    [Fact]
    public void ReadTextFile_ReturnsBadRequestWhenPathMissing()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var result = sut.ReadTextFile(new ReadTextFileRequest(""));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<ErrorResponse>(bad.Value);
        Assert.Equal("Path is required.", payload.Error);
        Assert.Equal(AgentErrorCodes.ValidationFailed, payload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(payload.RemediationHint));
    }

    [Fact]
    public void ReadTextFile_ReturnsNotFoundWithPathCodeWhenFileMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.log");
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var result = sut.ReadTextFile(new ReadTextFileRequest(missingPath));

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var payload = Assert.IsType<ErrorResponse>(notFound.Value);
        Assert.Equal(AgentErrorCodes.PathNotFound, payload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(payload.RemediationHint));
    }

    [Fact]
    public void ReadTextFile_ReturnsContentWhenFileExists()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "read-text-file-test.log");
        File.WriteAllText(filePath, "alpha\nbeta");
        try
        {
            using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
            var uiService = new Mock<IUiAutomationService>();
            var sut = CreateController(processService, uiService.Object);

            var result = sut.ReadTextFile(new ReadTextFileRequest(filePath));

            var ok = Assert.IsType<OkObjectResult>(result);
            var payload = Assert.IsType<ReadTextFileResponse>(ok.Value);
            Assert.Contains("alpha", payload.Content);
            Assert.Contains("beta", payload.Content);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TailFile_ReturnsLastLinesWhenFileExists()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "tail-file-test.log");
        File.WriteAllLines(filePath, ["line1", "line2", "line3", "line4"]);
        try
        {
            using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
            var uiService = new Mock<IUiAutomationService>();
            var sut = CreateController(processService, uiService.Object);

            var result = sut.TailFile(new TailFileRequest(filePath, 2));

            var ok = Assert.IsType<OkObjectResult>(result);
            var payload = Assert.IsType<TailFileResponse>(ok.Value);
            Assert.DoesNotContain("line1", payload.Content);
            Assert.Contains("line3", payload.Content);
            Assert.Contains("line4", payload.Content);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void TailFile_ReturnsBadRequestWhenLineCountInvalid()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var result = sut.TailFile(new TailFileRequest(Path.Combine(Path.GetTempPath(), "a.log"), 0));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<ErrorResponse>(bad.Value);
        Assert.Equal("Lines must be a positive integer.", payload.Error);
    }

    [Fact]
    public void ListDirectory_ReturnsEntriesWhenDirectoryExists()
    {
        var dirPath = Path.Combine(Path.GetTempPath(), $"adagio-list-{Guid.NewGuid():N}");
        var subDirPath = Path.Combine(dirPath, "child");
        var filePath = Path.Combine(dirPath, "a.txt");

        Directory.CreateDirectory(subDirPath);
        File.WriteAllText(filePath, "hello");

        try
        {
            using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
            var uiService = new Mock<IUiAutomationService>();
            var sut = CreateController(processService, uiService.Object);

            var result = sut.ListDirectory(new ListDirectoryRequest(dirPath));

            var ok = Assert.IsType<OkObjectResult>(result);
            var payload = Assert.IsType<ListDirectoryResponse>(ok.Value);
            Assert.Equal(Path.GetFullPath(dirPath), payload.Path);
            Assert.Contains(payload.Entries, e => e.Name == "a.txt" && !e.IsDirectory);
            Assert.Contains(payload.Entries, e => e.Name == "child" && e.IsDirectory);
        }
        finally
        {
            Directory.Delete(dirPath, recursive: true);
        }
    }

    [Fact]
    public void ListDirectory_ReturnsNotFoundWithPathCodeWhenDirectoryMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-dir-{Guid.NewGuid():N}");
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var result = sut.ListDirectory(new ListDirectoryRequest(missingPath));

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var payload = Assert.IsType<ErrorResponse>(notFound.Value);
        Assert.Equal(AgentErrorCodes.PathNotFound, payload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(payload.RemediationHint));
    }

    [Fact]
    public void FileExists_ReturnsExpectedFlags()
    {
        var dirPath = Path.Combine(Path.GetTempPath(), $"adagio-exists-{Guid.NewGuid():N}");
        var filePath = Path.Combine(dirPath, "exists.txt");

        Directory.CreateDirectory(dirPath);
        File.WriteAllText(filePath, "x");

        try
        {
            using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
            var uiService = new Mock<IUiAutomationService>();
            var sut = CreateController(processService, uiService.Object);

            var fileResult = sut.FileExists(new FileExistsRequest(filePath));
            var fileOk = Assert.IsType<OkObjectResult>(fileResult);
            var filePayload = Assert.IsType<FileExistsResponse>(fileOk.Value);
            Assert.True(filePayload.Exists);
            Assert.False(filePayload.IsDirectory);

            var missingResult = sut.FileExists(new FileExistsRequest(Path.Combine(dirPath, "missing.txt")));
            var missingOk = Assert.IsType<OkObjectResult>(missingResult);
            var missingPayload = Assert.IsType<FileExistsResponse>(missingOk.Value);
            Assert.False(missingPayload.Exists);
        }
        finally
        {
            Directory.Delete(dirPath, recursive: true);
        }
    }

    [Fact]
    public void CollectInstallArtifacts_ValidatesInputs()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        Assert.IsType<BadRequestObjectResult>(sut.CollectInstallArtifacts(new CollectInstallArtifactsRequest(0)));
        Assert.IsType<BadRequestObjectResult>(sut.CollectInstallArtifacts(new CollectInstallArtifactsRequest(1, 0)));
        Assert.IsType<BadRequestObjectResult>(sut.CollectInstallArtifacts(new CollectInstallArtifactsRequest(1, 1000, TailLines: 0)));
        Assert.IsType<BadRequestObjectResult>(sut.CollectInstallArtifacts(new CollectInstallArtifactsRequest(1, 1000, EventEntryCount: 0)));
    }

    [Fact]
    public void CollectInstallArtifacts_ReturnsProcessAndOptionalLogTail()
    {
        var commandInfo = ResolveQuickExitCommand();
        var logPath = Path.Combine(Path.GetTempPath(), $"artifact-log-{Guid.NewGuid():N}.log");
        File.WriteAllLines(logPath, ["line1", "line2", "line3"]);

        using var processService = CreateProcessService(
            allowedExecutablePaths: [Path.GetDirectoryName(commandInfo.Command)!]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        try
        {
            var runResult = Assert.IsType<OkObjectResult>(
                sut.Run(new RunRequest(commandInfo.Command, commandInfo.Arguments, null)));
            var runPayload = Assert.IsType<RunResponse>(runResult.Value);

            var result = sut.CollectInstallArtifacts(new CollectInstallArtifactsRequest(
                Pid: runPayload.Pid,
                TimeoutMilliseconds: 5000,
                LogPath: logPath,
                TailLines: 2,
                IncludeMsiEvents: false));

            var ok = Assert.IsType<OkObjectResult>(result);
            var payload = Assert.IsType<CollectInstallArtifactsResponse>(ok.Value);
            Assert.True(payload.Exited);
            Assert.Equal(runPayload.Pid, payload.Process.Pid);
            Assert.NotNull(payload.LogTail);
            Assert.DoesNotContain("line1", payload.LogTail!.Content);
            Assert.Contains("line2", payload.LogTail.Content);
            Assert.Contains("line3", payload.LogTail.Content);
            Assert.Empty(payload.MsiEvents);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [Fact]
    public void CollectInstallArtifacts_Returns499WhenRequestIsCancelled()
    {
        var commandInfo = ResolveLongRunningCommand();
        using var processService = CreateProcessService(
            allowedExecutablePaths: [Path.GetDirectoryName(commandInfo.Command)!]);
        var uiService = new Mock<IUiAutomationService>();

        var runController = CreateController(processService, uiService.Object);
        var runResult = Assert.IsType<OkObjectResult>(
            runController.Run(new RunRequest(commandInfo.Command, commandInfo.Arguments, null)));
        var runPayload = Assert.IsType<RunResponse>(runResult.Value);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = CreateController(processService, uiService.Object, requestAborted: cts.Token);

        try
        {
            var result = sut.CollectInstallArtifacts(new CollectInstallArtifactsRequest(
                Pid: runPayload.Pid,
                TimeoutMilliseconds: 5000,
                IncludeMsiEvents: false));

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(499, objectResult.StatusCode);
            var payload = Assert.IsType<ErrorResponse>(objectResult.Value);
            Assert.Equal(AgentErrorCodes.RequestCancelled, payload.ErrorCode);
            Assert.False(string.IsNullOrWhiteSpace(payload.RemediationHint));
        }
        finally
        {
            processService.Get(runPayload.Pid)?.Process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task RunInstallerAndAssert_ValidatesInputs()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        Assert.IsType<BadRequestObjectResult>(await sut.RunInstallerAndAssert(new RunInstallerAndAssertRequest("")));
        Assert.IsType<BadRequestObjectResult>(await sut.RunInstallerAndAssert(new RunInstallerAndAssertRequest("C:/Apps/setup.exe", TimeoutMilliseconds: 0)));
        Assert.IsType<BadRequestObjectResult>(await sut.RunInstallerAndAssert(new RunInstallerAndAssertRequest("C:/Apps/setup.exe", TailLines: 0)));
        Assert.IsType<BadRequestObjectResult>(await sut.RunInstallerAndAssert(new RunInstallerAndAssertRequest("C:/Apps/setup.exe", EventEntryCount: 0)));
        Assert.IsType<BadRequestObjectResult>(await sut.RunInstallerAndAssert(new RunInstallerAndAssertRequest("C:/Apps/setup.exe", LogMustContainText: "done")));
    }

    [Fact]
    public void RunInstallerAndCollectArtifacts_Returns499WhenRequestIsCancelled()
    {
        var commandInfo = ResolveLongRunningCommand();
        using var processService = CreateProcessService(
            allowedExecutablePaths: [Path.GetDirectoryName(commandInfo.Command)!]);
        var uiService = new Mock<IUiAutomationService>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = CreateController(processService, uiService.Object, requestAborted: cts.Token);

        var result = sut.RunInstallerAndCollectArtifacts(new RunInstallerAndCollectArtifactsRequest(
            Command: commandInfo.Command,
            Arguments: commandInfo.Arguments,
            TimeoutMilliseconds: 5000,
            IncludeMsiEvents: false));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(499, objectResult.StatusCode);
        var payload = Assert.IsType<ErrorResponse>(objectResult.Value);
        Assert.Equal(AgentErrorCodes.RequestCancelled, payload.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(payload.RemediationHint));

        processService.TerminateAllRunningProcesses("test-cleanup");
    }

    [Fact]
    public async Task RunInstallerAndAssert_ReturnsPassedWhenAllAssertionsSucceed()
    {
        var commandInfo = ResolveQuickExitCommand();
        var rootPath = Path.Combine(Path.GetTempPath(), $"adagio-run-assert-{Guid.NewGuid():N}");
        var expectedDir = Path.Combine(rootPath, "installed");
        var logPath = Path.Combine(rootPath, "install.log");

        Directory.CreateDirectory(expectedDir);
        File.WriteAllText(logPath, "Installation completed successfully.");

        var options = Options.Create(new global::AgentOptions
        {
            AllowedExecutablePaths = [Path.GetDirectoryName(commandInfo.Command)!],
            AllowedWritablePaths = [Path.GetTempPath()],
            AllowedReadablePaths = [Path.GetTempPath()],
            MaxConcurrentProcesses = 2,
            ProcessTimeoutSeconds = 60,
        });

        using var processService = new ProcessService(options, NullLogger<ProcessService>.Instance);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object, options);

        try
        {
            var result = await sut.RunInstallerAndAssert(new RunInstallerAndAssertRequest(
                Command: commandInfo.Command,
                Arguments: commandInfo.Arguments,
                TimeoutMilliseconds: 5000,
                LogPath: logPath,
                IncludeMsiEvents: false,
                ExpectedExitCode: 0,
                ExpectedPath: expectedDir,
                ExpectedPathMustBeDirectory: true,
                LogMustContainText: "completed",
                LogContainsIgnoreCase: true));

            var ok = Assert.IsType<OkObjectResult>(result);
            var payload = Assert.IsType<RunInstallerAndAssertResponse>(ok.Value);
            Assert.True(payload.Pid > 0);
            Assert.True(payload.Passed);
            Assert.True(payload.Artifacts.Exited);
            Assert.Equal(3, payload.Assertions.Count);
            Assert.All(payload.Assertions, assertion => Assert.True(assertion.Passed));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public void AssertProcessExited_ValidatesAndReturnsOkWhenExited()
    {
        var commandInfo = ResolveQuickExitCommand();
        using var processService = CreateProcessService(
            allowedExecutablePaths: [Path.GetDirectoryName(commandInfo.Command)!]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        Assert.IsType<BadRequestObjectResult>(sut.AssertProcessExited(new AssertProcessExitedRequest(0)));
        Assert.IsType<NotFoundObjectResult>(sut.AssertProcessExited(new AssertProcessExitedRequest(999999)));

        var runResult = Assert.IsType<OkObjectResult>(
            sut.Run(new RunRequest(commandInfo.Command, commandInfo.Arguments, null)));
        var runPayload = Assert.IsType<RunResponse>(runResult.Value);

        var result = sut.AssertProcessExited(new AssertProcessExitedRequest(runPayload.Pid, 5000, 0));
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<AssertionResponse>(ok.Value);
        Assert.True(payload.Passed);
    }

    [Fact]
    public void AssertPathExists_ValidatesAndReturnsOkWhenPresent()
    {
        var dirPath = Path.Combine(Path.GetTempPath(), $"adagio-assert-path-{Guid.NewGuid():N}");
        var filePath = Path.Combine(dirPath, "a.txt");
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(filePath, "hello");

        try
        {
            using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
            var uiService = new Mock<IUiAutomationService>();
            var sut = CreateController(processService, uiService.Object);

            Assert.IsType<BadRequestObjectResult>(sut.AssertPathExists(new AssertPathExistsRequest("")));

            var fileResult = sut.AssertPathExists(new AssertPathExistsRequest(filePath));
            var fileOk = Assert.IsType<OkObjectResult>(fileResult);
            Assert.True(Assert.IsType<AssertionResponse>(fileOk.Value).Passed);

            var dirResult = sut.AssertPathExists(new AssertPathExistsRequest(dirPath, MustBeDirectory: true));
            var dirOk = Assert.IsType<OkObjectResult>(dirResult);
            Assert.True(Assert.IsType<AssertionResponse>(dirOk.Value).Passed);
        }
        finally
        {
            Directory.Delete(dirPath, recursive: true);
        }
    }

    [Fact]
    public void AssertLogContains_ValidatesAndReturnsOkWhenMatchFound()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"adagio-assert-log-{Guid.NewGuid():N}.log");
        File.WriteAllText(filePath, "Install completed successfully");

        try
        {
            using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
            var uiService = new Mock<IUiAutomationService>();
            var sut = CreateController(processService, uiService.Object);

            Assert.IsType<BadRequestObjectResult>(sut.AssertLogContains(new AssertLogContainsRequest("", "ok")));
            Assert.IsType<BadRequestObjectResult>(sut.AssertLogContains(new AssertLogContainsRequest(filePath, "")));

            var result = sut.AssertLogContains(new AssertLogContainsRequest(filePath, "completed", IgnoreCase: true));
            var ok = Assert.IsType<OkObjectResult>(result);
            var payload = Assert.IsType<AssertionResponse>(ok.Value);
            Assert.True(payload.Passed);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static AutomationController CreateController(
        ProcessService processService,
        IUiAutomationService uiService,
        IOptions<AgentOptions>? options = null,
        IOptions<global::SecurityOptions>? securityOptions = null,
        SessionService? sessionService = null,
        string? sessionId = null,
        CancellationToken requestAborted = default)
    {
        sessionService ??= new SessionService();
        var installationResultService = new InstallationResultService(NullLogger<InstallationResultService>.Instance);

        var controller = new AutomationController(
            processService,
            sessionService,
            uiService,
            installationResultService,
            NullLogger<AutomationController>.Instance);

        // Set up mock HttpContext with RequestServices for CopyFile endpoint
        options ??= Options.Create(new global::AgentOptions
        {
            AllowedExecutablePaths = [Path.GetTempPath()],
            AllowedWritablePaths = [Path.GetTempPath()],
            AllowedReadablePaths = [Path.GetTempPath()],
            MaxConcurrentProcesses = 2,
            ProcessTimeoutSeconds = 60,
        });

        securityOptions ??= Options.Create(new global::SecurityOptions
        {
            RequireHttps = false,
            RequireApiKey = false,
        });

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(x => x.GetService(typeof(IOptions<AgentOptions>)))
            .Returns(options);
        serviceProviderMock
            .Setup(x => x.GetService(typeof(IOptions<global::SecurityOptions>)))
            .Returns(securityOptions);
        serviceProviderMock
            .Setup(x => x.GetService(typeof(SessionService)))
            .Returns(sessionService);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProviderMock.Object,
        };
        httpContext.RequestAborted = requestAborted;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            httpContext.Request.Headers[SessionService.SessionHeaderName] = sessionId;
        }
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };

        return controller;
    }

    private static ProcessService CreateProcessService(List<string> allowedExecutablePaths)
    {
        return new ProcessService(
            Options.Create(new global::AgentOptions
            {
                AllowedExecutablePaths = allowedExecutablePaths,
                MaxConcurrentProcesses = 2,
                ProcessTimeoutSeconds = 60,
            }),
            NullLogger<ProcessService>.Instance);
    }

    private static (string Command, string Arguments) ResolveLongRunningCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            var command = Path.Combine(Environment.SystemDirectory, "ping.exe");
            return (command, "127.0.0.1 -n 20");
        }

        return ("/bin/sleep", "20");
    }

    private static (string Command, string? Arguments) ResolveQuickExitCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            var command = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            return (command, "/c exit 0");
        }

        return ("/bin/true", null);
    }
}
