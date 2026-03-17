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

    // ── GET /health ──────────────────────────────────────────────────────────

    [HttpGet("/health")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public IActionResult Health()
    {
        return Ok(new HealthResponse("healthy", AgentVersion));
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
            return BadRequest(new ErrorResponse("Command is required."));
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
            return BadRequest(new ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start process.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to start process.", ex.Message));
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
            return BadRequest(new ErrorResponse("pid must be a positive integer."));
        }

        var tracked = _processService.Get(pid);
        if (tracked is null)
        {
            return NotFound(new ErrorResponse($"Process {pid} is not tracked."));
        }

        return Ok(ToProcessStatus(tracked));
    }

    // ── POST /wait-for-exit ────────────────────────────────────────────────

    /// <summary>Wait for a tracked process to exit or timeout.</summary>
    [HttpPost("/wait-for-exit")]
    [ProducesResponseType(typeof(WaitForExitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult WaitForExit([FromBody] WaitForExitRequest request)
    {
        if (request.Pid <= 0)
        {
            return BadRequest(new ErrorResponse("pid must be a positive integer."));
        }

        if (request.TimeoutMilliseconds <= 0)
        {
            return BadRequest(new ErrorResponse("timeoutMilliseconds must be a positive integer."));
        }

        var tracked = _processService.Get(request.Pid);
        if (tracked is null)
        {
            return NotFound(new ErrorResponse($"Process {request.Pid} is not tracked."));
        }

        try
        {
            var exited = tracked.Process.WaitForExit(request.TimeoutMilliseconds);
            return Ok(new WaitForExitResponse(exited, ToProcessStatus(tracked)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed while waiting for process {Pid} exit.", request.Pid);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed while waiting for process exit.", ex.Message));
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
            return BadRequest(new ErrorResponse("pid must be a positive integer."));
        }

        var tracked = _processService.Get(request.Pid);
        if (tracked is null)
        {
            return NotFound(new ErrorResponse($"Process {request.Pid} is not tracked."));
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
                new ErrorResponse("Failed to terminate process.", ex.Message));
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
            return BadRequest(new ErrorResponse("pid must be a positive integer."));
        }

        try
        {
            var tree = _uiService.GetUiTree(pid);
            return Ok(tree);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetUiTree failed for pid {Pid}.", pid);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to retrieve UI tree.", ex.Message));
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
            return BadRequest(new ErrorResponse("pid must be a positive integer."));
        }

        if (string.IsNullOrWhiteSpace(request.ElementId))
        {
            return BadRequest(new ErrorResponse("elementId is required."));
        }

        try
        {
            return Ok(_uiService.GetElementState(request.Pid, request.ElementId));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetElementState failed for element {ElementId}.", request.ElementId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to retrieve element state.", ex.Message));
        }
    }

    // ── POST /wait-for-element ─────────────────────────────────────────────

    /// <summary>Wait until a UI element becomes available or timeout is reached.</summary>
    [HttpPost("/wait-for-element")]
    [ProducesResponseType(typeof(WaitForElementResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public IActionResult WaitForElement([FromBody] WaitForElementRequest request)
    {
        if (request.Pid <= 0)
        {
            return BadRequest(new ErrorResponse("pid must be a positive integer."));
        }

        if (string.IsNullOrWhiteSpace(request.ElementId))
        {
            return BadRequest(new ErrorResponse("elementId is required."));
        }

        if (request.TimeoutMilliseconds <= 0)
        {
            return BadRequest(new ErrorResponse("timeoutMilliseconds must be a positive integer."));
        }

        if (request.PollIntervalMilliseconds <= 0)
        {
            return BadRequest(new ErrorResponse("pollIntervalMilliseconds must be a positive integer."));
        }

        try
        {
            return Ok(_uiService.WaitForElement(
                request.Pid,
                request.ElementId,
                request.TimeoutMilliseconds,
                request.PollIntervalMilliseconds));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WaitForElement failed for element {ElementId}.", request.ElementId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed while waiting for element.", ex.Message));
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
            return BadRequest(new ErrorResponse("pid must be a positive integer."));
        }

        if (string.IsNullOrWhiteSpace(request.ElementId))
        {
            return BadRequest(new ErrorResponse("elementId is required."));
        }

        try
        {
            _uiService.SetFocus(request.Pid, request.ElementId);
            return Ok(new StatusResponse("ok"));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetFocus failed for element {ElementId}.", request.ElementId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to focus element.", ex.Message));
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
            return BadRequest(new ErrorResponse("pid must be a positive integer."));
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new ErrorResponse("text is required."));
        }

        try
        {
            _uiService.SendKeys(request.Pid, request.Text);
            return Ok(new StatusResponse("ok"));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendKeys failed for pid {Pid}.", request.Pid);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to send keys.", ex.Message));
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
            return BadRequest(new ErrorResponse("pid must be a positive integer."));
        }

        try
        {
            var base64 = _uiService.CaptureScreenshot(pid);
            return Ok(new ScreenshotResponse(base64));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Screenshot failed for pid {Pid}.", pid);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to capture screenshot.", ex.Message));
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
            return BadRequest(new ErrorResponse("pid must be a positive integer."));
        }

        if (string.IsNullOrWhiteSpace(request.ElementId))
        {
            return BadRequest(new ErrorResponse("elementId is required."));
        }

        try
        {
            _uiService.Click(request.Pid, request.ElementId);
            return Ok(new StatusResponse("ok"));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Click failed for element {ElementId}.", request.ElementId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to click element.", ex.Message));
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
            return BadRequest(new ErrorResponse("pid must be a positive integer."));
        }

        if (string.IsNullOrWhiteSpace(request.ElementId))
        {
            return BadRequest(new ErrorResponse("elementId is required."));
        }

        if (request.Text is null)
        {
            return BadRequest(new ErrorResponse("text is required."));
        }

        try
        {
            _uiService.Type(request.Pid, request.ElementId, request.Text);
            return Ok(new StatusResponse("ok"));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new ErrorResponse(ex.Message));
        }
        catch (PlatformNotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Type failed for element {ElementId}.", request.ElementId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to type text.", ex.Message));
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
            return BadRequest(new ErrorResponse("DestinationPath is required."));
        }

        if (string.IsNullOrWhiteSpace(request.FileContentBase64))
        {
            return BadRequest(new ErrorResponse("FileContentBase64 is required."));
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
                return BadRequest(new ErrorResponse(
                    $"Destination path '{request.DestinationPath}' is not in an allowed directory. " +
                    $"Allowed paths: {string.Join(", ", options.Value.AllowedWritablePaths)}"));
            }

            // Check if file exists and overwrite flag
            if (System.IO.File.Exists(destinationPath) && !request.OverwriteIfExists)
            {
                return BadRequest(new ErrorResponse(
                    $"File already exists at '{request.DestinationPath}' and overwrite is not enabled."));
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
            return BadRequest(new ErrorResponse("FileContentBase64 must be valid base64.", ex.Message));
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
            return BadRequest(new ErrorResponse("Path is required."));
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
                return BadRequest(new ErrorResponse(
                    $"Path '{request.Path}' is not in an allowed directory. " +
                    $"Allowed paths: {string.Join(", ", options.Value.AllowedReadablePaths)}"));
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
            return BadRequest(new ErrorResponse("Path is required."));
        }

        if (request.Lines <= 0)
        {
            return BadRequest(new ErrorResponse("Lines must be a positive integer."));
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
                return BadRequest(new ErrorResponse(
                    $"Path '{request.Path}' is not in an allowed directory. " +
                    $"Allowed paths: {string.Join(", ", options.Value.AllowedReadablePaths)}"));
            }

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new ErrorResponse($"File '{request.Path}' does not exist."));
            }

            var allLines = System.IO.File.ReadAllLines(fullPath);
            var start = Math.Max(0, allLines.Length - request.Lines);
            var content = string.Join(Environment.NewLine, allLines.Skip(start));
            return Ok(new TailFileResponse(fullPath, request.Lines, content));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to tail file '{Path}'.", request.Path);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse("Failed to tail file.", ex.Message));
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
}
