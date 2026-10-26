using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GameMate.Models;

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
    /// Chord currently registered, or the chord to register on the next call.
    /// </summary>
    /// <remarks>
    /// The default is Ctrl+Alt+Shift+P: a triple modifier that no mainstream application binds, with a letter
    /// key because that exists on every keyboard layout. Auto-repeat suppression lives in the preset mapping,
    /// so holding the chord fires it once instead of ping-ponging the profiles.
    /// </remarks>
    public HotkeyPreset Preset { get; private set; } = HotkeyPreset.CtrlAltShiftP;

    private HwndSource? _source;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler? Pressed;

    /// <inheritdoc />
    public bool IsRegistered { get; private set; }

    /// <inheritdoc />
    public bool TryRegister(Window window, out string? failureReason)
    {
        return TryRegister(window, Preset, out failureReason);
    }

    /// <summary>
    /// Replaces the registered chord, restoring the previous one if the new chord is refused.
    /// </summary>
    /// <param name="window">Window that receives the hotkey message.</param>
    /// <param name="preset">Chord to register.</param>
    /// <param name="failureReason">User readable reason when the new chord was refused; otherwise null.</param>
    /// <returns><see langword="true"/> when the new chord is now registered.</returns>
    /// <remarks>
    /// Rollback is the important part: without it a refused chord would leave the application with no working
    /// hotkey at all, silently removing a shortcut the user already had.
    /// </remarks>
    public bool TryChangePreset(Window window, HotkeyPreset preset, out string? failureReason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(window);

        if (IsRegistered && Preset == preset)
        {
            failureReason = null;
            return true;
        }

        HotkeyPreset previous = Preset;

        // Released first so the old chord cannot keep firing alongside the new one.
        Unregister();

        if (TryRegister(window, preset, out failureReason))
        {
            return true;
        }

        // Restore the chord the user had before the refused change.
        if (TryRegister(window, previous, out _))
        {
            // The rollback succeeded, so the caller still has a working hotkey to report against.
            failureReason ??= GameMate.Localization.LanguageService.Current.Format(
                "Error.HotkeyConflict",
                HotkeyPresetInfo.GetDisplayText(preset));
        }

        return false;
    }

    /// <summary>
    /// Registers a specific chord against a window.
    /// </summary>
    /// <param name="window">Window that receives the hotkey message.</param>
    /// <param name="preset">Chord to register.</param>
    /// <param name="failureReason">User readable reason when registration failed; otherwise null.</param>
    /// <returns><see langword="true"/> when the chord is now registered.</returns>
    private bool TryRegister(Window window, HotkeyPreset preset, out string? failureReason)
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

        HotkeyModifiers modifiers = (HotkeyModifiers)HotkeyPresetInfo.GetModifiers(preset);
        uint virtualKey = HotkeyPresetInfo.GetVirtualKey(preset);

        try
        {
            if (!RegisterHotKey(handle, HotkeyId, (uint)modifiers, virtualKey))
            {
                // Windows reports a refused registration by returning false rather than by throwing, and
                // the usual cause is another process owning the chord. Surfacing it here is what keeps
                // the shortcut from being a silent no-op.
                failureReason = GameMate.Localization.LanguageService.Current.Format(
                    "Error.HotkeyConflict",
                    HotkeyPresetInfo.GetDisplayText(preset));
                return false;
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            failureReason = GameMate.Localization.LanguageService.Current.Format("Error.HotkeyApiUnavailable", exception.Message);
            return false;
        }

        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(OnWindowMessage);

        Preset = preset;
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
