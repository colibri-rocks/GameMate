using GameMate.Models;

namespace GameMate.Services;

/// <summary>
/// Loads and saves the three colour profiles.
/// </summary>
public interface IProfileStore
{
    /// <summary>
    /// Gets the full path of the JSON file backing this store.
    /// </summary>
    string FilePath { get; }

    /// <summary>
    /// Loads the stored profiles.
    /// </summary>
    /// <returns>
    /// The loaded profiles, normalised to three slots. Defaults are returned when the file is
    /// missing, unreadable, written by a different schema version or corrupt; in the last two cases
    /// the unreadable file is renamed so the user's data is preserved.
    /// This method never throws.
    /// </returns>
    ProfileCollection Load();

    /// <summary>
    /// Saves the profiles.
    /// </summary>
    /// <param name="profiles">Profiles to write.</param>
    /// <param name="failureReason">User readable reason when saving fails; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the file was written.</returns>
    bool TrySave(ProfileCollection profiles, out string? failureReason);
}
