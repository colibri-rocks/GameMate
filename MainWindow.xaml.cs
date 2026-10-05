using System.Windows;
using System.Windows.Controls;
using GameMate.Services;
using GameMate.ViewModels;

namespace GameMate;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// View model backing the whole window.
    /// </summary>
    private readonly MainViewModel _viewModel;

    /// <summary>
    /// Applies the palette and keeps the native title bar in step with it.
    /// </summary>
    private readonly IThemeManager _themeManager;

    /// <summary>
    /// Owns the system-wide profile toggle hotkey.
    /// </summary>
    private readonly ProfileHotkeyService _hotkeyService = new();

    /// <summary>
    /// Reads the machine information shown by the system information window.
    /// </summary>
    private readonly ISystemInfoService _systemInfoService;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class and wires the concrete
    /// services, so the window owns the only composition root of the application.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // Assigned before the view model so the same manager instance can be injected into it.
        _themeManager = ThemeManager.Instance;

        // One instance is shared: it caches the NVAPI probe that both the colour controls and the
        // system information window depend on, so probing twice would be wasteful.
        DisplayService displayService = new();

        _viewModel = new MainViewModel(
            displayService,
            new GammaRampDevice(),
            new NvidiaColorService(),
            new ProfileStore(),
            _themeManager);

        _systemInfoService = new SystemInfoService(displayService);

        DataContext = _viewModel;

        // The window has no handle before SourceInitialized, which is also the earliest point at which
        // the native title bar can be switched to dark.
        SourceInitialized += OnWindowSourceInitialized;

        // Re-applies the native chrome whenever the user cycles to another theme.
        _themeManager.ThemeChanged += OnThemeChanged;

        // Creating a window is a view concern, so the view model only asks for it and this window builds
        // it, which keeps the view model free of any dependency on WPF windows.
        _viewModel.SystemInfoRequested += OnSystemInfoRequested;

        // The hotkey has to work while GameMate is unfocused, so it is registered globally rather than
        // as a key binding.
        _hotkeyService.Pressed += OnProfileHotkeyPressed;

        // Display enumeration and the NVAPI probe run after the window is shown, so a slow or missing
        // driver can neither delay nor break startup.
        Loaded += OnWindowLoaded;
        Closed += OnWindowClosed;
    }

    /// <summary>
    /// Populates the window once it is on screen.
    /// </summary>
    /// <param name="sender">Window that was loaded.</param>
    /// <param name="e">Event data.</param>
    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnWindowLoaded;

        _viewModel.Initialize();

        // SizeToContent re-measures the window from the content tree on every layout pass and overrides a
        // manual resize, so height control is handed back to the user (and to MinHeight) once the window
        // has sized itself. This must run after Initialize, which is what establishes the final content
        // height: setting it earlier would capture the not-yet-populated window and reintroduce clipping.
        SizeToContent = SizeToContent.Manual;
    }

    /// <summary>
    /// Flushes the pending profile write when the window closes.
    /// </summary>
    /// <param name="sender">Window that closed.</param>
    /// <param name="e">Event data.</param>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // The theme manager outlives this window, so the subscription has to be released explicitly.
        _themeManager.ThemeChanged -= OnThemeChanged;
        _viewModel.SystemInfoRequested -= OnSystemInfoRequested;

        // Releasing the chord here matters: an unregistered global hotkey stays owned by the process
        // until it exits, so a relaunch would fail to register it again.
        _hotkeyService.Pressed -= OnProfileHotkeyPressed;
        _hotkeyService.Dispose();

        _viewModel.Dispose();
    }

    /// <summary>
    /// Switches the native title bar to the current palette and claims the profile toggle hotkey once
    /// the window has a handle.
    /// </summary>
    /// <param name="sender">Window that was initialised.</param>
    /// <param name="e">Event data.</param>
    private void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        _themeManager.ApplyNativeWindowTheme(this);

        // A global hotkey can only be registered against a window that already owns a message queue,
        // which is exactly what this event guarantees.
        if (!_hotkeyService.TryRegister(this, out string? failureReason))
        {
            // Windows refuses a chord another application owns without raising an error, so the outcome
            // is reported rather than left as a silently dead shortcut.
            _viewModel.ReportHotkeyUnavailable(failureReason);
        }
    }

    /// <summary>
    /// Applies the profile toggle when the global hotkey fires.
    /// </summary>
    /// <param name="sender">Hotkey service that raised the event.</param>
    /// <param name="e">Event data.</param>
    private void OnProfileHotkeyPressed(object? sender, EventArgs e)
    {
        _viewModel.ToggleProfileOneAndTwo();
    }

    /// <summary>
    /// Re-applies the native title bar colour after the user changed the theme.
    /// </summary>
    /// <param name="sender">Theme manager that raised the event.</param>
    /// <param name="e">Event data.</param>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _themeManager.ApplyNativeWindowTheme(this);
    }

    /// <summary>
    /// Opens the system information window on behalf of the view model.
    /// </summary>
    /// <param name="sender">View model that raised the request.</param>
    /// <param name="e">Event data.</param>
    private void OnSystemInfoRequested(object? sender, EventArgs e)
    {
        // The monitors were already enumerated at startup, so the window reuses that list instead of
        // enumerating the displays a second time; only the operating system and adapter data are read
        // fresh, because they are not available anywhere else.
        IReadOnlyList<MonitorViewModel> monitors = _viewModel.Displays.ToList();

        SystemInfoViewModel viewModel = new(_systemInfoService, monitors);
        SystemInfoWindow window = new(viewModel, _themeManager) { Owner = this };

        // Modal on purpose: the information is a snapshot, and the window must not stay open while the
        // user changes the very settings it describes.
        window.ShowDialog();

        // ApplyNativeWindowTheme remembers a single window, so the main window reclaims that ownership
        // to keep a later theme change updating the title bar of the window that is still open.
        _themeManager.ApplyNativeWindowTheme(this);
    }

    /// <summary>
    /// Moves the keyboard focus into the rename editor as soon as it becomes visible, so a double click
    /// on a profile button is enough to start typing.
    /// </summary>
    /// <param name="sender">Text box whose visibility changed.</param>
    /// <param name="e">Change data.</param>
    private void OnProfileNameEditorVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.IsVisible)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    /// <summary>
    /// Commits an in-place profile rename when the editor loses focus, because WPF has no declarative
    /// command binding for <see cref="UIElement.LostFocus"/>.
    /// </summary>
    /// <param name="sender">Text box that lost focus.</param>
    /// <param name="e">Event data.</param>
    private void OnProfileNameEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProfileSlotViewModel slot })
        {
            slot.CommitRenameCommand.Execute(null);
        }
    }
}
