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

    private static AutomationController CreateController(ProcessService processService, IUiAutomationService uiService)
    {
        return new AutomationController(
            processService,
            uiService,
            NullLogger<AutomationController>.Instance);
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
}
