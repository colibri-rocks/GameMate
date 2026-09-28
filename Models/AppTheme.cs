namespace GameMate.Models;

/// <summary>
/// The palette the window is drawn in.
/// </summary>
/// <remarks>
/// The numeric values are written to <c>profiles.json</c>, so they must stay stable once released.
/// </remarks>
public enum AppTheme
{
    /// <summary>
    /// Follow the Windows application theme. This is the stored default, so a freshly installed copy
    /// matches the rest of the desktop instead of imposing a palette on the user.
    /// </summary>
    System = 0,

    /// <summary>
    /// Always use the light palette, regardless of the Windows setting.
    /// </summary>
    Light = 1,

    /// <summary>
    /// Always use the dark palette, regardless of the Windows setting.
    /// </summary>
    Dark = 2,
}
