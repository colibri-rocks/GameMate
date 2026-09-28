namespace GameMate.Services;

/// <summary>
/// Outcome of probing the NVIDIA driver through NVAPI: either the NVIDIA specific controls can be
/// used, or a human readable reason explains why they cannot.
/// </summary>
public sealed class NvidiaAvailability
{
    private NvidiaAvailability(bool isAvailable, string? reason, string? driverVersion)
    {
        IsAvailable = isAvailable;
        Reason = reason;
        DriverVersion = driverVersion;
    }

    /// <summary>
    /// Gets a value indicating whether NVAPI initialised successfully.
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// Gets the reason the NVIDIA specific controls are unusable, or <see langword="null"/> when
    /// they are available. The text is shown to the user, so it must stay readable.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Gets the raw NVAPI driver version reported by the driver, or <see langword="null"/> when
    /// unavailable. The value is kept as an unformatted integer because its encoding is driver
    /// specific and must not be guessed.
    /// </summary>
    public string? DriverVersion { get; }

    /// <summary>
    /// Creates a result that reports the NVIDIA paths as usable.
    /// </summary>
    /// <param name="driverVersion">Optional raw driver version string for diagnostics.</param>
    /// <returns>The availability result.</returns>
    public static NvidiaAvailability Available(string? driverVersion = null)
    {
        return new NvidiaAvailability(true, null, driverVersion);
    }

    /// <summary>
    /// Creates a result that reports the NVIDIA paths as unusable.
    /// </summary>
    /// <param name="reason">Explanation shown in the window status area.</param>
    /// <returns>The availability result.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason"/> is empty.</exception>
    public static NvidiaAvailability Unavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new NvidiaAvailability(false, reason, null);
    }
}
