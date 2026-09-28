using GameMate.Infrastructure;
using GameMate.Models;

namespace GameMate.ViewModels;

/// <summary>
/// View model for one display: its identity, its capabilities and the five colour values the user
/// edits while the display is highlighted in the selector.
/// </summary>
/// <remarks>
/// The type is a pure data holder. Applying values to the driver is the responsibility of
/// <see cref="MainViewModel"/>, which reacts to <see cref="System.ComponentModel.INotifyPropertyChanged"/> notifications
/// from the highlighted display.
/// </remarks>
public sealed class MonitorViewModel : ObservableObject
{
    private ColorSettings _settings = ColorSettings.Defaults();
    private bool _supportsVibrance;
    private bool _usesNvidiaHue;
    private bool _supportsGammaRamp;
    private int _vibranceMinimum;
    private int _vibranceMaximum = ColorProfileMaxVibrance;
    private int? _vibranceDefault;

    /// <summary>
    /// Upper bound used for the Digital Vibrance slider until the driver reports its own range.
    /// </summary>
    private const int ColorProfileMaxVibrance = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonitorViewModel"/> class.
    /// </summary>
    /// <param name="display">Display description this view model presents.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="display"/> is null.</exception>
    public MonitorViewModel(DisplayInfo display)
    {
        Display = display ?? throw new ArgumentNullException(nameof(display));
    }

    /// <summary>
    /// Gets the display description, including the NVAPI identity used for the colour calls.
    /// </summary>
    public DisplayInfo Display { get; }

    /// <summary>
    /// Gets the stable key under which this display's settings are persisted.
    /// </summary>
    public string DeviceKey => Display.DeviceKey;

    /// <summary>
    /// Gets the monitor name without any prefix.
    /// </summary>
    public string FriendlyName => Display.FriendlyName;

    /// <summary>
    /// Gets the label shown in the selector, formatted the way the NVIDIA Control Panel formats it.
    /// </summary>
    public string DisplayLabel => $"{Display.Ordinal}. {Display.FriendlyName} (Display {Display.Ordinal})";

    /// <summary>
    /// Gets a value indicating whether this is the primary desktop display.
    /// </summary>
    public bool IsPrimary => Display.IsPrimary;

    /// <summary>
    /// Gets the lowest Digital Vibrance level the driver accepts.
    /// </summary>
    public int VibranceMinimum
    {
        get => _vibranceMinimum;
        private set => SetProperty(ref _vibranceMinimum, value);
    }

    /// <summary>
    /// Gets the highest Digital Vibrance level the driver accepts.
    /// </summary>
    public int VibranceMaximum
    {
        get => _vibranceMaximum;
        private set => SetProperty(ref _vibranceMaximum, value);
    }

    /// <summary>
    /// Gets the level the driver treats as neutral, or <see langword="null"/> when it reported none.
    /// </summary>
    public int? VibranceDefault
    {
        get => _vibranceDefault;
        private set => SetProperty(ref _vibranceDefault, value);
    }

    /// <summary>
    /// Gets a value indicating whether the Digital Vibrance slider applies to this display.
    /// </summary>
    public bool SupportsVibrance
    {
        get => _supportsVibrance;
        private set => SetProperty(ref _supportsVibrance, value);
    }

    /// <summary>
    /// Gets a value indicating whether hue is applied by NVAPI instead of inside the gamma ramp.
    /// </summary>
    public bool UsesNvidiaHue
    {
        get => _usesNvidiaHue;
        private set => SetProperty(ref _usesNvidiaHue, value);
    }

    /// <summary>
    /// Gets a value indicating whether the driver reported a gamma ramp for this display.
    /// </summary>
    public bool SupportsGammaRamp
    {
        get => _supportsGammaRamp;
        private set => SetProperty(ref _supportsGammaRamp, value);
    }

    /// <summary>
    /// Gets or sets the brightness offset.
    /// </summary>
    public int Brightness
    {
        get => _settings.Brightness;
        set
        {
            if (_settings.Brightness == value)
            {
                return;
            }

            _settings = _settings with { Brightness = value };
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the contrast value.
    /// </summary>
    public int Contrast
    {
        get => _settings.Contrast;
        set
        {
            if (_settings.Contrast == value)
            {
                return;
            }

            _settings = _settings with { Contrast = value };
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the display gamma.
    /// </summary>
    public double Gamma
    {
        get => _settings.Gamma;
        set
        {
            if (_settings.Gamma.Equals(value))
            {
                return;
            }

            _settings = _settings with { Gamma = value };
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the Digital Vibrance level.
    /// </summary>
    public int Vibrance
    {
        get => _settings.Vibrance;
        set
        {
            if (_settings.Vibrance == value)
            {
                return;
            }

            _settings = _settings with { Vibrance = value };
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the hue angle in degrees.
    /// </summary>
    public int Hue
    {
        get => _settings.Hue;
        set
        {
            if (_settings.Hue == value)
            {
                return;
            }

            _settings = _settings with { Hue = value };
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets an independent snapshot of the five values.
    /// </summary>
    public ColorSettings Settings => _settings with { };

    /// <summary>
    /// Replaces all five values at once, which is how a profile is loaded into the window.
    /// </summary>
    /// <param name="settings">Values to show.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
    public void LoadSettings(ColorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings with { };

        // Every property is announced, even when its value did not change, so the sliders stay in sync
        // when a profile is applied to several displays in a row.
        OnPropertyChanged(nameof(Brightness));
        OnPropertyChanged(nameof(Contrast));
        OnPropertyChanged(nameof(Gamma));
        OnPropertyChanged(nameof(Vibrance));
        OnPropertyChanged(nameof(Hue));
    }

    /// <summary>
    /// Records that the driver reported a gamma ramp, so brightness, contrast and gamma are usable.
    /// </summary>
    public void MarkGammaRampAvailable()
    {
        SupportsGammaRamp = true;
    }

    /// <summary>
    /// Records the Digital Vibrance range reported by the driver and adopts its current level.
    /// </summary>
    /// <param name="minimum">Lowest accepted level.</param>
    /// <param name="maximum">Highest accepted level.</param>
    /// <param name="defaultValue">Level the driver treats as neutral.</param>
    /// <param name="currentValue">Level the control currently holds.</param>
    public void MarkVibranceAvailable(int minimum, int maximum, int defaultValue, int currentValue)
    {
        VibranceMinimum = minimum;
        VibranceMaximum = maximum;
        VibranceDefault = defaultValue;
        SupportsVibrance = true;

        // The driver's current level wins: another application may already have changed it.
        Vibrance = currentValue;
    }

    /// <summary>
    /// Records that hue will be applied through NVAPI, adopting the angle the driver reports.
    /// </summary>
    /// <param name="currentAngle">Angle the control currently holds.</param>
    public void MarkNvidiaHueAvailable(int currentAngle)
    {
        Hue = currentAngle;
        UsesNvidiaHue = true;
    }

    /// <summary>
    /// Switches this display to the gamma-ramp hue fallback after NVAPI refused the call.
    /// </summary>
    public void MarkNvidiaHueUnavailable()
    {
        UsesNvidiaHue = false;
    }
}
