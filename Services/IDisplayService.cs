using GameMate.Models;

namespace GameMate.Services;

/// <summary>
/// Provides the displays that colour settings can be applied to, and reports whether the NVIDIA
/// specific part of that control is available.
/// </summary>
public interface IDisplayService
{
    /// <summary>
    /// Gets the NVAPI availability. The driver is probed once and the result is cached, so reading
    /// this property is cheap.
    /// </summary>
    NvidiaAvailability NvidiaAvailability { get; }

    /// <summary>
    /// Enumerates the active displays and correlates each one with its NVIDIA counterpart where
    /// that is possible.
    /// </summary>
    /// <returns>
    /// The active displays in the order Windows reports them. The list is empty when Windows itself
    /// could not enumerate displays. A display whose NVIDIA counterpart could not be identified is
    /// still returned, with <see cref="DisplayInfo.SupportsNvidiaColor"/> set to <see langword="false"/>.
    /// This method never throws.
    /// </returns>
    IReadOnlyList<DisplayInfo> GetDisplays();
}
