using System.Windows;
using System.Windows.Threading;
using GameMate.Models;
using GameMate.Services;
using GameMate.ViewModels;

namespace GameMate;

/// <summary>
/// Shows the update confirmation, the download progress and the hand-over to the out-of-process helper.
/// </summary>
public partial class UpdateWindow : Window
{
    /// <summary>
    /// Applies the palette to the native title bar of this window.
    /// </summary>
    private readonly IThemeManager _themeManager;

    /// <summary>
    /// Drives the confirmation, progress and cancellation.
    /// </summary>
    private readonly UpdateViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateWindow"/> class.
    /// </summary>
    /// <param name="updateCheckService">Supplies the running version and re-checks on demand.</param>
    /// <param name="updateDownloadService">Downloads and stages the release archive.</param>
    /// <param name="updateInstaller">Starts the helper that swaps the files.</param>
    /// <param name="initialResult">Check result the main window already knows about.</param>
    /// <param name="themeManager">Applies the current palette to the native title bar.</param>
    /// <exception cref="ArgumentNullException">Thrown when any dependency is null.</exception>
    public UpdateWindow(
        IUpdateCheckService updateCheckService,
        IUpdateDownloadService updateDownloadService,
        IUpdateInstaller updateInstaller,
        UpdateCheckResult initialResult,
        IThemeManager themeManager)
    {
        ArgumentNullException.ThrowIfNull(updateCheckService);
        ArgumentNullException.ThrowIfNull(updateDownloadService);
        ArgumentNullException.ThrowIfNull(updateInstaller);
        ArgumentNullException.ThrowIfNull(initialResult);
        ArgumentNullException.ThrowIfNull(themeManager);

        _themeManager = themeManager;

        InitializeComponent();

        _viewModel = new UpdateViewModel(updateCheckService, updateDownloadService, updateInstaller, initialResult);

        DataContext = _viewModel;

        _viewModel.RestartRequested += OnRestartRequested;

        SourceInitialized += OnWindowSourceInitialized;
        Closed += OnWindowClosed;
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
    /// Closes the application so the helper can replace the files.
    /// </summary>
    /// <param name="sender">View model that completed the hand-over.</param>
    /// <param name="e">Event data.</param>
    /// <remarks>
    /// Dispatched at background priority so the final "restarting" line is painted before the process ends;
    /// shutting down synchronously would leave the user looking at a frozen window.
    /// </remarks>
    private void OnRestartRequested(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => Application.Current.Shutdown()));
    }

    /// <summary>
    /// Releases the view model when the window goes away.
    /// </summary>
    /// <param name="sender">Window that closed.</param>
    /// <param name="e">Event data.</param>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _viewModel.RestartRequested -= OnRestartRequested;
        _viewModel.Dispose();
    }
}
