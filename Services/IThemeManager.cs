using System.Windows;
using GameMate.Models;

namespace GameMate.Services;

/// <summary>
/// Applies the window palette and keeps the native window chrome in step with it.
/// </summary>
public interface IThemeManager : IDisposable
{
    /// <summary>
    /// Gets the theme the user selected, which may be <see cref="AppTheme.System"/>.
    /// </summary>
    AppTheme Current { get; }

    /// <summary>
    /// Gets a value indicating whether the palette currently in use is the dark one. When
    /// <see cref="Current"/> is <see cref="AppTheme.System"/> this reflects the Windows setting.
    /// </summary>
    bool IsDark { get; }

    /// <summary>
    /// Occurs after the palette changed, either because <see cref="Apply"/> was called or because the
    /// Windows theme changed while <see cref="AppTheme.System"/> was selected.
    /// </summary>
    event EventHandler? ThemeChanged;

    /// <summary>
    /// Resolves <paramref name="theme"/> and installs the matching palette.
    /// </summary>
    /// <param name="theme">Theme to use.</param>
    /// <remarks>
    /// Calling this repeatedly with the same value is safe: the previously installed palette dictionary
    /// is removed before the replacement is inserted, so nothing accumulates.
    /// </remarks>
    void Apply(AppTheme theme);

    /// <summary>
    /// Switches the native title bar and window border of <paramref name="window"/> to match the current
    /// palette, and remembers the window so later theme changes re-apply automatically.
    /// </summary>
    /// <param name="window">Window whose non-client area should follow the theme.</param>
    /// <remarks>
    /// The window must already have a handle, so this is called from the
    /// <see cref="FrameworkElement.SourceInitialized"/> stage rather than from a constructor.
    /// </remarks>
    void ApplyNativeWindowTheme(Window window);
}
