namespace GameMate.Models;

/// <summary>
/// Immutable description of one video controller reported by the operating system, as returned by the
/// WMI <c>Win32_VideoController</c> class.
/// </summary>
/// <remarks>
/// One instance is created per adapter, so a hybrid-graphics machine reports the integrated and the
/// discrete adapter separately.
/// </remarks>
public sealed record VideoControllerInfo
{
    /// <summary>
    /// Gets the adapter model, for example <c>NVIDIA GeForce RTX 4070</c>. Never empty.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets a value indicating whether this adapter is an integrated graphics device.
    /// </summary>
    public bool IsIntegrated { get; init; }

    /// <summary>
    /// Gets the adapter model shown in the window, suffixed when the adapter is integrated.
    /// </summary>
    public string DisplayName => IsIntegrated ? $"{Name} (integrated)" : Name;

    /// <summary>
    /// Gets the driver version string reported for this adapter, or <see langword="null"/> when the
    /// driver did not report one.
    /// </summary>
    public string? DriverVersion { get; init; }

    /// <summary>
    /// Gets the driver date reported for this adapter, or <see langword="null"/> when the driver did
    /// not report one.
    /// </summary>
    public DateTime? DriverDate { get; init; }

    /// <summary>
    /// Gets the NVIDIA marketing driver version, for example <c>617.14</c>, or <see langword="null"/>
    /// when this adapter is not an NVIDIA one or NVAPI did not report a version.
    /// </summary>
    /// <remarks>
    /// NVAPI reports the marketing version encoded as <c>major * 100 + minor</c>, which is not the
    /// Windows format that WMI uses, so this value takes precedence when it is present.
    /// </remarks>
    public string? NvidiaDriverVersion { get; init; }

    /// <summary>
    /// Gets the single line shown under the adapter name, combining the driver version and date.
    /// </summary>
    /// <remarks>
    /// The window displays this directly, so a missing value is described in words rather than left
    /// blank, which would look like a rendering fault.
    /// </remarks>
    public string DriverSummary
    {
        get
        {
            string version = NvidiaDriverVersion is { Length: > 0 } nvidia
                ? $"Driver {nvidia}"
                : DriverVersion is { Length: > 0 } reported
                    ? $"Driver {reported}"
                    : "Driver version not reported";

            return DriverDate is { } date
                ? $"{version} ({date:yyyy-MM-dd})"
                : version;
        }
    }
}
