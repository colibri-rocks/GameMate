using System.Globalization;
using System.Management;
using System.Reflection;
using GameMate.Models;
using Microsoft.Win32;

namespace GameMate.Services;

/// <inheritdoc />
/// <remarks>
/// The operating system data comes from <c>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion</c>,
/// which reports the edition and the display version; <see cref="Environment.OSVersion"/> reports
/// neither. The adapters come from the WMI <c>Win32_VideoController</c> class, which lists each
/// adapter separately on hybrid-graphics machines and works for NVIDIA, AMD and Intel alike.
/// </remarks>
public sealed class SystemInfoService : ISystemInfoService
{
    /// <summary>
    /// Registry location of the Windows version information.
    /// </summary>
    private const string WindowsVersionKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    /// <summary>
    /// First Windows 11 build number. <c>ProductName</c> reports "Windows 10" even on Windows 11, so
    /// the build number is the only reliable way to correct the branding.
    /// </summary>
    private const int Windows11MinimumBuild = 22000;

    private readonly IDisplayService _displayService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemInfoService"/> class.
    /// </summary>
    /// <param name="displayService">
    /// Supplies the cached NVAPI probe result, which is where the NVIDIA marketing driver version comes
    /// from. Reusing it avoids probing the driver a second time.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="displayService"/> is null.</exception>
    public SystemInfoService(IDisplayService displayService)
    {
        _displayService = displayService ?? throw new ArgumentNullException(nameof(displayService));
    }

    /// <inheritdoc />
    public SystemInfo GetSystemInfo()
    {
        string edition;
        string version;

        // The key is opened once and closed before the WMI query, so the registry handle is not held
        // any longer than necessary.
        using (RegistryKey? key = OpenWindowsVersionKey())
        {
            edition = ResolveEdition(key);
            version = ResolveVersion(key);
        }

        NvidiaAvailability availability = _displayService.NvidiaAvailability;

        // Only NVAPI carries the version NVIDIA itself publishes, so it is used when NVAPI initialised.
        string? nvidiaDriverVersion = availability.IsAvailable
            ? FormatNvidiaDriverVersion(availability.DriverVersion)
            : null;

        IReadOnlyList<VideoControllerInfo> controllers = ReadVideoControllers(nvidiaDriverVersion, out string? warning);

        return new SystemInfo
        {
            WindowsEdition = edition,
            WindowsVersion = version,
            SoftwareVersion = ResolveSoftwareVersion(),
            VideoControllers = controllers,
            Warning = warning,
        };
    }

    /// <summary>
    /// Opens the Windows version registry key.
    /// </summary>
    /// <returns>The key, or <see langword="null"/> when it is missing or access was denied.</returns>
    private static RegistryKey? OpenWindowsVersionKey()
    {
        try
        {
            return Registry.LocalMachine.OpenSubKey(WindowsVersionKeyPath);
        }
        catch (Exception)
        {
            // A locked-down or missing hive must not stop the window from opening; the caller falls
            // back to neutral text.
            return null;
        }
    }

    /// <summary>
    /// Resolves the operating system edition, correcting the Windows 10 branding on Windows 11.
    /// </summary>
    /// <param name="key">Windows version key, or <see langword="null"/> when it could not be opened.</param>
    /// <returns>The edition, for example <c>Windows 11 Pro</c>.</returns>
    private static string ResolveEdition(RegistryKey? key)
    {
        string productName = key?.GetValue("ProductName") as string is { Length: > 0 } name
            ? name
            : "Windows";

        // Windows 11 keeps reporting "Windows 10 ..." in ProductName on many builds, so the build
        // number rewrites the branding while the edition part of the name is preserved.
        if (TryGetBuild(key, out int build)
            && build >= Windows11MinimumBuild
            && productName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows 11" + productName["Windows 10".Length..];
        }

        return productName;
    }

    /// <summary>
    /// Resolves the operating system version, combining the marketing version and the exact build.
    /// </summary>
    /// <param name="key">Windows version key, or <see langword="null"/> when it could not be opened.</param>
    /// <returns>The version, for example <c>23H2 (build 22631.4890)</c>.</returns>
    private static string ResolveVersion(RegistryKey? key)
    {
        string? displayVersion = key?.GetValue("DisplayVersion") as string;
        int revision = key?.GetValue("UBR") is int ubr ? ubr : 0;

        string build = TryGetBuild(key, out int buildNumber)
            ? $"{buildNumber}.{revision}"
            : "unknown";

        return displayVersion is { Length: > 0 }
            ? $"{displayVersion} (build {build})"
            : $"Build {build}";
    }

    /// <summary>
    /// Reads the Windows build number from the registry.
    /// </summary>
    /// <param name="key">Windows version key, or <see langword="null"/> when it could not be opened.</param>
    /// <param name="build">Receives the build number.</param>
    /// <returns><see langword="true"/> when a build number could be parsed.</returns>
    private static bool TryGetBuild(RegistryKey? key, out int build)
    {
        // The value is stored as REG_SZ and only looks numeric, so it is parsed rather than cast.
        string? raw = key?.GetValue("CurrentBuildNumber") as string
                      ?? key?.GetValue("CurrentBuild") as string;

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out build);
    }

    /// <summary>
    /// Converts the integer NVAPI driver version into NVIDIA's marketing version.
    /// </summary>
    /// <param name="rawVersion">Raw value reported by NVAPI, for example <c>61714</c>.</param>
    /// <returns>The marketing version, for example <c>617.14</c>, or <see langword="null"/> when it could not be used.</returns>
    /// <remarks>
    /// NVAPI encodes the version as <c>major * 100 + minor</c>: the machine used during implementation
    /// reported 61714 for driver 617.14. The encoding is derived here rather than displayed raw, and a
    /// value that does not parse falls back to the WMI string instead of showing a wrong number.
    /// </remarks>
    private static string? FormatNvidiaDriverVersion(string? rawVersion)
    {
        return uint.TryParse(rawVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value) && value > 0
            ? $"{value / 100}.{value % 100:D2}"
            : null;
    }

    /// <summary>
    /// Reads the application's own version from its assembly metadata.
    /// </summary>
    /// <returns>The version as displayed, for example <c>0.3.0</c>.</returns>
    /// <remarks>
    /// The informational version is used because it carries the value declared by &lt;Version&gt; in the
    /// project file. The .NET SDK appends the source revision after a '+' by default, so anything from
    /// that separator onwards is dropped; without this the window would show "0.3.0+1a2b3c4".
    /// </remarks>
    private static string ResolveSoftwareVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (informational is { Length: > 0 })
        {
            int separator = informational.IndexOf('+', StringComparison.Ordinal);

            return separator >= 0 ? informational[..separator] : informational;
        }

        // Falls back to the four-part assembly version when no informational version was emitted.
        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Queries WMI for the video controllers and their driver information.
    /// </summary>
    /// <param name="nvidiaDriverVersion">
    /// Marketing version to use for NVIDIA adapters, or <see langword="null"/> when NVAPI reported none.
    /// </param>
    /// <param name="warning">
    /// Receives an explanation when the query failed or returned nothing; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>One entry per adapter, or an empty list when nothing could be read.</returns>
    private static IReadOnlyList<VideoControllerInfo> ReadVideoControllers(
        string? nvidiaDriverVersion,
        out string? warning)
    {
        warning = null;

        try
        {
            List<VideoControllerInfo> controllers = [];

            using ManagementObjectSearcher searcher = new(
                "SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController");

            using ManagementObjectCollection results = searcher.Get();

            foreach (ManagementBaseObject adapter in results)
            {
                using (adapter)
                {
                    string name = adapter["Name"] as string is { Length: > 0 } reported
                        ? reported
                        : "Unnamed video controller";

                    controllers.Add(new VideoControllerInfo
                    {
                        Name = name,

                        // Matched by name, not by NVAPI availability: on a hybrid machine NVAPI is available
                        // while the AMD adapter is also in the list, and that adapter must keep its own value.
                        NvidiaDriverVersion = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                            ? nvidiaDriverVersion
                            : null,
                        DriverVersion = adapter["DriverVersion"] as string,

                        // DriverDate is a CIM_DATETIME; WMI maps it to DateTime, but a driver that
                        // reports nothing yields null, which the window renders as "not reported".
                        DriverDate = adapter["DriverDate"] as DateTime?,
                    });
                }
            }

            if (controllers.Count == 0)
            {
                warning = "The video adapters could not be read from the operating system (WMI returned no result).";
            }

            return controllers;
        }
        catch (Exception exception)
        {
            // WMI can be disabled, corrupted or blocked by policy and the query touches COM; none of
            // that may stop the window from showing the rest of the information.
            warning = $"The video adapters could not be read from the operating system ({exception.GetType().Name}: {exception.Message}).";
            return [];
        }
    }
}
