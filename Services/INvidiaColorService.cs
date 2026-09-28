using GameMate.Models;

namespace GameMate.Services;

/// <summary>
/// Wraps the NVIDIA colour controls that NVAPI exposes directly: Digital Vibrance (DVC) and Hue.
/// </summary>
/// <remarks>
/// Both controls are addressed through the wrapper's display object rather than the physical GPU,
/// because the shipped assembly exposes them on <c>NvAPIWrapper.Display.Display</c>
/// (<c>DigitalVibranceControl</c> / <c>HUEControl</c>) while writes go through
/// <c>NvAPIWrapper.Native.DisplayApi.SetDVCLevelEx</c> and <c>SetHUEAngle</c>.
/// Every member reports failure instead of throwing: a display type that does not support these
/// controls (some laptop panels, for example) must only disable the matching slider in the UI.
/// </remarks>
public interface INvidiaColorService
{
    /// <summary>
    /// Reads the Digital Vibrance range the driver reports, including the current level.
    /// </summary>
    /// <param name="display">Display to query.</param>
    /// <param name="range">Receives the range, or <see langword="null"/> when the read failed.</param>
    /// <param name="failureReason">User readable reason when the read fails; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the range could be read.</returns>
    bool TryReadDigitalVibrance(DisplayInfo display, out ColorLevelRange? range, out string? failureReason);

    /// <summary>
    /// Sets the Digital Vibrance level of a display.
    /// </summary>
    /// <param name="display">Display to change.</param>
    /// <param name="level">Requested level; clamped to the range the driver reports.</param>
    /// <param name="failureReason">User readable reason when the call fails; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the level was written.</returns>
    bool TrySetDigitalVibrance(DisplayInfo display, int level, out string? failureReason);

    /// <summary>
    /// Reads the current and default hue angle of a display.
    /// </summary>
    /// <param name="display">Display to query.</param>
    /// <param name="range">
    /// Receives the angle range. NVAPI reports no hue limits, so the range is the one the gamma-ramp
    /// fallback uses and both paths therefore agree on the accepted angles.
    /// </param>
    /// <param name="failureReason">User readable reason when the read fails; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the angles could be read.</returns>
    bool TryReadHue(DisplayInfo display, out ColorLevelRange? range, out string? failureReason);

    /// <summary>
    /// Sets the hue angle of a display through NVAPI.
    /// </summary>
    /// <param name="display">Display to change.</param>
    /// <param name="angle">Requested angle in degrees; clamped to the gamma-ramp hue domain.</param>
    /// <param name="failureReason">User readable reason when the call fails; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the angle was written.</returns>
    bool TrySetHue(DisplayInfo display, int angle, out string? failureReason);
}
