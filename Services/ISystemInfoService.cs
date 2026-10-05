using GameMate.Models;

namespace GameMate.Services;

/// <summary>
/// Gathers machine level information for the system information window: the operating system edition
/// and version, plus the video adapters and their driver versions.
/// </summary>
public interface ISystemInfoService
{
    /// <summary>
    /// Reads a best-effort snapshot of the machine.
    /// </summary>
    /// <returns>
    /// The gathered information. Failures are reported through <see cref="SystemInfo.Warning"/> rather
    /// than by throwing, so a locked-down machine still gets a window showing whatever was readable.
    /// This method never throws.
    /// </returns>
    SystemInfo GetSystemInfo();
}
