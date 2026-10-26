using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using GameMate.Infrastructure;
using GameMate.Localization;
using GameMate.Models;
using GameMate.Services;

namespace GameMate.ViewModels;

/// <summary>
/// Drives the single window: the display selector, the five colour sliders, the three profile slots
/// and the live, debounced push of the values to the display driver.
/// </summary>
/// <remarks>
/// The window never loads a profile into the driver by itself. Applying colour settings is always an
/// explicit user action, either by dragging a slider or by clicking one of the profile buttons.
/// Startup only reads the stored values into the sliders.
/// </remarks>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Gets the status text shown when nothing needs reporting, in the selected language.
    /// </summary>
    /// <remarks>
    /// A property rather than a constant because the text is localized. It is resolved on every read, so a
    /// language switch is reflected the next time the idle state is compared or assigned.
    /// </remarks>
    private static string ReadyStatus => LanguageService.Current["Status.Ready"];

    private readonly IDisplayService _displayService;
    private readonly IGammaRampDevice _gammaRampDevice;
    private readonly INvidiaColorService _nvidiaColorService;
    private readonly IThemeManager _themeManager;
    private readonly IProfileStore _profileStore;

    private readonly Debouncer _applyDebouncer = new();
    private readonly Debouncer _saveDebouncer = new(TimeSpan.FromMilliseconds(500));

    private ProfileCollection _profiles = ProfileCollection.CreateDefault();
    private MonitorViewModel? _selectedDisplay;
    private string _statusMessage = ReadyStatus;
    private bool _isLoading;
    private bool _isInitialized;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="displayService">Source of the active displays and of the NVAPI availability.</param>
    /// <param name="gammaRampDevice">Carrier for brightness, contrast and gamma.</param>
    /// <param name="nvidiaColorService">Carrier for Digital Vibrance and hue.</param>
    /// <param name="profileStore">Persistence for the three profile slots.</param>
    /// <param name="themeManager">Applies the palette when the user cycles the theme.</param>
    /// <exception cref="ArgumentNullException">Thrown when any dependency is null.</exception>
    public MainViewModel(
        IDisplayService displayService,
        IGammaRampDevice gammaRampDevice,
        INvidiaColorService nvidiaColorService,
        IProfileStore profileStore,
        IThemeManager themeManager)
    {
        _displayService = displayService ?? throw new ArgumentNullException(nameof(displayService));
        _gammaRampDevice = gammaRampDevice ?? throw new ArgumentNullException(nameof(gammaRampDevice));
        _nvidiaColorService = nvidiaColorService ?? throw new ArgumentNullException(nameof(nvidiaColorService));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _themeManager = themeManager ?? throw new ArgumentNullException(nameof(themeManager));

        ApplyProfileCommand = new RelayCommand(ApplyProfile, _ => SelectedDisplay is not null);
        ResetToDefaultsCommand = new RelayCommand(ResetToDefaults, () => SelectedDisplay is not null);
        ShowSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
        ShowSystemInfoCommand = new RelayCommand(() => SystemInfoRequested?.Invoke(this, EventArgs.Empty));

        // Computed strings such as NvidiaSummary are built from the language service rather than bound to its
        // indexer, so they have to be re-raised when the language changes.
        LanguageService.Current.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Gets the displays found at startup, in the order Windows reports them. This list is the
    /// authoritative target set for profile application.
    /// </summary>
    public ObservableCollection<MonitorViewModel> Displays { get; } = [];

    /// <summary>
    /// Gets the command that applies one of the three profile slots. The command parameter is the
    /// slot number as text, "1" to "3".
    /// </summary>
    public RelayCommand ApplyProfileCommand { get; }

    /// <summary>
    /// Gets the command that returns the highlighted display to neutral values and clears its
    /// per-display override.
    /// </summary>
    public RelayCommand ResetToDefaultsCommand { get; }

    /// <summary>
    /// Gets the command that asks for the settings window.
    /// </summary>
    /// <remarks>
    /// The command only raises <see cref="SettingsRequested"/>: creating a window is a view concern, so it is
    /// left to the composition root, which is the only place that knows how to build one.
    /// </remarks>
    public RelayCommand ShowSettingsCommand { get; }

    /// <summary>
    /// Occurs when the user asks for the settings window, which the composition root opens.
    /// </summary>
    public event EventHandler? SettingsRequested;

    /// <summary>
    /// Gets the command that asks for the system information window.
    /// </summary>
    /// <remarks>
    /// The command only raises <see cref="SystemInfoRequested"/>: creating a window is a view concern,
    /// so it is left to the composition root, which is the only place that knows how to build one.
    /// </remarks>
    public RelayCommand ShowSystemInfoCommand { get; }

    /// <summary>
    /// Occurs when the user asks for the system information window, which the composition root opens.
    /// </summary>
    public event EventHandler? SystemInfoRequested;

    /// <summary>
    /// Gets the key combination of the global profile toggle, shown in bold in the window hint.
    /// </summary>
    public string HotkeyCombination => HotkeyPresetInfo.GetDisplayText(_profiles.Hotkey);

    /// <summary>
    /// Occurs when the user picks another global chord, which the composition root applies to the hotkey service.
    /// </summary>
    public event EventHandler? HotkeyChangeRequested;

    /// <summary>
    /// Gets or sets the global chord registered for the profile toggle hotkey.
    /// </summary>
    /// <remarks>
    /// Setting this persists the choice and asks the composition root to re-register, because registering a
    /// system-wide hotkey is a view concern: only the window owns the <c>HwndSource</c> the chord binds to.
    /// </remarks>
    public HotkeyPreset CurrentHotkey
    {
        get => _profiles.Hotkey;
        set
        {
            // An undefined value could only arrive from code rather than the bound ComboBox, but it must never
            // reach RegisterHotKey, where it would map to no virtual key at all.
            if (!Enum.IsDefined(value) || _profiles.Hotkey == value)
            {
                return;
            }

            _profiles.Hotkey = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HotkeyCombination));

            HotkeyChangeRequested?.Invoke(this, EventArgs.Empty);

            ScheduleProfileSave();
        }
    }

    /// <summary>
    /// Puts the stored chord back after the hotkey service refused a change, and reports why.
    /// </summary>
    /// <param name="preset">Chord that is actually registered.</param>
    /// <param name="reason">Explanation produced by the hotkey service.</param>
    /// <remarks>
    /// Deliberately does not raise <see cref="HotkeyChangeRequested"/>: this is the rollback path, so asking
    /// for another registration would loop forever.
    /// </remarks>
    public void RevertHotkey(HotkeyPreset preset, string? reason)
    {
        _profiles.Hotkey = preset;

        OnPropertyChanged(nameof(CurrentHotkey));
        OnPropertyChanged(nameof(HotkeyCombination));

        ReportHotkeyUnavailable(reason);

        ScheduleProfileSave();
    }

    /// <summary>
    /// Gets the name of the first profile slot, shown in bold in the window hint.
    /// </summary>
    public string ProfileOneName => GetSlotName(0);

    /// <summary>
    /// Gets the name of the second profile slot, shown in bold in the window hint.
    /// </summary>
    public string ProfileTwoName => GetSlotName(1);

    /// <summary>
    /// Gets the name of the third profile slot, or an empty string when the hotkey only covers two slots.
    /// </summary>
    /// <remarks>
    /// Empty rather than hidden so the footer hint can keep one fixed sequence of runs; a run with no text
    /// occupies no width and leaves the two-profile sentence byte-identical to before this setting existed.
    /// </remarks>
    public string ProfileThreeName =>
        CurrentHotkeyProfileCount == HotkeyProfileCount.Three ? GetSlotName(2) : string.Empty;

    /// <summary>
    /// Gets the separator between the first and second profile names in the footer hint.
    /// </summary>
    /// <remarks>
    /// A comma when three names are listed, and the localized "and" when only two are, so the two-profile
    /// sentence keeps its original wording.
    /// </remarks>
    public string HotkeyHintSeparator => CurrentHotkeyProfileCount == HotkeyProfileCount.Three
        ? LanguageService.Current["Main.HotkeyHint.Comma"]
        : LanguageService.Current["Main.HotkeyHint.And"];

    /// <summary>
    /// Gets the joining word before the third profile name, or an empty string in two-profile mode.
    /// </summary>
    public string HotkeyHintThirdJoin => CurrentHotkeyProfileCount == HotkeyProfileCount.Three
        ? LanguageService.Current["Main.HotkeyHint.And"]
        : string.Empty;

    /// <summary>
    /// Returns the current name of a profile slot for the hotkey hint.
    /// </summary>
    /// <param name="index">Zero-based slot index.</param>
    /// <returns>The slot name, or its default one-based name before the slots were created.</returns>
    private string GetSlotName(int index)
    {
        // DisplayName is used rather than Name so an untouched slot appears in the selected language in the
        // hint, without the stored name being rewritten.
        if (index < ProfileSlots.Count && ProfileSlots[index].DisplayName is { Length: > 0 } displayed)
        {
            return displayed;
        }

        return LanguageService.Current.Format("Profile.DefaultName", index + 1);
    }

    /// <summary>
    /// Advances the global hotkey through the profile slots selected by <see cref="CurrentHotkeyProfileCount"/>.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="ApplyProfileCommand"/> because that command requires a selected
    /// display, while this is driven by a global hotkey that can fire before the window was ever
    /// focused. Loosening the command's own guard instead would change when the profile buttons are
    /// enabled in the UI.
    /// </remarks>
    public void ToggleProfileOneAndTwo()
    {
        // ActiveProfileIndex already records which slot was applied last, so it doubles as the cycle
        // memory and no extra field has to be added or persisted.
        int slotCount = CurrentHotkeyProfileCount == HotkeyProfileCount.Three ? ColorProfile.SlotCount : 2;

        // Clamping first keeps the cycle deterministic if the persisted index sits outside the active range;
        // without it a stale index of 2 in two-profile mode would wrap to the wrong slot.
        int current = Math.Clamp(_profiles.ActiveProfileIndex, 0, slotCount - 1);
        int next = (current + 1) % slotCount;

        ApplyProfile((next + 1).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Gets or sets how many profile slots the global hotkey steps through.
    /// </summary>
    /// <remarks>
    /// Persisted with the profile file so the choice survives a restart, and read on every hotkey press, so no
    /// extra state has to be kept in step.
    /// </remarks>
    public HotkeyProfileCount CurrentHotkeyProfileCount
    {
        get => _profiles.HotkeyProfileCount;
        set
        {
            if (_profiles.HotkeyProfileCount == value)
            {
                return;
            }

            _profiles.HotkeyProfileCount = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsHotkeyScopeTwo));
            OnPropertyChanged(nameof(IsHotkeyScopeThree));
            OnPropertyChanged(nameof(ProfileThreeName));
            OnPropertyChanged(nameof(HotkeyHintSeparator));
            OnPropertyChanged(nameof(HotkeyHintThirdJoin));

            ScheduleProfileSave();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the hotkey toggles between profile slots 1 and 2.
    /// </summary>
    public bool IsHotkeyScopeTwo
    {
        get => CurrentHotkeyProfileCount == HotkeyProfileCount.Two;
        set => SelectHotkeyProfileCount(value, HotkeyProfileCount.Two);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the hotkey cycles all three profile slots.
    /// </summary>
    public bool IsHotkeyScopeThree
    {
        get => CurrentHotkeyProfileCount == HotkeyProfileCount.Three;
        set => SelectHotkeyProfileCount(value, HotkeyProfileCount.Three);
    }

    /// <summary>
    /// Applies a hotkey scope when its toggle reports that it became the selected one.
    /// </summary>
    /// <param name="selected">Value reported by the toggle.</param>
    /// <param name="scope">Scope the toggle stands for.</param>
    private void SelectHotkeyProfileCount(bool selected, HotkeyProfileCount scope)
    {
        // Only the toggle that became checked applies, so unchecking the previous one does not clear it again.
        if (selected)
        {
            CurrentHotkeyProfileCount = scope;
        }
    }

    /// <summary>
    /// Reports that the global profile toggle hotkey could not be claimed, so the window explains the
    /// state instead of leaving a shortcut that silently does nothing.
    /// </summary>
    /// <param name="reason">
    /// Explanation produced by the hotkey service, or <see langword="null"/> when none was supplied.
    /// </param>
    public void ReportHotkeyUnavailable(string? reason)
    {
        // Called from the composition root before the window is shown, so it deliberately goes through
        // the same status property the rest of the window uses.
        StatusMessage = reason ?? LanguageService.Current["Status.HotkeyUnavailable"];
    }

    /// <summary>
    /// Gets or sets the theme the window is drawn in.
    /// </summary>
    /// <remarks>
    /// Setting this applies the palette immediately and writes the profile file, which is what the settings
    /// window drives. The theme is an appearance preference rather than a colour edit, so nothing here touches
    /// the gamma ramp or Digital Vibrance.
    /// </remarks>
    public AppTheme CurrentTheme
    {
        get => _profiles.Theme;
        set
        {
            if (_profiles.Theme == value)
            {
                return;
            }

            _profiles.Theme = value;

            _themeManager.Apply(value);

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsThemeSystem));
            OnPropertyChanged(nameof(IsThemeLight));
            OnPropertyChanged(nameof(IsThemeDark));

            ScheduleProfileSave();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the theme follows the Windows setting.
    /// </summary>
    public bool IsThemeSystem
    {
        get => CurrentTheme == AppTheme.System;
        set => SelectTheme(value, AppTheme.System);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the light palette is selected.
    /// </summary>
    public bool IsThemeLight
    {
        get => CurrentTheme == AppTheme.Light;
        set => SelectTheme(value, AppTheme.Light);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the dark palette is selected.
    /// </summary>
    public bool IsThemeDark
    {
        get => CurrentTheme == AppTheme.Dark;
        set => SelectTheme(value, AppTheme.Dark);
    }

    /// <summary>
    /// Gets or sets the interface language.
    /// </summary>
    /// <remarks>
    /// Stored and remembered, but deliberately not applied: no translation exists yet, so the interface stays
    /// English whatever is selected.
    /// </remarks>
    public AppLanguage CurrentLanguage
    {
        get => _profiles.Language;
        set
        {
            if (_profiles.Language == value)
            {
                return;
            }

            _profiles.Language = value;

            // Captured before the switch, while the idle comparison still uses the old language's text.
            bool wasIdle = !ShowsStatusMessage;

            // Switching here is what repaints every open window: the service raises PropertyChanged on its
            // indexer, so every {loc:Loc} binding re-reads its text.
            LanguageService.Current.SetLanguage(value);

            if (wasIdle)
            {
                // Re-render the idle text so the status line does not keep the previous language's wording.
                StatusMessage = ReadyStatus;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLanguageEnglish));
            OnPropertyChanged(nameof(IsLanguageRussian));
            OnPropertyChanged(nameof(IsLanguageGerman));
            OnPropertyChanged(nameof(IsLanguageChineseSimplified));

            ScheduleProfileSave();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether English is the selected language.
    /// </summary>
    public bool IsLanguageEnglish
    {
        get => CurrentLanguage == AppLanguage.English;
        set => SelectLanguage(value, AppLanguage.English);
    }

    /// <summary>
    /// Gets or sets a value indicating whether Russian is the selected language.
    /// </summary>
    public bool IsLanguageRussian
    {
        get => CurrentLanguage == AppLanguage.Russian;
        set => SelectLanguage(value, AppLanguage.Russian);
    }

    /// <summary>
    /// Gets or sets a value indicating whether German is the selected language.
    /// </summary>
    public bool IsLanguageGerman
    {
        get => CurrentLanguage == AppLanguage.German;
        set => SelectLanguage(value, AppLanguage.German);
    }

    /// <summary>
    /// Gets or sets a value indicating whether Simplified Chinese is the selected language.
    /// </summary>
    public bool IsLanguageChineseSimplified
    {
        get => CurrentLanguage == AppLanguage.ChineseSimplified;
        set => SelectLanguage(value, AppLanguage.ChineseSimplified);
    }

    /// <summary>
    /// Applies a theme when its toggle reports that it became the selected one.
    /// </summary>
    /// <param name="selected">Value reported by the toggle.</param>
    /// <param name="theme">Theme the toggle stands for.</param>
    private void SelectTheme(bool selected, AppTheme theme)
    {
        // Only the toggle that became checked applies, so unchecking the previous one does not clear the
        // selection a second time.
        if (selected)
        {
            CurrentTheme = theme;
        }
    }

    /// <summary>
    /// Records a language when its toggle reports that it became the selected one.
    /// </summary>
    /// <param name="selected">Value reported by the toggle.</param>
    /// <param name="language">Language the toggle stands for.</param>
    private void SelectLanguage(bool selected, AppLanguage language)
    {
        if (selected)
        {
            CurrentLanguage = language;
        }
    }

    /// <summary>
    /// Gets or sets the display being edited. Only one display is edited at a time, matching the
    /// single-select selector of the NVIDIA Control Panel.
    /// </summary>
    public MonitorViewModel? SelectedDisplay
    {
        get => _selectedDisplay;
        set
        {
            if (ReferenceEquals(_selectedDisplay, value))
            {
                return;
            }

            _selectedDisplay = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(IsVibranceEnabled));

            // The slider commands are only meaningful with a display selected.
            ApplyProfileCommand.RaiseCanExecuteChanged();
            ResetToDefaultsCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Gets a value indicating whether a display is currently highlighted.
    /// </summary>
    public bool HasSelection => SelectedDisplay is not null;

    /// <summary>
    /// Gets a value indicating whether the Digital Vibrance slider applies to the highlighted display.
    /// </summary>
    public bool IsVibranceEnabled => SelectedDisplay?.SupportsVibrance ?? false;

    /// <summary>
    /// Gets the message shown in the status area: the outcome of the last action, or an explanation of
    /// why part of the interface is unavailable.
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                // Visibility of the whole status block follows this text.
                OnPropertyChanged(nameof(ShowsStatusMessage));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the status line carries something worth showing.
    /// </summary>
    /// <remarks>
    /// The window hides the line while it only holds the idle text, so the resting state stays clean
    /// without losing apply results or driver errors.
    /// </remarks>
    public bool ShowsStatusMessage => !string.Equals(_statusMessage, ReadyStatus, StringComparison.Ordinal);

    /// <summary>
    /// Gets the name of the profile that is currently in use.
    /// </summary>
    public string ActiveProfileSummary => LanguageService.Current.Format("Status.ActiveProfile", ActiveProfile.Name);

    /// <summary>
    /// Gets a summary of the NVIDIA driver state, used to explain why the NVIDIA specific controls may
    /// be disabled.
    /// </summary>
    public string NvidiaSummary
    {
        get
        {
            NvidiaAvailability availability = _displayService.NvidiaAvailability;

            if (!availability.IsAvailable)
            {
                return availability.Reason ?? LanguageService.Current["Nvidia.Summary.Unavailable"];
            }

            return availability.DriverVersion is { Length: > 0 } version
                ? LanguageService.Current.Format("Nvidia.Summary.WithVersion", version)
                : LanguageService.Current["Nvidia.Summary.WithoutVersion"];
        }
    }

    /// <summary>
    /// Gets the three profile slots shown as buttons in the window.
    /// </summary>
    public ObservableCollection<ProfileSlotViewModel> ProfileSlots { get; } = [];

    /// <summary>
    /// Gets the index of the profile slot currently in use.
    /// </summary>
    public int ActiveProfileIndex
    {
        get => _profiles.ActiveProfileIndex;
        private set
        {
            if (_profiles.ActiveProfileIndex == value)
            {
                return;
            }

            _profiles.ActiveProfileIndex = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveProfileSummary));

            // The highlight on the profile buttons follows the slot that is in use.
            UpdateActiveProfileSlot();
        }
    }

    /// <summary>
    /// Gets the profile slot currently in use.
    /// </summary>
    private ColorProfile ActiveProfile =>
        _profiles.Profiles[Math.Clamp(_profiles.ActiveProfileIndex, 0, _profiles.Profiles.Count - 1)];

    /// <summary>
    /// Loads the stored profiles and enumerates the displays. Call once, after the window is loaded,
    /// so a slow or missing driver cannot delay or break startup.
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        _profiles = _profileStore.Load();

        // Keep the service in step with the file that was actually loaded, which matters whenever the profile
        // is reloaded rather than read once during startup.
        LanguageService.Current.SetLanguage(_profiles.Language);

        // App.xaml.cs already applied the stored theme before this window existed. Re-applying it here
        // keeps this view model the single owner of the theme even though the file is read twice.
        _themeManager.Apply(_profiles.Theme);

        IReadOnlyList<DisplayInfo> displays = _displayService.GetDisplays();

        // Suppress the change notifications raised while the sliders are populated: they are not user
        // edits and must not be written to the driver.
        _isLoading = true;

        try
        {
            foreach (MonitorViewModel existing in Displays)
            {
                existing.PropertyChanged -= OnMonitorPropertyChanged;
            }

            Displays.Clear();

            ColorProfile active = ActiveProfile;

            foreach (DisplayInfo display in displays)
            {
                MonitorViewModel monitor = new(display);

                InitialiseMonitor(monitor);

                monitor.PropertyChanged += OnMonitorPropertyChanged;

                // The window opens on the values of the active profile, but nothing is pushed to the
                // driver until the user acts.
                monitor.LoadSettings(ResolveStartupSettings(active, monitor));

                Displays.Add(monitor);
            }

            SynchroniseProfileSlots();

            ActiveProfileIndex = _profiles.ActiveProfileIndex;

            SelectedDisplay = Displays.FirstOrDefault(monitor => monitor.IsPrimary) ?? Displays.FirstOrDefault();
        }
        finally
        {
            _isLoading = false;
        }

        OnPropertyChanged(nameof(NvidiaSummary));
        UpdateInitialStatus();
    }

    /// <summary>
    /// Flushes pending work and detaches from the displays.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (MonitorViewModel monitor in Displays)
        {
            monitor.PropertyChanged -= OnMonitorPropertyChanged;
        }

        foreach (ProfileSlotViewModel slot in ProfileSlots)
        {
            slot.PropertyChanged -= OnProfileSlotPropertyChanged;
        }

        // The language service is process-wide and outlives this view model, so the subscription has to be
        // released explicitly.
        LanguageService.Current.PropertyChanged -= OnLanguageChanged;

        _applyDebouncer.Dispose();
        _saveDebouncer.Dispose();

        // Write immediately so the last edits are not lost with the pending debounce.
        SaveProfiles();
    }

    /// <summary>
    /// Reads the current capability and driver values of one display.
    /// </summary>
    /// <param name="monitor">Display to initialise.</param>
    private void InitialiseMonitor(MonitorViewModel monitor)
    {
        // Brightness, contrast and gamma start neutral: a gamma ramp is a 256 entry curve and those
        // three settings cannot be recovered from it, so the stored profile is what supplies them.
        monitor.LoadSettings(ColorSettings.Defaults());

        // Reading the ramp back is the cheapest way to find out whether the display accepts ramps at
        // all; a refusal is reported in the status area rather than disabling the sliders, because
        // writing can still succeed on some drivers.
        if (_gammaRampDevice.TryReadCurrent(monitor.Display, out ushort[]? ramp, out _) && ramp is not null)
        {
            monitor.MarkGammaRampAvailable();
        }

        DisplayInfo display = monitor.Display;

        if (!display.SupportsNvidiaColor)
        {
            return;
        }

        if (_nvidiaColorService.TryReadDigitalVibrance(display, out ColorLevelRange? vibrance, out _)
            && vibrance is not null)
        {
            monitor.MarkVibranceAvailable(
                vibrance.Minimum, vibrance.Maximum, vibrance.Default, vibrance.Current);
        }

        if (_nvidiaColorService.TryReadHue(display, out ColorLevelRange? hue, out _) && hue is not null)
        {
            monitor.MarkNvidiaHueAvailable(hue.Current);
        }
    }

    /// <summary>
    /// Chooses the values the window opens with for one display.
    /// </summary>
    /// <param name="profile">Profile that is active at startup.</param>
    /// <param name="monitor">Display being populated.</param>
    /// <returns>
    /// The stored override, the profile's global values when the display is covered by the profile, or
    /// the values just read from the driver when the display has never been configured.
    /// </returns>
    private static ColorSettings ResolveStartupSettings(ColorProfile profile, MonitorViewModel monitor)
    {
        if (profile.MonitorOverrides.TryGetValue(monitor.DeviceKey, out ColorSettings? stored))
        {
            return stored;
        }

        return profile.Devices.Contains(monitor.DeviceKey) ? profile.Global : monitor.Settings;
    }

    /// <summary>
    /// Applies one of the three profile slots.
    /// </summary>
    /// <param name="parameter">Slot number as text, "1" to "3".</param>
    private void ApplyProfile(object? parameter)
    {
        if (!int.TryParse(
                parameter?.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int number))
        {
            return;
        }

        int index = number - 1;

        if (index < 0 || index >= _profiles.Profiles.Count)
        {
            return;
        }

        ColorProfile profile = _profiles.Profiles[index];

        _isLoading = true;

        try
        {
            ActiveProfileIndex = index;

            // Every display the slot covers is restored, so switching profiles brings back the same set
            // of monitors rather than only the one highlighted in the selector.
            foreach (string deviceKey in profile.Devices)
            {
                FindMonitor(deviceKey)?.LoadSettings(profile.Resolve(deviceKey));
            }

            // The highlighted display keeps its selection: applying a profile must not move the user's
            // focus to another monitor. It only has to show this profile's values.
            MonitorViewModel? highlighted = SelectedDisplay;

            if (highlighted is not null)
            {
                highlighted.LoadSettings(profile.Resolve(highlighted.DeviceKey));
            }
        }
        finally
        {
            _isLoading = false;
        }

        int appliedCount = 0;

        foreach (string deviceKey in profile.Devices.ToArray())
        {
            MonitorViewModel? monitor = FindMonitor(deviceKey);

            if (monitor is null)
            {
                continue;
            }

            ApplySettings(monitor, monitor.Settings);
            appliedCount++;
        }

        // A slot that has never been configured still has to do something useful, so its global values
        // are applied to the highlighted display.
        if (appliedCount == 0 && SelectedDisplay is not null)
        {
            SelectedDisplay.LoadSettings(profile.Global);
            ApplySettings(SelectedDisplay, SelectedDisplay.Settings);
            appliedCount = 1;
        }

        // The hotkey path can reach this method while nothing is selected. Rather than doing nothing,
        // the slot's global values are applied to every enumerated display, which is what makes the
        // shortcut useful from another application. The UI buttons are unaffected because they can only
        // be clicked with a selection present.
        if (appliedCount == 0)
        {
            foreach (MonitorViewModel monitor in Displays)
            {
                ApplySettings(monitor, profile.Global);
                appliedCount++;
            }
        }

        ScheduleProfileSave();

        // A successful apply deliberately leaves the status line untouched: the sliders and the active
        // profile highlight already show the outcome, so the resting state stays clean. Only a real
        // failure is worth reporting.
        if (appliedCount == 0)
        {
            StatusMessage = LanguageService.Current.Format("Status.ProfileCouldNotApply", profile.Name);
        }
    }

    /// <summary>
    /// Returns the highlighted display to neutral values and clears its per-display override.
    /// </summary>
    private void ResetToDefaults()
    {
        MonitorViewModel? monitor = SelectedDisplay;

        if (monitor is null)
        {
            return;
        }

        ColorSettings neutral = CreateNeutralSettings(monitor);

        _isLoading = true;

        try
        {
            monitor.LoadSettings(neutral);

            // Removing the override and the coverage entry lets the display follow the global values
            // again, which is what "cleared" means for the hybrid profile model.
            ColorProfile profile = ActiveProfile;

            profile.MonitorOverrides.Remove(monitor.DeviceKey);
            profile.Devices.Remove(monitor.DeviceKey);
            profile.Global = neutral with { };
        }
        finally
        {
            _isLoading = false;
        }

        ApplySettings(monitor, monitor.Settings);

        ScheduleProfileSave();
        StatusMessage = LanguageService.Current.Format("Status.ResetToDefaults", monitor.DisplayLabel);
    }

    /// <summary>
    /// Builds the neutral values a reset produces.
    /// </summary>
    /// <param name="monitor">Display being reset.</param>
    /// <returns>The neutral settings, using the driver's own neutral Digital Vibrance level when known.</returns>
    private static ColorSettings CreateNeutralSettings(MonitorViewModel monitor)
    {
        ColorSettings neutral = ColorSettings.Defaults();

        return monitor.VibranceDefault is { } driverDefault
            ? neutral with { Vibrance = driverDefault }
            : neutral;
    }

    /// <summary>
    /// Pushes one display's values to the driver.
    /// </summary>
    /// <param name="monitor">Display to change.</param>
    /// <param name="settings">Values to apply.</param>
    private void ApplySettings(MonitorViewModel monitor, ColorSettings settings)
    {
        List<string> failures = [];

        // Hue is applied through NVAPI first because that is a real colour rotation; the gamma ramp can
        // only approximate it with per-channel gains.
        int rampHue = settings.Hue;

        if (monitor.UsesNvidiaHue)
        {
            if (_nvidiaColorService.TrySetHue(monitor.Display, settings.Hue, out string? hueFailure))
            {
                // NVAPI owns the hue now, so the ramp must not apply it a second time.
                rampHue = 0;
            }
            else
            {
                // Fall back to the ramp for the rest of the session so the slider keeps working.
                monitor.MarkNvidiaHueUnavailable();

                if (hueFailure is not null)
                {
                    failures.Add(hueFailure);
                }
            }
        }

        ushort[] ramp = GammaRampBuilder.Build(
            settings.Brightness, settings.Contrast, settings.Gamma, rampHue);

        if (!_gammaRampDevice.TryApply(monitor.Display, ramp, out string? rampFailure) && rampFailure is not null)
        {
            failures.Add(rampFailure);
        }

        if (monitor.SupportsVibrance
            && !_nvidiaColorService.TrySetDigitalVibrance(monitor.Display, settings.Vibrance, out string? vibranceFailure)
            && vibranceFailure is not null)
        {
            failures.Add(vibranceFailure);
        }

        StatusMessage = failures.Count == 0
            ? LanguageService.Current.Format("Status.Applied", monitor.DisplayLabel)
            : string.Join(" ", failures);
    }

    /// <summary>
    /// Schedules an apply for one display.
    /// </summary>
    /// <param name="monitor">Display being edited.</param>
    private void ScheduleApply(MonitorViewModel monitor)
    {
        // Coalesced so a slider drag does not push a ramp to the driver for every pixel; the lambda
        // reads the settings when it finally runs, so the newest values are what get sent.
        _applyDebouncer.Invoke(() => ApplySettings(monitor, monitor.Settings));
    }

    /// <summary>
    /// Schedules a write of the profile file, so dragging a slider does not touch the disk per tick.
    /// </summary>
    private void ScheduleProfileSave()
    {
        _saveDebouncer.Invoke(SaveProfiles);
    }

    /// <summary>
    /// Writes the profile file and reports a failure in the status area.
    /// </summary>
    private void SaveProfiles()
    {
        if (!_profileStore.TrySave(_profiles, out string? failureReason))
        {
            StatusMessage = failureReason ?? "The profiles could not be saved.";
        }
    }

    /// <summary>
    /// Finds a display by its persistence key.
    /// </summary>
    /// <param name="deviceKey">Key to look for.</param>
    /// <returns>The matching display, or <see langword="null"/> when it is no longer connected.</returns>
    private MonitorViewModel? FindMonitor(string deviceKey)
    {
        foreach (MonitorViewModel monitor in Displays)
        {
            if (string.Equals(monitor.DeviceKey, deviceKey, StringComparison.Ordinal))
            {
                return monitor;
            }
        }

        return null;
    }

    /// <summary>
    /// Records a user edit into the active profile and pushes it to the display.
    /// </summary>
    /// <param name="sender">Display that changed.</param>
    /// <param name="e">Description of the change.</param>
    private void OnMonitorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoading || _disposed || sender is not MonitorViewModel monitor)
        {
            return;
        }

        if (!ReferenceEquals(monitor, SelectedDisplay))
        {
            return;
        }

        ColorSettings settings = monitor.Settings;
        ColorProfile profile = ActiveProfile;

        // The active slot follows the edits: the edited display stores an override and the global values
        // track the most recent edit. This is what makes the slots remember every setting.
        profile.Global = settings.Copy();
        profile.SetMonitorSettings(monitor.DeviceKey, settings);

        ScheduleApply(monitor);
        ScheduleProfileSave();
    }

    /// <summary>
    /// Creates the profile slot view models and copies the stored names and active state into them.
    /// </summary>
    private void SynchroniseProfileSlots()
    {
        if (ProfileSlots.Count == 0)
        {
            for (int index = 0; index < _profiles.Profiles.Count; index++)
            {
                ProfileSlotViewModel slot = new(index + 1)
                {
                    // Assigning before subscribing to PropertyChanged keeps the initial load from
                    // looking like a user rename, so it cannot schedule a save.
                    Name = _profiles.Profiles[index].Name,
                };

                slot.PropertyChanged += OnProfileSlotPropertyChanged;

                ProfileSlots.Add(slot);
            }
        }

        UpdateActiveProfileSlot();

        // The hint names the first two slots, so both names have to be refreshed once they exist.
        OnPropertyChanged(nameof(ProfileOneName));
        OnPropertyChanged(nameof(ProfileTwoName));
    }

    /// <summary>
    /// Marks the slot that is currently in use so the window can highlight its button.
    /// </summary>
    private void UpdateActiveProfileSlot()
    {
        for (int index = 0; index < ProfileSlots.Count; index++)
        {
            ProfileSlots[index].IsActive = index == _profiles.ActiveProfileIndex;
        }
    }

    /// <summary>
    /// Writes a renamed slot into the stored profile and schedules a save.
    /// </summary>
    /// <param name="sender">Slot that changed.</param>
    /// <param name="e">Description of the change.</param>
    private void OnProfileSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ProfileSlotViewModel slot
            || e.PropertyName != nameof(ProfileSlotViewModel.Name))
        {
            return;
        }

        int index = slot.Number - 1;

        if (index < 0 || index >= _profiles.Profiles.Count)
        {
            return;
        }

        _profiles.Profiles[index].Name = slot.Name;

        // The status area shows the active profile's name and the hotkey hint names the first two slots,
        // so both have to be refreshed as well.
        OnPropertyChanged(nameof(ActiveProfileSummary));
        OnPropertyChanged(nameof(ProfileOneName));
        OnPropertyChanged(nameof(ProfileTwoName));

        if (_isLoading || _disposed)
        {
            return;
        }

        ScheduleProfileSave();
    }

    /// <summary>
    /// Sets the status text reported during startup.
    /// </summary>
    private void UpdateInitialStatus()
    {
        NvidiaAvailability availability = _displayService.NvidiaAvailability;

        if (!availability.IsAvailable)
        {
            StatusMessage = availability.Reason ?? LanguageService.Current["Nvidia.Summary.Unavailable"];
            return;
        }

        if (Displays.Count == 0)
        {
            StatusMessage = LanguageService.Current["Status.NoDisplay"];
            return;
        }

        MonitorViewModel? unreadable = Displays.FirstOrDefault(monitor => !monitor.SupportsGammaRamp);

        StatusMessage = unreadable is null
            ? ReadyStatus
            : LanguageService.Current.Format("Status.DriverNoRamp", unreadable.FriendlyName);
    }

    /// <summary>
    /// Re-raises the computed strings when the interface language changes.
    /// </summary>
    /// <param name="sender">Language service that raised the change.</param>
    /// <param name="e">Description of the change.</param>
    /// <remarks>
    /// These properties build their text from the language service instead of being bound to its indexer, so
    /// without this they would keep the previous language until something else happened to refresh them.
    /// </remarks>
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(NvidiaSummary));
        OnPropertyChanged(nameof(ActiveProfileSummary));
        OnPropertyChanged(nameof(ProfileOneName));
        OnPropertyChanged(nameof(ProfileTwoName));

        // An untouched slot renders its automatic name in the selected language, so each button re-reads it.
        foreach (ProfileSlotViewModel slot in ProfileSlots)
        {
            slot.NotifyLanguageChanged();
        }

        // The selector shows a localized label, so each display re-reads it as well.
        foreach (MonitorViewModel monitor in Displays)
        {
            monitor.NotifyLanguageChanged();
        }
    }
}
