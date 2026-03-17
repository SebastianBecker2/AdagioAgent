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
    public void WaitForExit_ReturnsExitedTrueForShortLivedProcess()
    {
        var commandInfo = ResolveQuickExitCommand();
        using var processService = CreateProcessService(
            allowedExecutablePaths: [Path.GetDirectoryName(commandInfo.Command)!]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        var runResult = Assert.IsType<OkObjectResult>(
            sut.Run(new RunRequest(commandInfo.Command, commandInfo.Arguments, null)));
        var runPayload = Assert.IsType<RunResponse>(runResult.Value);

        var waitResult = sut.WaitForExit(new WaitForExitRequest(runPayload.Pid, 5000));
        var ok = Assert.IsType<OkObjectResult>(waitResult);
        var payload = Assert.IsType<WaitForExitResponse>(ok.Value);

        Assert.True(payload.Exited);
        Assert.Equal("exited", payload.Process.Status);
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
        }

        {
            var uiService = new Mock<IUiAutomationService>();
            uiService.Setup(x => x.GetUiTree(123)).Throws(new PlatformNotSupportedException("nope"));
            var sut = CreateController(processService, uiService.Object);
            var result = sut.GetUiTree(123);
            var notImplemented = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status501NotImplemented, notImplemented.StatusCode);
        }

        {
            var uiService = new Mock<IUiAutomationService>();
            uiService.Setup(x => x.GetUiTree(123)).Throws(new Exception("boom"));
            var sut = CreateController(processService, uiService.Object);
            var result = sut.GetUiTree(123);
            var internalError = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status500InternalServerError, internalError.StatusCode);
        }
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

        var result = sut.CopyFile(new CopyFileRequest("C:\\Windows\\System32\\test.txt", "SGVsbG8=", false));

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
    private static AutomationController CreateController(ProcessService processService, IUiAutomationService uiService, IOptions<AgentOptions>? options = null)
    {
        var controller = new AutomationController(
            processService,
            uiService,
            NullLogger<AutomationController>.Instance);

        // Set up mock HttpContext with RequestServices for CopyFile endpoint
        options ??= Options.Create(new global::AgentOptions
        {
            AllowedExecutablePaths = [Path.GetTempPath()],
            MaxConcurrentProcesses = 2,
            ProcessTimeoutSeconds = 60,
        });

        var httpContextMock = new Mock<HttpContext>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(x => x.GetService(typeof(IOptions<AgentOptions>)))
            .Returns(options);

        httpContextMock.Setup(x => x.RequestServices).Returns(serviceProviderMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
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
