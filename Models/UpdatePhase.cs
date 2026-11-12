namespace GameMate.Models;

/// <summary>
/// Stage of the in-app update flow.
/// </summary>
/// <remarks>
/// The update window drives its controls from this value rather than from several independent flags, so an
/// illegal combination such as "downloading and failed at once" cannot arise.
/// </remarks>
public enum UpdatePhase
{
    /// <summary>
    /// No transfer is running and no failure is being reported.
    /// </summary>
    Idle,

    /// <summary>
    /// The release archive is being downloaded.
    /// </summary>
    Downloading,

    /// <summary>
    /// The archive has been downloaded and is being unpacked and validated.
    /// </summary>
    Staging,

    /// <summary>
    /// The payload is staged and the helper has been started, so the application must exit.
    /// </summary>
    Ready,

    /// <summary>
    /// The last attempt failed; the reason is shown next to the retry actions.
    /// </summary>
    Failed,
}
