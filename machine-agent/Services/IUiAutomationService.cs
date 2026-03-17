using AdagioMachineAgent.Models;

namespace AdagioMachineAgent.Services;

/// <summary>
/// Platform-independent interface for UI automation and screenshots.
/// </summary>
public interface IUiAutomationService : IDisposable
{
    /// <summary>Return the UI element tree for the main window of the given process.</summary>
    UiTreeResponse GetUiTree(int pid);

    /// <summary>Click a UI element identified by its composite ID.</summary>
    void Click(int pid, string elementId);

    /// <summary>Focus a UI element and type the given text into it.</summary>
    void Type(int pid, string elementId, string text);

    /// <summary>Focus a specific UI element.</summary>
    void SetFocus(int pid, string elementId);

    /// <summary>Send keystrokes to the application window.</summary>
    void SendKeys(int pid, string text);

    /// <summary>Press a key combination in the application window.</summary>
    void PressHotkey(int pid, IReadOnlyList<string> keys);

    /// <summary>Set a checkbox or radio button to the requested checked state.</summary>
    void SetCheckbox(int pid, string elementId, bool isChecked);

    /// <summary>Select an option in a combo box or list control by text label or zero-based index.</summary>
    void SelectOption(int pid, string elementId, string? optionText, int? optionIndex);

    /// <summary>Return the current state snapshot of a UI element.</summary>
    ElementStateResponse GetElementState(int pid, string elementId);

    /// <summary>Wait for a UI element to become available.</summary>
    WaitForElementResponse WaitForElement(
        int pid,
        string elementId,
        int timeoutMilliseconds,
        int pollIntervalMilliseconds);

    /// <summary>
    /// Capture a screenshot of the main window for <paramref name="pid"/> and
    /// return it as a base64-encoded PNG string.
    /// </summary>
    string CaptureScreenshot(int pid);
}
