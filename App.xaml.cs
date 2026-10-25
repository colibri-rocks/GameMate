using System.Windows;
using GameMate.Localization;
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
        ProfileCollection saved = new ProfileStore().Load();

        ThemeManager.Instance.Apply(saved.Theme);

        // The language is installed before the first window is constructed for the same reason as the palette:
        // a binding created before the dictionary is loaded would resolve to its key name and only correct
        // itself once the window was already visible.
        LanguageService.Current.SetLanguage(saved.Language);

        // base.OnStartup is what instantiates StartupUri, so it has to run last.
        base.OnStartup(e);
    }
}
