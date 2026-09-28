namespace GameMate.Models;

/// <summary>
/// One of the three profile slots: the values shared by every display it covers, the per-display
/// overrides, and the displays the slot applies to.
/// </summary>
/// <remarks>
/// This is the hybrid model chosen for the feature. <see cref="Global"/> holds the most recent
/// values and applies to every covered display that has no entry in
/// <see cref="MonitorOverrides"/>, so one monitor can deviate from the shared values without
/// disturbing the others.
/// </remarks>
public sealed record ColorProfile
{
    /// <summary>
    /// Number of profile slots the application offers.
    /// </summary>
    public const int SlotCount = 3;

    /// <summary>
    /// Gets or sets the display name of the slot, for example "Profile 1".
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the values applied to a covered display that has no override.
    /// </summary>
    public ColorSettings Global { get; set; } = ColorSettings.Defaults();

    /// <summary>
    /// Gets or sets the per-display overrides, keyed by <see cref="DisplayInfo.DeviceKey"/>.
    /// </summary>
    public Dictionary<string, ColorSettings> MonitorOverrides { get; set; } = [];

    /// <summary>
    /// Gets or sets the keys of the displays this slot has been configured for. This is the set the
    /// slot applies to, so switching profiles restores the same monitors.
    /// </summary>
    public List<string> Devices { get; set; } = [];

    /// <summary>
    /// Creates an empty slot.
    /// </summary>
    /// <param name="number">One-based slot number.</param>
    /// <returns>A new empty profile.</returns>
    public static ColorProfile Create(int number)
    {
        return new ColorProfile
        {
            Name = $"Profile {number}",
        };
    }

    /// <summary>
    /// Resolves the settings that apply to a display.
    /// </summary>
    /// <param name="deviceKey">Key of the display.</param>
    /// <returns>The display's override when it has one; otherwise the global values.</returns>
    public ColorSettings Resolve(string deviceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);

        return MonitorOverrides.TryGetValue(deviceKey, out ColorSettings? settings) ? settings : Global;
    }

    /// <summary>
    /// Stores the settings of one display and records the display as covered by this slot.
    /// </summary>
    /// <param name="deviceKey">Key of the display.</param>
    /// <param name="settings">Values to remember for that display.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="deviceKey"/> is empty.</exception>
    public void SetMonitorSettings(string deviceKey, ColorSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        ArgumentNullException.ThrowIfNull(settings);

        MonitorOverrides[deviceKey] = settings.Copy();

        if (!Devices.Contains(deviceKey))
        {
            Devices.Add(deviceKey);
        }
    }
}
