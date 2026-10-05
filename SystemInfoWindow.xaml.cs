using System.Runtime.InteropServices;
using System.Windows;
using GameMate.Services;
using GameMate.ViewModels;

namespace GameMate;

/// <summary>
/// Interaction logic for SystemInfoWindow.xaml. Shows the operating system, the video adapters and the
/// monitors detected at startup.
/// </summary>
public partial class SystemInfoWindow : Window
{
    /// <summary>
    /// Applies the palette to the native title bar of this window.
    /// </summary>
    private readonly IThemeManager _themeManager;

    /// <summary>
    /// View model that supplies both the displayed information and its plain-text report.
    /// </summary>
    private readonly SystemInfoViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemInfoWindow"/> class.
    /// </summary>
    /// <param name="viewModel">View model that supplies the information to display.</param>
    /// <param name="themeManager">Applies the current palette to the native title bar.</param>
    /// <exception cref="ArgumentNullException">Thrown when any dependency is null.</exception>
    public SystemInfoWindow(SystemInfoViewModel viewModel, IThemeManager themeManager)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(themeManager);

        _viewModel = viewModel;
        _themeManager = themeManager;

        InitializeComponent();

        DataContext = _viewModel;

        // The window has no handle before SourceInitialized, which is also the earliest point at which
        // the native title bar can be switched to dark.
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
    /// Places the whole report on the clipboard.
    /// </summary>
    /// <param name="sender">Button that was clicked.</param>
    /// <param name="e">Event data.</param>
    /// <remarks>
    /// WPF has no declarative binding for the clipboard, so this lives in the code behind, matching the
    /// existing lost-focus rename handler.
    /// </remarks>
    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_viewModel.ReportText);
        }
        catch (Exception exception) when (exception is COMException or ExternalException)
        {
            // Another process can hold the clipboard open, in which case SetText throws rather than
            // failing quietly. Reporting it is what keeps the click from looking like it did nothing.
            MessageBox.Show(
                this,
                $"The report could not be copied because the clipboard is in use by another application."
                + $"{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "GameMate - Copy failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
