namespace GameMate.Services;

/// <summary>
/// Verifies runtime prerequisites that the project file cannot enforce on its own.
/// </summary>
public static class PlatformGuard
{
    /// <summary>
    /// Verifies that the current process is 64-bit, which NVAPI requires.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the process is 64-bit; otherwise a message that describes the
    /// problem and what the user should do about it.
    /// </returns>
    public static string? EnsureX64()
    {
        if (Environment.Is64BitProcess)
        {
            return null;
        }

        // nvapi64.dll is a 64-bit module, so a 32-bit host cannot load it and every NVAPI entry
        // point would fail. Reporting this up front keeps the failure out of the UI pipeline and
        // out of the gamma-ramp code path.
        return "GameMate is running as a 32-bit process, so the NVIDIA API (nvapi64.dll) cannot be loaded. "
             + "Digital Vibrance is disabled. Run the x64 build to enable it.";
    }
}
