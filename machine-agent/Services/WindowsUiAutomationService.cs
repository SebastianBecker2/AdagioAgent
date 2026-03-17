#if WINDOWS
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AdagioMachineAgent.Models;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
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
    private const uint KeyeventfKeyup = 0x0002;

    private static readonly Dictionary<string, byte> VirtualKeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alt"] = 0x12,
        ["ctrl"] = 0x11,
        ["control"] = 0x11,
        ["shift"] = 0x10,
        ["enter"] = 0x0D,
        ["esc"] = 0x1B,
        ["escape"] = 0x1B,
        ["tab"] = 0x09,
        ["space"] = 0x20,
        ["left"] = 0x25,
        ["up"] = 0x26,
        ["right"] = 0x27,
        ["down"] = 0x28,
        ["delete"] = 0x2E,
        ["backspace"] = 0x08,
        ["home"] = 0x24,
        ["end"] = 0x23,
        ["pageup"] = 0x21,
        ["pagedown"] = 0x22,
        ["f1"] = 0x70,
        ["f2"] = 0x71,
        ["f3"] = 0x72,
        ["f4"] = 0x73,
        ["f5"] = 0x74,
        ["f6"] = 0x75,
        ["f7"] = 0x76,
        ["f8"] = 0x77,
        ["f9"] = 0x78,
        ["f10"] = 0x79,
        ["f11"] = 0x7A,
        ["f12"] = 0x7B,
    };

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
    /// Focus a UI element.
    /// </summary>
    public void SetFocus(int pid, string elementId)
    {
        var element = FindElement(pid, elementId);
        element.Focus();
    }

    /// <summary>
    /// Send keystrokes to the focused application window.
    /// </summary>
    public void SendKeys(int pid, string text)
    {
        var app = Application.Attach(pid);
        var mainWindow = app.GetMainWindow(_automation);

        if (mainWindow == null)
        {
            throw new InvalidOperationException(
                $"No main window found for process {pid}.");
        }

        mainWindow.Focus();
        Keyboard.Type(text);
    }

    /// <summary>
    /// Press a hotkey combination in the focused application window.
    /// </summary>
    public void PressHotkey(int pid, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
        {
            throw new InvalidOperationException("At least one key is required.");
        }

        var app = Application.Attach(pid);
        var mainWindow = app.GetMainWindow(_automation);

        if (mainWindow == null)
        {
            throw new InvalidOperationException(
                $"No main window found for process {pid}.");
        }

        mainWindow.Focus();
        Thread.Sleep(50);

        var virtualKeys = keys.Select(ParseVirtualKey).ToList();
        foreach (var key in virtualKeys)
        {
            keybd_event(key, 0, 0, 0);
        }

        for (var i = virtualKeys.Count - 1; i >= 0; i--)
        {
            keybd_event(virtualKeys[i], 0, KeyeventfKeyup, 0);
        }
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

    private static byte ParseVirtualKey(string key)
    {
        if (VirtualKeyMap.TryGetValue(key, out var known))
        {
            return known;
        }

        if (key.Length == 1)
        {
            char ch = char.ToUpperInvariant(key[0]);
            if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
            {
                return (byte)ch;
            }
        }

        throw new InvalidOperationException($"Unsupported hotkey key '{key}'.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);
}
#endif
