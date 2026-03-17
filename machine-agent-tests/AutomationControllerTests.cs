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
    public void WaitForElement_ValidatesInputsAndReturnsResult()
    {
        using var processService = CreateProcessService(allowedExecutablePaths: [Path.GetTempPath()]);
        var uiService = new Mock<IUiAutomationService>();
        var sut = CreateController(processService, uiService.Object);

        Assert.IsType<BadRequestObjectResult>(sut.WaitForElement(new WaitForElementRequest(0, "button-ok")));
        Assert.IsType<BadRequestObjectResult>(sut.WaitForElement(new WaitForElementRequest(1, "", 1000, 100)));
        Assert.IsType<BadRequestObjectResult>(sut.WaitForElement(new WaitForElementRequest(1, "button-ok", 0, 100)));
        Assert.IsType<BadRequestObjectResult>(sut.WaitForElement(new WaitForElementRequest(1, "button-ok", 1000, 0)));

        uiService
            .Setup(x => x.WaitForElement(42, "button-ok", 1000, 100))
            .Returns(new WaitForElementResponse(true, new ElementStateResponse("button-ok", "button", "OK", "", null, true)));

        var result = sut.WaitForElement(new WaitForElementRequest(42, "button-ok", 1000, 100));
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<WaitForElementResponse>(ok.Value);
        Assert.True(payload.Found);
        Assert.NotNull(payload.Element);
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

        var result = sut.CopyFile(new CopyFileRequest("C:\\Windows\\System32\\test.txt", "SGVsbG8=", false));

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
            AllowedWritablePaths = [Path.GetTempPath()],
            AllowedReadablePaths = [Path.GetTempPath()],
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
