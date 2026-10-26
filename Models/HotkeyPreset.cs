namespace GameMate.Models;

/// <summary>
/// Global chord the application registers for the profile toggle hotkey.
/// </summary>
/// <remarks>
/// <para>
/// Values are persisted by name in <c>profiles.json</c>, because the store configures
/// <c>JsonStringEnumConverter</c>. An existing member must therefore never be renumbered or removed; new
/// members may only be appended.
/// </para>
/// <para>
/// <see cref="CtrlAltShiftP"/> is value zero and is the default, which is the chord the application always used
/// before this setting existed. A fresh installation and every pre-existing profile file therefore behave
/// exactly as before, with no migration and no schema bump.
/// </para>
/// <para>
/// F13 is a valid virtual key and registers successfully, but the key itself exists only on 122-key and other
/// extended layouts, so most users cannot physically press <see cref="F13"/>, <see cref="CtrlF13"/> or
/// <see cref="AltF13"/>. Those three are kept because they were explicitly requested, and are ordered last so
/// the physically reachable chords come first. No further guard or warning is added.
/// </para>
/// </remarks>
public enum HotkeyPreset
{
    /// <summary>
    /// Ctrl+Alt+Shift+P, the default. This is the only chord earlier versions registered.
    /// </summary>
    CtrlAltShiftP = 0,

    /// <summary>
    /// Ctrl+Alt+Shift+Insert.
    /// </summary>
    CtrlAltShiftInsert = 1,

    /// <summary>
    /// Ctrl+Alt+Shift+Home.
    /// </summary>
    CtrlAltShiftHome = 2,

    /// <summary>
    /// Ctrl+Alt+Shift+End.
    /// </summary>
    CtrlAltShiftEnd = 3,

    /// <summary>
    /// Ctrl+Alt+Shift+ScrollLock.
    /// </summary>
    CtrlAltShiftScrollLock = 4,

    /// <summary>
    /// F13 on its own. Requires an extended keyboard.
    /// </summary>
    F13 = 5,

    /// <summary>
    /// Ctrl+F13. Requires an extended keyboard.
    /// </summary>
    CtrlF13 = 6,

    /// <summary>
    /// Alt+F13. Requires an extended keyboard.
    /// </summary>
    AltF13 = 7,
}

/// <summary>
/// Maps a <see cref="HotkeyPreset"/> to the values <c>RegisterHotKey</c> needs, and to the text shown in the
/// settings window.
/// </summary>
public static class HotkeyPresetInfo
{
    /// <summary>
    /// MOD_ALT.
    /// </summary>
    private const uint ModAlt = 0x0001;

    /// <summary>
    /// MOD_CONTROL.
    /// </summary>
    private const uint ModControl = 0x0002;

    /// <summary>
    /// MOD_SHIFT.
    /// </summary>
    private const uint ModShift = 0x0004;

    /// <summary>
    /// MOD_NOREPEAT, applied to every chord so holding the keys fires the hotkey once rather than repeating.
    /// </summary>
    private const uint ModNoRepeat = 0x4000;

    // MOD_WIN would be 0x0008 if a Windows-key chord were ever added to the set.

    /// <summary>
    /// Virtual-key code of the <c>P</c> key, chosen originally because it exists on every keyboard layout.
    /// </summary>
    private const uint VirtualKeyP = 0x50;

    /// <summary>
    /// Virtual-key code of Insert.
    /// </summary>
    private const uint VirtualKeyInsert = 0x2D;

    /// <summary>
    /// Virtual-key code of Home.
    /// </summary>
    private const uint VirtualKeyHome = 0x24;

    /// <summary>
    /// Virtual-key code of End.
    /// </summary>
    private const uint VirtualKeyEnd = 0x23;

    /// <summary>
    /// Virtual-key code of ScrollLock.
    /// </summary>
    private const uint VirtualKeyScrollLock = 0x91;

    /// <summary>
    /// Virtual-key code of F13. Registers successfully, but the key exists only on extended layouts.
    /// </summary>
    private const uint VirtualKeyF13 = 0x7C;

    /// <summary>
    /// Gets the <c>RegisterHotKey</c> modifier flags for a preset.
    /// </summary>
    /// <param name="preset">Preset to describe.</param>
    /// <returns>The modifier bit flags, always including <c>MOD_NOREPEAT</c>.</returns>
    public static uint GetModifiers(HotkeyPreset preset) => preset switch
    {
        HotkeyPreset.F13 => ModNoRepeat,
        HotkeyPreset.CtrlF13 => ModControl | ModNoRepeat,
        HotkeyPreset.AltF13 => ModAlt | ModNoRepeat,
        _ => ModControl | ModAlt | ModShift | ModNoRepeat,
    };

    /// <summary>
    /// Gets the virtual-key code for a preset.
    /// </summary>
    /// <param name="preset">Preset to describe.</param>
    /// <returns>The virtual-key code passed to <c>RegisterHotKey</c>.</returns>
    public static uint GetVirtualKey(HotkeyPreset preset) => preset switch
    {
        HotkeyPreset.CtrlAltShiftInsert => VirtualKeyInsert,
        HotkeyPreset.CtrlAltShiftHome => VirtualKeyHome,
        HotkeyPreset.CtrlAltShiftEnd => VirtualKeyEnd,
        HotkeyPreset.CtrlAltShiftScrollLock => VirtualKeyScrollLock,
        HotkeyPreset.F13 or HotkeyPreset.CtrlF13 or HotkeyPreset.AltF13 => VirtualKeyF13,
        _ => VirtualKeyP,
    };

    /// <summary>
    /// Gets the chord text shown to the user, for example <c>Ctrl+Alt+Shift+P</c>.
    /// </summary>
    /// <param name="preset">Preset to describe.</param>
    /// <returns>The display text for the chord.</returns>
    /// <remarks>
    /// Deliberately a fixed string rather than a localized resource: key names read the same in every language,
    /// so translating them would only invite inconsistency between the four language files.
    /// </remarks>
    public static string GetDisplayText(HotkeyPreset preset) => preset switch
    {
        HotkeyPreset.CtrlAltShiftInsert => "Ctrl+Alt+Shift+Insert",
        HotkeyPreset.CtrlAltShiftHome => "Ctrl+Alt+Shift+Home",
        HotkeyPreset.CtrlAltShiftEnd => "Ctrl+Alt+Shift+End",
        HotkeyPreset.CtrlAltShiftScrollLock => "Ctrl+Alt+Shift+ScrollLock",
        HotkeyPreset.F13 => "F13",
        HotkeyPreset.CtrlF13 => "Ctrl+F13",
        HotkeyPreset.AltF13 => "Alt+F13",
        _ => "Ctrl+Alt+Shift+P",
    };
}
