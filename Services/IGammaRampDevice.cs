using GameMate.Models;

namespace GameMate.Services;

/// <summary>
/// Reads and writes the gamma ramp of a single display.
/// </summary>
public interface IGammaRampDevice
{
    /// <summary>
    /// Applies a ramp to a display.
    /// </summary>
    /// <param name="display">
    /// Target display. Its <see cref="DisplayInfo.DeviceName"/> addresses the GDI device context.
    /// </param>
    /// <param name="ramp">Ramp holding exactly <see cref="GammaRampBuilder.RampLength"/> entries.</param>
    /// <param name="failureReason">User readable reason when the call fails; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the ramp was accepted by the driver.</returns>
    bool TryApply(DisplayInfo display, ushort[] ramp, out string? failureReason);

    /// <summary>
    /// Reads the ramp the driver currently holds for a display.
    /// </summary>
    /// <param name="display">Display to read.</param>
    /// <param name="ramp">Current ramp, or <see langword="null"/> when reading failed.</param>
    /// <param name="failureReason">User readable reason when reading fails; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the ramp could be read.</returns>
    bool TryReadCurrent(DisplayInfo display, out ushort[]? ramp, out string? failureReason);

    /// <summary>
    /// Reads the current ramp and substitutes the neutral ramp when the driver refuses to report it,
    /// so the UI always has a baseline to initialise from.
    /// </summary>
    /// <param name="display">Display to read.</param>
    /// <returns>A ramp holding exactly <see cref="GammaRampBuilder.RampLength"/> entries.</returns>
    ushort[] ReadCurrentOrDefault(DisplayInfo display);
}
