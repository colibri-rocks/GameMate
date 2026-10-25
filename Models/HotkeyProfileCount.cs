namespace GameMate.Models;

/// <summary>
/// How many profile slots the global hotkey steps through.
/// </summary>
/// <remarks>
/// The numeric values are persisted in <c>profiles.json</c>, so an existing member must never be renumbered or
/// removed; new members may only be appended. Reading stays tolerant because the persisted property defaults to
/// <see cref="Two"/>, which means a file written before this setting existed keeps the old behaviour.
/// </remarks>
public enum HotkeyProfileCount
{
    /// <summary>
    /// The hotkey toggles between profile slots 1 and 2, which is what the application did before this setting
    /// existed and is therefore the default.
    /// </summary>
    Two = 0,

    /// <summary>
    /// The hotkey advances through all three profile slots and wraps: 1, then 2, then 3, then 1 again.
    /// </summary>
    Three = 1,
}
