using System.Windows;
using GameMate.Services;
using GameMate.ViewModels;

namespace GameMate;

/// <summary>
/// Interaction logic for SettingsWindow.xaml.
/// </summary>
/// <remarks>
/// A thin view over the state <see cref="MainViewModel"/> already owns, so the theme selected here is the same
/// theme the whole application resolves. Nothing is duplicated, and closing the window discards no state
/// because every change is applied and persisted as it is made.
/// </remarks>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// Applies the palette and keeps the native title bar in step with it.
    /// </summary>
    private readonly IThemeManager _themeManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
    /// </summary>
    /// <param name="viewModel">View model shared with the main window, supplying the theme and language state.</param>
    /// <param name="themeManager">Applied when the native chrome has to follow a theme change.</param>
    /// <exception cref="ArgumentNullException">Thrown when any dependency is null.</exception>
    public SettingsWindow(MainViewModel viewModel, IThemeManager themeManager)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(themeManager);

        InitializeComponent();

        _themeManager = themeManager;

        DataContext = viewModel;

        // The window has no handle before SourceInitialized, which is the earliest point at which the native
        // title bar can be switched to dark.
        SourceInitialized += OnSourceInitialized;

        Closed += OnClosed;
    }

    /// <summary>
    /// Switches the native title bar to the palette that is currently selected.
    /// </summary>
    /// <param name="sender">Window that was initialised.</param>
    /// <param name="e">Event data.</param>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _themeManager.ApplyNativeWindowTheme(this);
    }

    /// <summary>
    /// Hands the native title bar back to the window that opened this one.
    /// </summary>
    /// <param name="sender">Window that closed.</param>
    /// <param name="e">Event data.</param>
    private void OnClosed(object? sender, EventArgs e)
    {
        // ApplyNativeWindowTheme remembers a single window, so without this a theme change made after the
        // dialog is gone would keep updating a window that no longer exists.
        if (Owner is Window owner)
        {
            _themeManager.ApplyNativeWindowTheme(owner);
        }
    }
}
