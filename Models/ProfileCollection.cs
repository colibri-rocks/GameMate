namespace GameMate.Models;

/// <summary>
/// Root object persisted as <c>profiles.json</c>.
/// </summary>
public sealed record ProfileCollection
{
    /// <summary>
    /// Version written by this build. A file carrying any other version is treated as unreadable so
    /// that a future layout change cannot silently corrupt the user's settings.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Gets or sets the schema version of the file.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// Gets or sets the index of the profile that was in use when the file was written, so the window
    /// can reopen on the same slot.
    /// </summary>
    public int ActiveProfileIndex { get; set; }

    /// <summary>
    /// Gets or sets the theme the window was last drawn in.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="AppTheme.System"/> so a file written before this setting existed keeps
    /// following the Windows theme instead of being forced into a palette. The schema version is
    /// deliberately NOT bumped for this: raising it would make ProfileStore treat every existing
    /// profiles.json as unreadable, back it up and reset the user's colour profiles.
    /// </remarks>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>
    /// Gets or sets the profile slots. Always holds exactly <see cref="ColorProfile.SlotCount"/> entries.
    /// </summary>
    public List<ColorProfile> Profiles { get; set; } = [];

    /// <summary>
    /// Creates the default collection, which contains three empty slots.
    /// </summary>
    /// <returns>A new collection.</returns>
    public static ProfileCollection CreateDefault()
    {
        ProfileCollection collection = new()
        {
            Version = CurrentVersion,
            ActiveProfileIndex = 0,
        };

        collection.Normalize();

        return collection;
    }

    /// <summary>
    /// Forces the collection back into a shape the rest of the application can rely on: the current
    /// schema version, exactly three slots, each named and with non-null collections.
    /// </summary>
    public void Normalize()
    {
        Version = CurrentVersion;
        ActiveProfileIndex = Math.Clamp(ActiveProfileIndex, 0, ColorProfile.SlotCount - 1);

        // An undefined value can only come from a hand-edited file, and it must not reach the switch in
        // ThemeManager, so it falls back to System here.
        if (!Enum.IsDefined(Theme))
        {
            Theme = AppTheme.System;
        }

        while (Profiles.Count < ColorProfile.SlotCount)
        {
            Profiles.Add(ColorProfile.Create(Profiles.Count + 1));
        }

        if (Profiles.Count > ColorProfile.SlotCount)
        {
            Profiles.RemoveRange(ColorProfile.SlotCount, Profiles.Count - ColorProfile.SlotCount);
        }

        for (int index = 0; index < Profiles.Count; index++)
        {
            ColorProfile profile = Profiles[index];

            // A renamed profile keeps its custom name; only a missing or blank one falls back to the
            // positional default.
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                profile.Name = $"Profile {index + 1}";
            }

            // Deserialization can hand back nulls even for non-nullable members, so each one is
            // repaired rather than trusted.
            if (profile.Global is null)
            {
                profile.Global = ColorSettings.Defaults();
            }

            if (profile.MonitorOverrides is null)
            {
                profile.MonitorOverrides = [];
            }

            if (profile.Devices is null)
            {
                profile.Devices = [];
            }
        }
    }
}
