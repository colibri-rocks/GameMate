using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameMate.Models;

namespace GameMate.Services;

/// <inheritdoc />
/// <remarks>
/// The file lives in <c>%APPDATA%\GameMate\profiles.json</c>, which is per user and writable without
/// elevation. Writing goes through a temporary file followed by an overwriting move, so an
/// interrupted save can never leave a half written file behind.
/// </remarks>
public sealed class ProfileStore : IProfileStore
{
    /// <summary>
    /// Serializer configuration shared by load and save.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,

        // Enums are written by name so the file stays readable and hand-editable. Only the theme uses an
        // enum, and it has no predecessor format, so there is nothing to stay compatible with.
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Directory holding the profile file.
    /// </summary>
    private readonly string _directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileStore"/> class.
    /// </summary>
    public ProfileStore()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GameMate");

        FilePath = Path.Combine(_directory, "profiles.json");
    }

    /// <inheritdoc />
    public string FilePath { get; }

    /// <inheritdoc />
    public ProfileCollection Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return ProfileCollection.CreateDefault();
            }

            string json = File.ReadAllText(FilePath);
            ProfileCollection? loaded = JsonSerializer.Deserialize<ProfileCollection>(json, SerializerOptions);

            if (loaded is null || loaded.Version != ProfileCollection.CurrentVersion)
            {
                // Keep the user's file for inspection instead of discarding it, then start clean.
                BackupUnreadableFile();

                return ProfileCollection.CreateDefault();
            }

            loaded.Normalize();

            return loaded;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            BackupUnreadableFile();

            return ProfileCollection.CreateDefault();
        }
    }

    /// <inheritdoc />
    public bool TrySave(ProfileCollection profiles, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        try
        {
            Directory.CreateDirectory(_directory);

            string json = JsonSerializer.Serialize(profiles, SerializerOptions);
            string temporaryPath = FilePath + ".tmp";

            File.WriteAllText(temporaryPath, json);

            // Replacing the real file with a move keeps the visible file either fully old or fully new.
            File.Move(temporaryPath, FilePath, overwrite: true);

            failureReason = null;
            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            failureReason = $"The profiles could not be saved to {FilePath} "
                          + $"({exception.GetType().Name}: {exception.Message}).";

            return false;
        }
    }

    /// <summary>
    /// Determines whether an exception describes an unusable file rather than a programming error.
    /// </summary>
    /// <param name="exception">Exception to classify.</param>
    /// <returns><see langword="true"/> when the caller can recover by falling back to defaults.</returns>
    private static bool IsRecoverable(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException
            or ArgumentException;
    }

    /// <summary>
    /// Renames an unreadable profile file so it is not silently overwritten on the next save.
    /// </summary>
    private void BackupUnreadableFile()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            string backupPath = Path.Combine(
                _directory,
                $"profiles.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json");

            File.Move(FilePath, backupPath, overwrite: true);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            // Backing up is best effort: failing to copy the file away must not stop the application
            // from starting with defaults.
        }
    }
}
