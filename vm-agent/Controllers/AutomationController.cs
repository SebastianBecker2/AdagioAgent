using AdagioVmAgent.Models;
using AdagioVmAgent.Services;
using Microsoft.AspNetCore.Mvc;

namespace AdagioVmAgent.Controllers;

/// <summary>
/// REST API surface exposed to the VS Code controller extension.
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class AutomationController : ControllerBase
{
    private readonly ProcessService _processService;
    private readonly UiAutomationService _uiService;
    private readonly ILogger<AutomationController> _logger;

    private static readonly string AgentVersion =
        typeof(AutomationController).Assembly.GetName().Version?.ToString() ?? "0.1.0";

    public AutomationController(
        ProcessService processService,
        UiAutomationService uiService,
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

    /// <summary>Start an installer process and return its PID.</summary>
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
}
