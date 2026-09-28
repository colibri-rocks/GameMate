using System.Windows;
using GameMate.Models;
using GameMate.Services;

namespace GameMate;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Applies the persisted theme before the startup window is created, so the window is never drawn
    /// in the wrong palette even for a single frame.
    /// </summary>
    /// <param name="e">Startup arguments supplied by the framework.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        // This reads the profile file a second time later on, when MainViewModel initialises. That
        // duplication is deliberate: one cheap JSON read here buys a flash-free window, and it keeps
        // MainViewModel free of any startup-ordering responsibility.
        AppTheme savedTheme = new ProfileStore().Load().Theme;

        ThemeManager.Instance.Apply(savedTheme);

        // base.OnStartup is what instantiates StartupUri, so it has to run last.
        base.OnStartup(e);
    }
}
