using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using GameMate.Infrastructure;
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
    /// Status text shown when nothing needs reporting.
    /// </summary>
    private const string ReadyStatus = "Ready.";

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
        CycleThemeCommand = new RelayCommand(CycleTheme);
        ShowSystemInfoCommand = new RelayCommand(() => SystemInfoRequested?.Invoke(this, EventArgs.Empty));
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
    /// Gets the command that advances the theme through Follow Windows, Light and Dark.
    /// </summary>
    public RelayCommand CycleThemeCommand { get; }

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
    public string HotkeyCombination => "Ctrl+Alt+Shift+P";

    /// <summary>
    /// Gets the name of the first profile slot, shown in bold in the window hint.
    /// </summary>
    public string ProfileOneName => GetSlotName(0);

    /// <summary>
    /// Gets the name of the second profile slot, shown in bold in the window hint.
    /// </summary>
    public string ProfileTwoName => GetSlotName(1);

    /// <summary>
    /// Returns the current name of a profile slot for the hotkey hint.
    /// </summary>
    /// <param name="index">Zero-based slot index.</param>
    /// <returns>The slot name, or its default one-based name before the slots were created.</returns>
    private string GetSlotName(int index)
    {
        return index < ProfileSlots.Count && ProfileSlots[index].Name is { Length: > 0 } name
            ? name
            : $"Profile {index + 1}";
    }

    /// <summary>
    /// Toggles between profile slot 1 and slot 2, applying the newly selected slot.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="ApplyProfileCommand"/> because that command requires a selected
    /// display, while this is driven by a global hotkey that can fire before the window was ever
    /// focused. Loosening the command's own guard instead would change when the profile buttons are
    /// enabled in the UI.
    /// </remarks>
    public void ToggleProfileOneAndTwo()
    {
        // ActiveProfileIndex already records which slot was applied last, so it doubles as the toggle
        // memory and no extra field has to be added or persisted.
        int target = _profiles.ActiveProfileIndex == 0 ? 1 : 0;

        ApplyProfile((target + 1).ToString(CultureInfo.InvariantCulture));
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
        StatusMessage = reason ?? "The profile toggle hotkey (Ctrl+Alt+Shift+P) is unavailable.";
    }

    /// <summary>
    /// Gets the Segoe MDL2 Assets glyph for the theme the user selected.
    /// </summary>
    /// <remarks>
    /// The glyph shows the selected mode rather than the palette that is currently resolved, so choosing
    /// Follow Windows keeps showing the gear icon even while Windows itself is dark.
    /// </remarks>
    public string ThemeGlyph => _profiles.Theme switch
    {
        AppTheme.Light => "\uE706",
        AppTheme.Dark => "\uE708",
        _ => "\uE713",
    };

    /// <summary>
    /// Gets the tooltip for the theme button, naming the selected theme and the one a click moves to.
    /// </summary>
    public string ThemeTooltip =>
        $"Theme: {DescribeTheme(_profiles.Theme)}. Click to switch to {DescribeTheme(NextTheme(_profiles.Theme))}.";

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
    public string ActiveProfileSummary => $"Active profile: {ActiveProfile.Name}";

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
                return availability.Reason ?? "The NVIDIA specific controls are unavailable.";
            }

            return availability.DriverVersion is { Length: > 0 } version
                ? $"NVIDIA driver detected (version {version}). Digital Vibrance and hue are controlled through NVAPI."
                : "NVIDIA driver detected. Digital Vibrance and hue are controlled through NVAPI.";
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
            StatusMessage = $"{profile.Name} could not be applied because no display is available.";
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
        StatusMessage = $"{monitor.DisplayLabel}: reset to defaults.";
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
            ? $"{monitor.DisplayLabel}: applied."
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
    /// Advances the theme through Follow Windows, Light and Dark, applies it and persists the choice.
    /// </summary>
    private void CycleTheme()
    {
        _profiles.Theme = NextTheme(_profiles.Theme);

        _themeManager.Apply(_profiles.Theme);

        OnPropertyChanged(nameof(ThemeGlyph));
        OnPropertyChanged(nameof(ThemeTooltip));

        // The theme is an appearance preference, not a colour edit, so it only writes the profile file.
        // Nothing here touches the gamma ramp or Digital Vibrance.
        ScheduleProfileSave();
    }

    /// <summary>
    /// Returns the theme a click moves to, in the fixed order Follow Windows, Light, Dark.
    /// </summary>
    /// <param name="theme">Theme currently selected.</param>
    /// <returns>The next theme in the cycle.</returns>
    private static AppTheme NextTheme(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.System => AppTheme.Light,
            AppTheme.Light => AppTheme.Dark,
            _ => AppTheme.System,
        };
    }

    /// <summary>
    /// Produces the name of a theme for the tooltip.
    /// </summary>
    /// <param name="theme">Theme to describe.</param>
    /// <returns>A human readable theme name.</returns>
    private static string DescribeTheme(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Light => "Light",
            AppTheme.Dark => "Dark",
            _ => "Follow Windows",
        };
    }

    /// <summary>
    /// Sets the status text reported during startup.
    /// </summary>
    private void UpdateInitialStatus()
    {
        NvidiaAvailability availability = _displayService.NvidiaAvailability;

        if (!availability.IsAvailable)
        {
            StatusMessage = availability.Reason ?? "The NVIDIA specific controls are unavailable.";
            return;
        }

        if (Displays.Count == 0)
        {
            StatusMessage = "No active display was found, so there is nothing to configure.";
            return;
        }

        MonitorViewModel? unreadable = Displays.FirstOrDefault(monitor => !monitor.SupportsGammaRamp);

        StatusMessage = unreadable is null
            ? ReadyStatus
            : $"The driver did not report a gamma ramp for \"{unreadable.FriendlyName}\"; changes to it may not take effect.";
    }
}
