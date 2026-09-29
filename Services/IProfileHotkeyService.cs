using System.Windows;

namespace GameMate.Services;

/// <summary>
/// Owns a single system-wide (global) hotkey that raises an event when pressed.
/// </summary>
/// <remarks>
/// A global hotkey is used rather than a WPF <c>KeyBinding</c> so the shortcut keeps working while
/// GameMate is unfocused or minimised, which is the whole point for a display colour toggle.
/// </remarks>
public interface IProfileHotkeyService : IDisposable
{
    /// <summary>
    /// Occurs when the registered hotkey is pressed, on the thread that owns the window.
    /// </summary>
    event EventHandler? Pressed;

    /// <summary>
    /// Gets a value indicating whether the hotkey is currently registered.
    /// </summary>
    bool IsRegistered { get; }

    /// <summary>
    /// Registers the hotkey against a window's message queue.
    /// </summary>
    /// <param name="window">
    /// Window whose handle receives the hotkey message. It must already have a handle, so this is
    /// called from the <see cref="FrameworkElement.SourceInitialized"/> stage.
    /// </param>
    /// <param name="failureReason">
    /// User readable explanation when registration fails; otherwise <see langword="null"/>.
    /// </param>
    /// <returns><see langword="true"/> when the hotkey was registered.</returns>
    /// <remarks>
    /// This never throws. Windows refuses the registration without raising an error when another
    /// application already owns the chord, so the caller is expected to report
    /// <paramref name="failureReason"/> rather than treat the shortcut as working.
    /// </remarks>
    bool TryRegister(Window window, out string? failureReason);

    /// <summary>
    /// Releases the hotkey and detaches the message hook.
    /// </summary>
    /// <remarks>
    /// Safe to call more than once and safe to call when registration never succeeded. Releasing is
    /// essential because an unregistered chord stays owned by this process until it exits.
    /// </remarks>
    void Unregister();
}
