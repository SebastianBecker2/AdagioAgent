#if WINDOWS
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AdagioMachineAgent.Models;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace AdagioMachineAgent.Services;

/// <summary>
/// Windows implementation of <see cref="IUiAutomationService"/> using FlaUI (UIA3)
/// and System.Drawing. Only supported on Windows.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsUiAutomationService : IUiAutomationService
{
    private readonly UIA3Automation _automation = new();

    // ── UI tree ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Return the UI element tree for the main window of the given process.
    /// </summary>
    public UiTreeResponse GetUiTree(int pid)
    {
        var app = Application.Attach(pid);
        var mainWindow = app.GetMainWindow(_automation);

        if (mainWindow == null)
        {
            throw new InvalidOperationException(
                $"No main window found for process {pid}.");
        }

        var elements = Walk(mainWindow).ToList();
        return new UiTreeResponse(mainWindow.Title, elements);
    }

    // ── Click ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Click a UI element identified by its composite ID (see <see cref="BuildId"/>).
    /// </summary>
    public void Click(int pid, string elementId)
    {
        var element = FindElement(pid, elementId);
        element.Click();
    }

    // ── Type ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Focus a UI element and type the given text into it.
    /// </summary>
    public void Type(int pid, string elementId, string text)
    {
        var element = FindElement(pid, elementId);
        element.Focus();
        element.AsTextBox()?.Enter(text);
    }

    /// <summary>
    /// Return the current state of a UI element.
    /// </summary>
    public ElementStateResponse GetElementState(int pid, string elementId)
    {
        var element = FindElement(pid, elementId);
        return ToElementState(element);
    }

    /// <summary>
    /// Wait for an element to become available until timeout.
    /// </summary>
    public WaitForElementResponse WaitForElement(int pid, string elementId, int timeoutMilliseconds, int pollIntervalMilliseconds)
    {
        var startedAt = DateTime.UtcNow;

        while ((DateTime.UtcNow - startedAt).TotalMilliseconds < timeoutMilliseconds)
        {
            var match = TryFindElement(pid, elementId);
            if (match is not null)
            {
                return new WaitForElementResponse(true, ToElementState(match));
            }

            Thread.Sleep(pollIntervalMilliseconds);
        }

        return new WaitForElementResponse(false, null);
    }

    // ── Screenshot ───────────────────────────────────────────────────────────

    /// <summary>
    /// Capture a screenshot of the main window for <paramref name="pid"/> and
    /// return it as a base64-encoded PNG string.
    /// </summary>
    public string CaptureScreenshot(int pid)
    {
        var app = Application.Attach(pid);
        var mainWindow = app.GetMainWindow(_automation);

        if (mainWindow == null)
        {
            throw new InvalidOperationException(
                $"No main window found for process {pid}.");
        }

        var bounds = mainWindow.BoundingRectangle;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0,
                new Size(bounds.Width, bounds.Height),
                CopyPixelOperation.SourceCopy);
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return Convert.ToBase64String(ms.ToArray());
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    public void Dispose() => _automation.Dispose();

    // ── Private helpers ──────────────────────────────────────────────────────

    private AutomationElement FindElement(int pid, string elementId)
    {
        var match = TryFindElement(pid, elementId);
        if (match == null)
        {
            throw new InvalidOperationException(
                $"Element '{elementId}' not found in the UI tree of process {pid}.");
        }

        return match;
    }

    private AutomationElement? TryFindElement(int pid, string elementId)
    {
        var app = Application.Attach(pid);
        var mainWindow = app.GetMainWindow(_automation);

        if (mainWindow == null)
        {
            throw new InvalidOperationException(
                $"No main window found for process {pid}.");
        }

        // Walk all descendants to find the one whose ID matches
        return FindById(mainWindow, elementId);
    }

    private static AutomationElement? FindById(AutomationElement root, string id)
    {
        if (BuildId(root) == id)
        {
            return root;
        }

        foreach (var child in root.FindAllChildren())
        {
            var found = FindById(child, id);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<UiElement> Walk(AutomationElement element)
    {
        foreach (var child in element.FindAllChildren())
        {
            yield return ToDto(child);
        }
    }

    private static UiElement ToDto(AutomationElement el)
    {
        var children = el.FindAllChildren()
            .Select(ToDto)
            .ToList();

        var r = el.BoundingRectangle;
        Bounds? bounds = r.IsEmpty
            ? null
            : new Bounds(r.X, r.Y, r.Width, r.Height);

        return new UiElement(
            Id: BuildId(el),
            Type: el.ControlType.ToString(),
            Name: el.Name ?? string.Empty,
            AutomationId: el.AutomationId ?? string.Empty,
            Bounds: bounds,
            Children: children.Count > 0 ? children : null);
    }

    private static ElementStateResponse ToElementState(AutomationElement el)
    {
        var r = el.BoundingRectangle;
        Bounds? bounds = r.IsEmpty
            ? null
            : new Bounds(r.X, r.Y, r.Width, r.Height);

        return new ElementStateResponse(
            Id: BuildId(el),
            Type: el.ControlType.ToString(),
            Name: el.Name ?? string.Empty,
            AutomationId: el.AutomationId ?? string.Empty,
            Bounds: bounds,
            Available: true);
    }

    /// <summary>
    /// Build a stable, human-readable element ID from control type + automation ID + name.
    /// </summary>
    private static string BuildId(AutomationElement el)
    {
        var parts = new List<string> { el.ControlType.ToString() };
        if (!string.IsNullOrEmpty(el.AutomationId)) parts.Add(el.AutomationId);
        else if (!string.IsNullOrEmpty(el.Name)) parts.Add(el.Name.Replace(' ', '-'));
        return string.Join("-", parts).ToLowerInvariant();
    }
}
#endif
