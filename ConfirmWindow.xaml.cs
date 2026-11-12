using System.Windows;
using GameMate.Services;

namespace GameMate;

/// <summary>
/// Asks the user to confirm something that cannot be undone by a later click.
/// </summary>
/// <remarks>
/// A WPF window rather than <see cref="MessageBox"/>: the native dialog ignores the application palette, so it
/// would appear in the system light style against a dark theme, and it takes its button captions from the
/// Windows UI language instead of the language the user selected in the application. Neither can be fixed by
/// configuration, so the dialog is drawn here.
/// </remarks>
public partial class ConfirmWindow : Window
{
    /// <summary>
    /// Applies the palette to the native title bar of this window.
    /// </summary>
    private readonly IThemeManager _themeManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfirmWindow"/> class.
    /// </summary>
    /// <param name="message">Question to display, already localised and formatted.</param>
    /// <param name="themeManager">Applies the current palette to the native title bar.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="themeManager"/> is null.</exception>
    public ConfirmWindow(string message, IThemeManager themeManager)
    {
        ArgumentNullException.ThrowIfNull(themeManager);

        _themeManager = themeManager;

        InitializeComponent();

        MessageText.Text = message;

        // The window has no handle before SourceInitialized, which is also the earliest point at which the
        // native title bar can be switched to the current palette.
        SourceInitialized += OnWindowSourceInitialized;
    }

    /// <summary>
    /// Switches the native title bar to the current palette once the window has a handle.
    /// </summary>
    /// <param name="sender">Window that was initialised.</param>
    /// <param name="e">Event data.</param>
    private void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        _themeManager.ApplyNativeWindowTheme(this);
    }

    /// <summary>
    /// Accepts the action and closes the dialog.
    /// </summary>
    /// <param name="sender">Button that was clicked.</param>
    /// <param name="e">Event data.</param>
    private void OnYesClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    /// <summary>
    /// Declines the action and closes the dialog.
    /// </summary>
    /// <param name="sender">Button that was clicked.</param>
    /// <param name="e">Event data.</param>
    private void OnNoClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
