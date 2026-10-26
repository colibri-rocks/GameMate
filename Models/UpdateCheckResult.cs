namespace GameMate.Models;

/// <summary>
/// Outcome of comparing the running application version with the latest published release.
/// </summary>
public enum UpdateCheckState
{
    /// <summary>
    /// The check has not run yet, so nothing is known about the published version.
    /// </summary>
    Unknown,

    /// <summary>
    /// A request is in flight.
    /// </summary>
    Checking,

    /// <summary>
    /// The running version is the same as, or newer than, the latest published release.
    /// </summary>
    UpToDate,

    /// <summary>
    /// A release newer than the running version exists.
    /// </summary>
    NewVersionAvailable,

    /// <summary>
    /// The check could not complete: offline, rate limited, a server error, or an unreadable response.
    /// </summary>
    Failed,
}

/// <summary>
/// Immutable result of one update check.
/// </summary>
/// <remarks>
/// Deliberately carries no user-facing text. The wording is chosen by the view model through
/// <c>LanguageService</c>, so a language switch re-renders it without the service knowing about presentation.
/// </remarks>
public sealed record UpdateCheckResult
{
    /// <summary>
    /// Gets the state this result describes.
    /// </summary>
    public required UpdateCheckState State { get; init; }

    /// <summary>
    /// Gets the latest published version, for example <c>0.7.2</c>.
    /// </summary>
    /// <remarks>
    /// The leading <c>v</c> of the git tag and any title prefix are already stripped, so this is safe to show
    /// directly and is identical in every language because it is only digits and dots.
    /// </remarks>
    public string? LatestVersion { get; init; }

    /// <summary>
    /// Gets the absolute URI of the release download, for example the release zip.
    /// </summary>
    public string? DownloadUrl { get; init; }

    /// <summary>
    /// Creates the "not checked yet" result.
    /// </summary>
    /// <returns>A result in <see cref="UpdateCheckState.Unknown"/>.</returns>
    public static UpdateCheckResult NotChecked() => new() { State = UpdateCheckState.Unknown };

    /// <summary>
    /// Creates the "request in flight" result.
    /// </summary>
    /// <returns>A result in <see cref="UpdateCheckState.Checking"/>.</returns>
    public static UpdateCheckResult InProgress() => new() { State = UpdateCheckState.Checking };

    /// <summary>
    /// Creates the "running version is current" result.
    /// </summary>
    /// <returns>A result in <see cref="UpdateCheckState.UpToDate"/>.</returns>
    public static UpdateCheckResult UpToDate() => new() { State = UpdateCheckState.UpToDate };

    /// <summary>
    /// Creates the failure result, used for every reason the check could not complete.
    /// </summary>
    /// <returns>A result in <see cref="UpdateCheckState.Failed"/>.</returns>
    public static UpdateCheckResult Failed() => new() { State = UpdateCheckState.Failed };

    /// <summary>
    /// Creates the "newer release exists" result.
    /// </summary>
    /// <param name="latestVersion">Published version with any tag prefix already stripped.</param>
    /// <param name="downloadUrl">Absolute URI the user can download the release from.</param>
    /// <returns>A result in <see cref="UpdateCheckState.NewVersionAvailable"/>.</returns>
    public static UpdateCheckResult NewVersion(string latestVersion, string downloadUrl) => new()
    {
        State = UpdateCheckState.NewVersionAvailable,
        LatestVersion = latestVersion,
        DownloadUrl = downloadUrl,
    };
}
