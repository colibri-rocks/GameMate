using System.Globalization;
using GameMate.Models;
using NvAPIWrapper;
using WindowsDisplayAPI.DisplayConfig;
using NvDisplay = NvAPIWrapper.Display.Display;

namespace GameMate.Services;

/// <summary>
/// Enumerates the active displays and correlates them with their NVIDIA counterparts so that the
/// UI can address both the Windows gamma ramp and the NVIDIA colour controls.
/// </summary>
/// <remarks>
/// The member names used here were verified by reflecting over the shipped assemblies during
/// implementation (NvAPIWrapper 0.8.1.101, WindowsDisplayAPI 1.3.0.13):
/// <list type="bullet">
/// <item><description>
/// <c>NvAPIWrapper.NVIDIA.Initialize()</c> starts NVAPI; <c>NVIDIA.DriverVersion</c> is a static
/// property.
/// </description></item>
/// <item><description>
/// <c>NvAPIWrapper.Display.Display.GetDisplays()</c> returns the NVIDIA displays. A display exposes
/// <c>Name</c> (string), <c>DisplayDevice.DisplayId</c> (the numeric display id) and the colour
/// accessors <c>DigitalVibranceControl</c> / <c>HUEControl</c>. <c>PhysicalGPU</c> carries no colour
/// members, which is why this service stores the <c>Display</c> object instead of the GPU.
/// </description></item>
/// <item><description>
/// <c>WindowsDisplayAPI.DisplayConfig.PathInfo.GetActivePaths(bool)</c> returns the OS paths. The GDI
/// name lives on <c>PathInfo.DisplaySource.DisplayName</c>; the stable monitor identity and friendly
/// name live on <c>PathInfo.TargetsInfo[i].DisplayTarget</c> (<c>DevicePath</c>, <c>FriendlyName</c>).
/// <c>PathDisplayTarget</c> has no <c>DeviceName</c> member, so the GDI name can only come from the
/// display source.
/// </description></item>
/// </list>
/// </remarks>
public sealed class DisplayService : IDisplayService
{
    /// <summary>
    /// Prefixes that the two APIs add to device names differently. They carry no identity
    /// information, so they are stripped before the fallback comparison.
    /// </summary>
    private static readonly string[] DeviceNamePrefixes = [@"\\?\", @"\\.\"];

    private readonly object _syncRoot = new();

    private NvidiaAvailability? _availability;
    private IReadOnlyList<DisplayInfo>? _displays;

    /// <inheritdoc />
    public NvidiaAvailability NvidiaAvailability
    {
        get
        {
            lock (_syncRoot)
            {
                return EnsureProbed();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        lock (_syncRoot)
        {
            if (_displays is not null)
            {
                return _displays;
            }

            NvidiaAvailability availability = EnsureProbed();

            NvDisplay[] nvidiaDisplays = availability.IsAvailable
                ? EnumerateNvidiaDisplays()
                : [];

            _displays = EnumerateOsDisplays(nvidiaDisplays);

            return _displays;
        }
    }

    /// <summary>
    /// Initialises NVAPI at most once and caches the outcome. Must be called while holding
    /// <see cref="_syncRoot"/>.
    /// </summary>
    /// <returns>The cached or freshly probed availability.</returns>
    private NvidiaAvailability EnsureProbed()
    {
        if (_availability is not null)
        {
            return _availability;
        }

        // Bitness is validated before touching NVAPI: on a 32-bit host loading nvapi64.dll fails with
        // a DllNotFoundException, which would otherwise look identical to a missing driver.
        string? bitnessProblem = PlatformGuard.EnsureX64();
        if (bitnessProblem is not null)
        {
            _availability = NvidiaAvailability.Unavailable(bitnessProblem);
            return _availability;
        }

        try
        {
            NVIDIA.Initialize();

            _availability = NvidiaAvailability.Available(
                NVIDIA.DriverVersion.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception exception)
        {
            // Covers "no NVIDIA GPU present", an outdated driver and a missing nvapi64.dll. NVAPI
            // failures must never escape this method because it runs on the UI thread while the
            // window is being built.
            _availability = NvidiaAvailability.Unavailable(
                $"The NVIDIA driver could not be initialised ({exception.GetType().Name}: {exception.Message}). "
                + "Digital Vibrance is disabled; Brightness, Contrast, Gamma and Hue fall back to the display gamma ramp.");
        }

        return _availability;
    }

    /// <summary>
    /// Reads the NVIDIA display list.
    /// </summary>
    /// <returns>
    /// The NVIDIA displays, or an empty array when the list could not be read. In the failure case
    /// the cached availability is downgraded so the UI can explain the state.
    /// </returns>
    private NvDisplay[] EnumerateNvidiaDisplays()
    {
        try
        {
            return NvDisplay.GetDisplays();
        }
        catch (Exception exception)
        {
            _availability = NvidiaAvailability.Unavailable(
                $"The NVIDIA display list could not be read ({exception.GetType().Name}: {exception.Message}). "
                + "Digital Vibrance is disabled; Brightness, Contrast, Gamma and Hue fall back to the display gamma ramp.");

            return [];
        }
    }

    /// <summary>
    /// Builds the <see cref="DisplayInfo"/> list from the Windows display configuration.
    /// </summary>
    /// <param name="nvidiaDisplays">NVIDIA displays available for correlation; may be empty.</param>
    /// <returns>The active displays, or an empty list when Windows could not enumerate them.</returns>
    private static IReadOnlyList<DisplayInfo> EnumerateOsDisplays(NvDisplay[] nvidiaDisplays)
    {
        PathInfo[] paths;

        try
        {
            // virtualModeAware: false reports the configuration the operating system is actually
            // using, which is what the gamma ramp has to match.
            paths = PathInfo.GetActivePaths(false);
        }
        catch (Exception)
        {
            return [];
        }

        if (paths.Length == 0)
        {
            return [];
        }

        List<DisplayInfo> displays = new(paths.Length);

        // Tracks which NVIDIA displays were already consumed so one display can never be mapped to
        // two Windows displays.
        HashSet<int> assigned = [];

        int ordinal = 1;

        foreach (PathInfo path in paths)
        {
            PathDisplayTarget? target = null;
            if (path.TargetsInfo is { Length: > 0 })
            {
                target = path.TargetsInfo[0].DisplayTarget;
            }

            string gdiName = path.DisplaySource?.DisplayName ?? string.Empty;
            string devicePath = target?.DevicePath ?? string.Empty;
            string friendlyName = ResolveFriendlyName(target?.FriendlyName, gdiName, ordinal);

            NvDisplay? nvidiaDisplay = MatchNvidiaDisplay(
                gdiName, devicePath, friendlyName, ordinal, paths.Length, nvidiaDisplays, assigned);

            displays.Add(new DisplayInfo
            {
                DeviceKey = ResolveDeviceKey(devicePath, gdiName, ordinal),
                DeviceName = gdiName,
                FriendlyName = friendlyName,
                Ordinal = ordinal,
                IsPrimary = path.IsGDIPrimary,
                NvidiaDisplayId = nvidiaDisplay?.DisplayDevice?.DisplayId,
                NvidiaDisplay = nvidiaDisplay,
            });

            ordinal++;
        }

        return displays;
    }

    /// <summary>
    /// Correlates one Windows display with an NVIDIA display.
    /// </summary>
    /// <param name="gdiName">GDI device name such as <c>\\.\DISPLAY1</c>.</param>
    /// <param name="devicePath">CCD device path of the monitor.</param>
    /// <param name="friendlyName">Monitor friendly name.</param>
    /// <param name="ordinal">One-based position of the display in the Windows list.</param>
    /// <param name="osDisplayCount">Total number of Windows displays.</param>
    /// <param name="nvidiaDisplays">NVIDIA displays to match against.</param>
    /// <param name="assigned">Indices in <paramref name="nvidiaDisplays"/> that are already taken.</param>
    /// <returns>The matching NVIDIA display, or <see langword="null"/> when none could be identified.</returns>
    private static NvDisplay? MatchNvidiaDisplay(
        string gdiName,
        string devicePath,
        string friendlyName,
        int ordinal,
        int osDisplayCount,
        NvDisplay[] nvidiaDisplays,
        HashSet<int> assigned)
    {
        if (nvidiaDisplays.Length == 0)
        {
            return null;
        }

        // Pass 1: literal comparison. Display.Name is documented by the wrapper as the display name
        // and on current drivers it is the GDI name, but the other two keys are tried as well
        // because the exact value is not contractual.
        for (int index = 0; index < nvidiaDisplays.Length; index++)
        {
            if (assigned.Contains(index))
            {
                continue;
            }

            string candidate = nvidiaDisplays[index].Name ?? string.Empty;

            if (Matches(candidate, gdiName)
                || Matches(candidate, devicePath)
                || Matches(candidate, friendlyName))
            {
                assigned.Add(index);
                return nvidiaDisplays[index];
            }
        }

        // Pass 2: the two APIs spell the same display with different prefixes, so compare again with
        // those prefixes removed.
        for (int index = 0; index < nvidiaDisplays.Length; index++)
        {
            if (assigned.Contains(index))
            {
                continue;
            }

            string candidate = nvidiaDisplays[index].Name;

            if (MatchesNormalized(candidate, gdiName)
                || MatchesNormalized(candidate, devicePath)
                || MatchesNormalized(candidate, friendlyName))
            {
                assigned.Add(index);
                return nvidiaDisplays[index];
            }
        }

        // Pass 3: positional fallback, only when both APIs report the same number of displays.
        // A guess is refused otherwise because it would silently apply one monitor's Digital
        // Vibrance to a different monitor.
        if (nvidiaDisplays.Length == osDisplayCount)
        {
            int positional = ordinal - 1;

            if (positional >= 0 && positional < nvidiaDisplays.Length && !assigned.Contains(positional))
            {
                assigned.Add(positional);
                return nvidiaDisplays[positional];
            }
        }

        return null;
    }

    /// <summary>
    /// Picks the name shown in the display selector.
    /// </summary>
    /// <param name="friendlyName">Friendly name reported by the monitor, if any.</param>
    /// <param name="gdiName">GDI device name used as the second choice.</param>
    /// <param name="ordinal">Ordinal used as the last resort.</param>
    /// <returns>A non-empty display label.</returns>
    private static string ResolveFriendlyName(string? friendlyName, string gdiName, int ordinal)
    {
        if (!string.IsNullOrWhiteSpace(friendlyName))
        {
            return friendlyName.Trim();
        }

        return string.IsNullOrWhiteSpace(gdiName) ? $"Display {ordinal}" : gdiName;
    }

    /// <summary>
    /// Builds the key used to persist per-monitor profile overrides.
    /// </summary>
    /// <param name="devicePath">CCD device path, the preferred key.</param>
    /// <param name="gdiName">GDI device name, the fallback key.</param>
    /// <param name="ordinal">Ordinal, used when neither name is available.</param>
    /// <returns>A non-empty key.</returns>
    private static string ResolveDeviceKey(string devicePath, string gdiName, int ordinal)
    {
        if (!string.IsNullOrWhiteSpace(devicePath))
        {
            return devicePath.Trim();
        }

        // The GDI name is the weakest fallback because Windows renumbers displays when the layout
        // changes, but it is still better than losing the override entirely.
        if (!string.IsNullOrWhiteSpace(gdiName))
        {
            return gdiName.Trim();
        }

        return $"ordinal:{ordinal}";
    }

    /// <summary>
    /// Compares two device names literally.
    /// </summary>
    /// <param name="candidate">Name reported by NVAPI.</param>
    /// <param name="expected">Name reported by Windows.</param>
    /// <returns><see langword="true"/> when both are non-empty and equal ignoring case.</returns>
    private static bool Matches(string candidate, string expected)
    {
        return expected.Length > 0
            && string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compares two device names after removing the API specific prefixes.
    /// </summary>
    /// <param name="left">First name.</param>
    /// <param name="right">Second name.</param>
    /// <returns><see langword="true"/> when the normalized names are non-empty and equal ignoring case.</returns>
    private static bool MatchesNormalized(string? left, string? right)
    {
        string normalizedLeft = Normalize(left);

        return normalizedLeft.Length > 0
            && string.Equals(normalizedLeft, Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes a device name for comparison by trimming whitespace, dropping the
    /// <c>\\?\</c> / <c>\\.\</c> prefixes and any remaining leading separators.
    /// </summary>
    /// <param name="value">Device name to normalize.</param>
    /// <returns>The normalized name, or an empty string when <paramref name="value"/> is empty.</returns>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string result = value.Trim();

        foreach (string prefix in DeviceNamePrefixes)
        {
            if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                result = result[prefix.Length..];
                break;
            }
        }

        return result.TrimStart('\\').Trim();
    }
}
