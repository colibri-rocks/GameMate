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
    /// Initializes a new instance of the <see cref="MainWindow"/> class and wires the concrete
    /// services, so the window owns the only composition root of the application.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // Assigned before the view model so the same manager instance can be injected into it.
        _themeManager = ThemeManager.Instance;

        _viewModel = new MainViewModel(
            new DisplayService(),
            new GammaRampDevice(),
            new NvidiaColorService(),
            new ProfileStore(),
            _themeManager);

        DataContext = _viewModel;

        // The window has no handle before SourceInitialized, which is also the earliest point at which
        // the native title bar can be switched to dark.
        SourceInitialized += OnWindowSourceInitialized;

        // Re-applies the native chrome whenever the user cycles to another theme.
        _themeManager.ThemeChanged += OnThemeChanged;

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

        _viewModel.Dispose();
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
    /// Re-applies the native title bar colour after the user changed the theme.
    /// </summary>
    /// <param name="sender">Theme manager that raised the event.</param>
    /// <param name="e">Event data.</param>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
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
