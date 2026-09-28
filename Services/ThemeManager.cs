using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GameMate.Models;
using Microsoft.Win32;

namespace GameMate.Services;

/// <inheritdoc />
/// <remarks>
/// The palette lives in two swap-in resource dictionaries, <c>Themes/Tokens.Light.xaml</c> and
/// <c>Themes/Tokens.Dark.xaml</c>, holding an identical key set. Applying a theme replaces the
/// dictionary instance in <see cref="Application.Resources"/> instead of mutating individual brushes.
/// That is deliberate: WPF freezes brushes declared in XAML resources, so writing to
/// <c>SolidColorBrush.Color</c> afterwards silently fails, whereas replacing the whole dictionary makes
/// every <c>DynamicResource</c> consumer re-resolve on its own and keeps a single set of control styles
/// shared by both palettes.
/// </remarks>
public sealed class ThemeManager : IThemeManager
{
    /// <summary>
    /// Pack URI of the light palette, in the form that resolves against this assembly.
    /// </summary>
    private const string LightTokensSource = "/GameMate;component/Themes/Tokens.Light.xaml";

    /// <summary>
    /// Pack URI of the dark palette.
    /// </summary>
    private const string DarkTokensSource = "/GameMate;component/Themes/Tokens.Dark.xaml";

    /// <summary>
    /// Registry location of the Windows application theme preference.
    /// </summary>
    private const string PersonalizeKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// Registry value holding the Windows application theme. Zero means dark, anything else light.
    /// </summary>
    private const string AppsUseLightThemeValueName = "AppsUseLightTheme";

    /// <summary>
    /// DWM attribute that switches the native title bar to dark, available from Windows 10 20H1.
    /// </summary>
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>
    /// The undocumented attribute accepted by Windows 10 builds older than 20H1.
    /// </summary>
    private const int DwmwaUseImmersiveDarkModePre20H1 = 19;

    private static readonly Lazy<ThemeManager> SharedInstance = new(() => new ThemeManager());

    private Window? _nativeWindow;
    private bool _watchesSystemTheme;
    private bool _disposed;

    /// <summary>
    /// Gets the process-wide instance. A single shared instance is needed because the theme is applied
    /// before the main window exists and then reused by the window and its view model.
    /// </summary>
    public static ThemeManager Instance => SharedInstance.Value;

    /// <inheritdoc />
    public AppTheme Current { get; private set; } = AppTheme.System;

    /// <inheritdoc />
    public bool IsDark { get; private set; }

    /// <inheritdoc />
    public event EventHandler? ThemeChanged;

    /// <inheritdoc />
    public void Apply(AppTheme theme)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Current = theme;
        IsDark = ResolveIsDark(theme);

        SwapPaletteDictionary(IsDark);
        UpdateSystemWatcher(theme);
        ReapplyNativeWindowTheme();

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void ApplyNativeWindowTheme(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _nativeWindow = window;

        SetImmersiveDarkMode(window, IsDark);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Detaching the static SystemEvents handler matters: leaving it subscribed would keep this
        // instance alive past shutdown.
        StopWatchingSystemTheme();
    }

    /// <summary>
    /// Decides whether a theme resolves to the dark palette.
    /// </summary>
    /// <param name="theme">Selected theme.</param>
    /// <returns><see langword="true"/> when the dark palette should be used.</returns>
    private static bool ResolveIsDark(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,

            // Unknown values behave like System rather than throwing, so a hand-edited settings file
            // cannot prevent the window from opening.
            _ => IsSystemDark(),
        };
    }

    /// <summary>
    /// Reads the Windows application theme preference.
    /// </summary>
    /// <returns><see langword="true"/> when Windows is using dark application mode.</returns>
    private static bool IsSystemDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            object? value = key?.GetValue(AppsUseLightThemeValueName);

            // The value is absent on some systems and the registry is the only supported source for this
            // preference, so a missing entry is treated as light rather than as a failure.
            return value is int appsUseLight && appsUseLight == 0;
        }
        catch (Exception)
        {
            // A locked down or missing registry key must not stop the application from starting, and the
            // read is best effort by nature. Falling back to light keeps the light default of the app.
            return false;
        }
    }

    /// <summary>
    /// Replaces the palette dictionary currently merged into the application resources.
    /// </summary>
    /// <param name="dark">Whether the dark palette should be installed.</param>
    private static void SwapPaletteDictionary(bool dark)
    {
        if (Application.Current is not { } application)
        {
            // Only reachable when the manager is exercised outside a running Application, which the
            // window never does.
            return;
        }

        Collection<ResourceDictionary> merged = application.Resources.MergedDictionaries;

        // Removing first is what makes repeated applies idempotent, and it also removes the light
        // dictionary merged by App.xaml so the two never coexist.
        for (int index = merged.Count - 1; index >= 0; index--)
        {
            if (IsPaletteDictionary(merged[index]))
            {
                merged.RemoveAt(index);
            }
        }

        // Inserted at the front so dictionaries merged after it, namely the control styles, still win
        // for any key defined in both.
        merged.Insert(0, new ResourceDictionary
        {
            Source = new Uri(dark ? DarkTokensSource : LightTokensSource, UriKind.Relative),
        });
    }

    /// <summary>
    /// Determines whether a merged dictionary is one of the two palette files.
    /// </summary>
    /// <param name="dictionary">Dictionary to test.</param>
    /// <returns><see langword="true"/> when the dictionary is a palette file.</returns>
    private static bool IsPaletteDictionary(ResourceDictionary dictionary)
    {
        string? source = dictionary.Source?.OriginalString;

        return source is not null
            && source.Contains("Tokens.", StringComparison.OrdinalIgnoreCase)
            && source.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Keeps the SystemEvents subscription in step with the selected theme.
    /// </summary>
    /// <param name="theme">Theme that was just applied.</param>
    private void UpdateSystemWatcher(AppTheme theme)
    {
        if (theme == AppTheme.System)
        {
            if (!_watchesSystemTheme)
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                _watchesSystemTheme = true;
            }

            return;
        }

        StopWatchingSystemTheme();
    }

    /// <summary>
    /// Detaches the SystemEvents subscription.
    /// </summary>
    private void StopWatchingSystemTheme()
    {
        if (!_watchesSystemTheme)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _watchesSystemTheme = false;
    }

    /// <summary>
    /// Re-resolves the theme when the Windows preference changes while System is selected.
    /// </summary>
    /// <param name="sender">Event source.</param>
    /// <param name="e">Category of the change.</param>
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Only the General category tracks the applications light/dark preference; reacting to every
        // category would rebuild the palette on unrelated changes such as the accent colour.
        if (e.Category != UserPreferenceCategory.General || Current != AppTheme.System)
        {
            return;
        }

        if (IsSystemDark() == IsDark)
        {
            return;
        }

        Apply(AppTheme.System);
    }

    /// <summary>
    /// Re-applies the native chrome to the remembered window, when there is one.
    /// </summary>
    private void ReapplyNativeWindowTheme()
    {
        if (_nativeWindow is { } window)
        {
            SetImmersiveDarkMode(window, IsDark);
        }
    }

    /// <summary>
    /// Switches the native title bar and border of a window between light and dark.
    /// </summary>
    /// <param name="window">Window to update.</param>
    /// <param name="dark">Whether the native chrome should be dark.</param>
    private static void SetImmersiveDarkMode(Window window, bool dark)
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;

            if (handle == IntPtr.Zero)
            {
                // The window has no handle yet; the caller is expected to retry from
                // SourceInitialized.
                return;
            }

            int useDark = dark ? 1 : 0;

            // Attribute 20 exists from Windows 10 20H1 onwards, earlier builds only accept 19. Trying 20
            // first and falling back on a non-zero HRESULT avoids an explicit OS version check.
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModePre20H1, ref useDark, sizeof(int));
            }
        }
        catch (Exception)
        {
            // The call is purely cosmetic and dwmapi is missing on Server Core, so a failure must not
            // interrupt theming of the WPF content.
        }
    }

    /// <summary>
    /// Sets an attribute on the Desktop Window Manager for a window.
    /// </summary>
    /// <param name="windowHandle">Handle of the window to change.</param>
    /// <param name="attribute">Attribute to set.</param>
    /// <param name="value">New value of the attribute.</param>
    /// <param name="size">Size of the value in bytes.</param>
    /// <returns>Zero on success; a non-zero HRESULT otherwise.</returns>
    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int value,
        int size);
}
