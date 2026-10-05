namespace GameMate.Models;

/// <summary>
/// Immutable snapshot of the machine gathered for the system information window: the operating system
/// edition and version, the video controllers, and any warning raised while gathering.
/// </summary>
public sealed record SystemInfo
{
    /// <summary>
    /// Gets the operating system edition, for example <c>Windows 11 Pro</c>. Never empty.
    /// </summary>
    public required string WindowsEdition { get; init; }

    /// <summary>
    /// Gets the operating system version, for example <c>23H2 (build 22631.4890)</c>. Never empty.
    /// </summary>
    public required string WindowsVersion { get; init; }

    /// <summary>
    /// Gets the application's own version, for example <c>0.3.0</c>. Never empty.
    /// </summary>
    public required string SoftwareVersion { get; init; }

    /// <summary>
    /// Gets the video controllers reported by WMI, one entry per adapter. Empty when the query failed
    /// or returned nothing.
    /// </summary>
    public required IReadOnlyList<VideoControllerInfo> VideoControllers { get; init; }

    /// <summary>
    /// Gets an explanation of what could not be read, or <see langword="null"/> when everything was
    /// gathered successfully.
    /// </summary>
    public string? Warning { get; init; }
}
