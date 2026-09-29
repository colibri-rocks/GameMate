using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GameMate.Services;

/// <inheritdoc />
/// <remarks>
/// The chord is registered with <c>RegisterHotKey</c> and observed through an <c>HwndSource</c>
/// message hook, which is the only way to receive a system-wide key press from WPF.
/// </remarks>
public sealed class ProfileHotkeyService : IProfileHotkeyService
{
    /// <summary>
    /// Modifier flags accepted by <c>RegisterHotKey</c>.
    /// </summary>
    [Flags]
    private enum HotkeyModifiers : uint
    {
        /// <summary>No modifier.</summary>
        None = 0x0,

        /// <summary>Alt key.</summary>
        Alt = 0x1,

        /// <summary>Ctrl key.</summary>
        Control = 0x2,

        /// <summary>Shift key.</summary>
        Shift = 0x4,

        /// <summary>
        /// Suppresses auto-repeat while the chord is held. Required here: without it, holding the
        /// chord re-fires continuously and rapidly ping-pongs the two profiles.
        /// </summary>
        NoRepeat = 0x4000,
    }

    /// <summary>
    /// Message Windows posts when a registered hotkey fires.
    /// </summary>
    private const int WmHotkey = 0x0312;

    /// <summary>
    /// Identifier of this hotkey, unique within the process.
    /// </summary>
    private const int HotkeyId = 0x476D;

    /// <summary>
    /// Modifier set of the profile toggle chord.
    /// </summary>
    /// <remarks>
    /// A triple modifier is deliberate. Two-modifier Alt chords are claimed by the GeForce Experience
    /// overlay namespace (Ctrl+Alt+P in particular) and plain chords clash with Windows and Steam, so
    /// Ctrl+Alt+Shift+P is a combination no mainstream application binds by default. A letter key is
    /// used because it exists on every keyboard layout, unlike punctuation keys such as '='.
    /// </remarks>
    private const HotkeyModifiers Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.NoRepeat;

    /// <summary>
    /// Virtual-key code of the chord's key: 0x50 is 'P'.
    /// </summary>
    private const uint VirtualKey = 0x50;

    private HwndSource? _source;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler? Pressed;

    /// <inheritdoc />
    public bool IsRegistered { get; private set; }

    /// <inheritdoc />
    public bool TryRegister(Window window, out string? failureReason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(window);

        if (IsRegistered)
        {
            failureReason = null;
            return true;
        }

        IntPtr handle = new WindowInteropHelper(window).Handle;

        if (handle == IntPtr.Zero)
        {
            // Registration is only possible once the window owns a message queue, so the caller has to
            // wait for SourceInitialized.
            failureReason = "The window has no handle yet, so the profile hotkey could not be registered.";
            return false;
        }

        try
        {
            if (!RegisterHotKey(handle, HotkeyId, (uint)Modifiers, VirtualKey))
            {
                // Windows reports a refused registration by returning false rather than by throwing, and
                // the usual cause is another process owning the chord. Surfacing it here is what keeps
                // the shortcut from being a silent no-op.
                failureReason = "Ctrl+Alt+Shift+P is already owned by another application, so the "
                              + "profile toggle hotkey is unavailable.";
                return false;
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            failureReason = $"The Windows hotkey API is not available ({exception.Message}).";
            return false;
        }

        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(OnWindowMessage);

        IsRegistered = true;
        failureReason = null;
        return true;
    }

    /// <inheritdoc />
    public void Unregister()
    {
        if (!IsRegistered)
        {
            return;
        }

        _source?.RemoveHook(OnWindowMessage);

        IntPtr handle = _source?.Handle ?? IntPtr.Zero;
        _source = null;

        try
        {
            if (handle != IntPtr.Zero)
            {
                // Without this the chord stays owned by the process until it exits, so a relaunch of
                // the application would fail to register it again.
                UnregisterHotKey(handle, HotkeyId);
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Nothing actionable remains at this point; process teardown releases the chord anyway.
        }

        IsRegistered = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Unregister();
    }

    /// <summary>
    /// Handles window messages, forwarding hotkey presses to <see cref="Pressed"/>.
    /// </summary>
    /// <param name="hwnd">Window the message belongs to.</param>
    /// <param name="msg">Message identifier.</param>
    /// <param name="wParam">First message parameter, holding the hotkey identifier.</param>
    /// <param name="lParam">Second message parameter.</param>
    /// <param name="handled">Receives whether the message was handled.</param>
    /// <returns>Always zero.</returns>
    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // The unhooked window receives a great many messages, so everything that is not this hotkey is
        // left untouched for other hooks and the default window procedure.
        if (msg != WmHotkey || wParam.ToInt32() != HotkeyId)
        {
            return IntPtr.Zero;
        }

        Pressed?.Invoke(this, EventArgs.Empty);

        handled = true;

        return IntPtr.Zero;
    }

    /// <summary>
    /// Registers a system-wide hotkey.
    /// </summary>
    /// <param name="windowHandle">Handle of the window that will receive the hotkey message.</param>
    /// <param name="id">Identifier of the hotkey within this window.</param>
    /// <param name="modifiers">Modifier keys that must accompany the hotkey.</param>
    /// <param name="virtualKey">Virtual-key code of the hotkey.</param>
    /// <returns><see langword="true"/> when the hotkey was registered.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    /// <summary>
    /// Releases a system-wide hotkey previously registered for a window.
    /// </summary>
    /// <param name="windowHandle">Handle of the window the hotkey was registered against.</param>
    /// <param name="id">Identifier of the hotkey to release.</param>
    /// <returns><see langword="true"/> when the hotkey was released.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
