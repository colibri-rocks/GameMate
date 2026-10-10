using System.Collections.ObjectModel;
using System.Text;
using GameMate.Infrastructure;
using GameMate.Models;
using GameMate.Services;

namespace GameMate.ViewModels;

/// <summary>
/// Backs the system information window: the operating system, the video adapters and the monitors
/// that were detected when the application started.
/// </summary>
public sealed class SystemInfoViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SystemInfoViewModel"/> class and gathers the
    /// machine information immediately, because the window only exists while the user is looking at it.
    /// </summary>
    /// <param name="systemInfoService">Source of the operating system and video adapter information.</param>
    /// <param name="monitors">
    /// Displays detected at startup, which the main view model already enumerated. Reusing them is what
    /// makes the resolution and the primary flag available here without enumerating the displays twice.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when any dependency is null.</exception>
    public SystemInfoViewModel(ISystemInfoService systemInfoService, IEnumerable<MonitorViewModel> monitors)
    {
        ArgumentNullException.ThrowIfNull(systemInfoService);
        ArgumentNullException.ThrowIfNull(monitors);

        SystemInfo info = systemInfoService.GetSystemInfo();

        WindowsEdition = info.WindowsEdition;
        WindowsVersion = info.WindowsVersion;
        SoftwareVersion = info.SoftwareVersion;
        Warning = info.Warning;

        foreach (VideoControllerInfo controller in info.VideoControllers)
        {
            VideoControllers.Add(controller);
        }

        foreach (FeatureGroup group in BuildFeatureGroups(VideoControllers))
        {
            FeatureGroups.Add(group);
        }

        foreach (MonitorViewModel monitor in monitors)
        {
            Monitors.Add(monitor);
        }

        BuildGraphicsEntries();

        // Built once here: the window content cannot change while it is open, so there is no reason to
        // rebuild the same text on every copy.
        ReportText = BuildReport();
    }

    /// <summary>
    /// Gets the operating system edition, for example <c>Windows 11 Pro</c>.
    /// </summary>
    public string WindowsEdition { get; }

    /// <summary>
    /// Gets the operating system version, for example <c>23H2 (build 22631.4890)</c>.
    /// </summary>
    public string WindowsVersion { get; }

    /// <summary>
    /// Gets the application's own version, for example <c>0.3.0</c>.
    /// </summary>
    public string SoftwareVersion { get; }

    /// <summary>
    /// Gets the version line shown in the window, for example <c>Version 0.3.0</c>.
    /// </summary>
    public string SoftwareVersionLabel => $"Version {SoftwareVersion}";

    /// <summary>
    /// Gets an explanation of what could not be read, or <see langword="null"/> when everything was
    /// read successfully. The window hides the warning row while this is null.
    /// </summary>
    public string? Warning { get; }

    /// <summary>
    /// Gets a value indicating whether a warning has to be shown.
    /// </summary>
    public bool HasWarning => Warning is { Length: > 0 };

    /// <summary>
    /// Gets a value indicating whether at least one video adapter was reported.
    /// </summary>
    public bool HasVideoControllers => VideoControllers.Count > 0;

    /// <summary>
    /// Gets a value indicating whether at least one monitor was detected.
    /// </summary>
    public bool HasMonitors => Monitors.Count > 0;

    /// <summary>
    /// Gets the video adapters reported by the operating system, one per adapter.
    /// </summary>
    public ObservableCollection<VideoControllerInfo> VideoControllers { get; } = [];

    /// <summary>
    /// Gets the monitors detected at startup, each carrying its label, resolution and primary flag.
    /// </summary>
    public ObservableCollection<MonitorViewModel> Monitors { get; } = [];

    /// <summary>
    /// Gets the feature matrix: which colour controls this machine supports, grouped by the API that
    /// provides them.
    /// </summary>
    public ObservableCollection<FeatureGroup> FeatureGroups { get; } = [];

    /// <summary>
    /// Gets the merged Graphics content: every video adapter with the monitors it drives.
    /// </summary>
    public ObservableCollection<GraphicsAdapterEntry> GraphicsEntries { get; } = [];

    /// <summary>
    /// Gets the monitors that could not be attributed to a video adapter, so none of them disappears from
    /// the window when the correlation fails.
    /// </summary>
    public ObservableCollection<MonitorViewModel> UnmatchedMonitors { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the unmatched monitors group has to be shown.
    /// </summary>
    public bool HasUnmatchedMonitors => UnmatchedMonitors.Count > 0;

    /// <summary>
    /// Gets the complete window content as plain text, ready to be placed on the clipboard.
    /// </summary>
    /// <remarks>
    /// The text is produced here rather than in the window so that the report is defined next to the data
    /// it describes, and so the window keeps no formatting logic of its own.
    /// </remarks>
    public string ReportText { get; }

    /// <summary>
    /// Builds the feature matrix, grouped by the API that applies each control.
    /// </summary>
    /// <param name="controllers">Video adapters reported by the operating system.</param>
    /// <returns>The groups, in the order they are shown.</returns>
    /// <remarks>
    /// Brightness, Contrast, Gamma and the ramp approximation of Hue all go through the Windows gamma
    /// ramp, which works on any adapter. Digital Vibrance and the native Hue rotation are applied only
    /// through NVAPI, so they need an NVIDIA card. Hue therefore appears in both groups: NVAPI is the
    /// preferred path and the ramp is what remains when NVAPI is unavailable.
    /// </remarks>
    private static IEnumerable<FeatureGroup> BuildFeatureGroups(IEnumerable<VideoControllerInfo> controllers)
    {
        bool hasNvidia = controllers.Any(controller =>
            controller.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));

        // The gamma ramp path works on any adapter, so every row in this group is available.
        yield return new FeatureGroup
        {
            Name = "Windows API (gamma ramp)",
            Features =
            [
                new FeatureSupport { Feature = "Brightness", IsSupported = true },
                new FeatureSupport { Feature = "Contrast", IsSupported = true },
                new FeatureSupport { Feature = "Gamma", IsSupported = true },
                new FeatureSupport { Feature = "Hue (ramp approximation)", IsSupported = true },
            ],
        };

        // NVAPI is NVIDIA only, so this group follows the presence of an NVIDIA adapter.
        yield return new FeatureGroup
        {
            Name = "NVAPI (NVIDIA)",
            Features =
            [
                new FeatureSupport { Feature = "Digital Vibrance", IsSupported = hasNvidia },
                new FeatureSupport { Feature = "Hue (native, preferred)", IsSupported = hasNvidia },
            ],
        };
    }

    /// <summary>
    /// Attributes each monitor to the video adapter that drives it and fills
    /// <see cref="GraphicsEntries"/>.
    /// </summary>
    /// <remarks>
    /// Matching is done on the normalised PCI adapter path, which is the only identifier the display
    /// configuration API and WMI spell the same way. A monitor whose adapter key matches nothing is kept
    /// in <see cref="UnmatchedMonitors"/> rather than dropped, and an adapter with no monitors still gets
    /// an entry so it can report that state.
    /// </remarks>
    private void BuildGraphicsEntries()
    {
        HashSet<MonitorViewModel> attributed = [];

        foreach (VideoControllerInfo adapter in VideoControllers)
        {
            List<MonitorViewModel> driven = [];

            if (adapter.AdapterKey.Length > 0)
            {
                foreach (MonitorViewModel monitor in Monitors)
                {
                    if (string.Equals(monitor.Display.AdapterKey, adapter.AdapterKey, StringComparison.OrdinalIgnoreCase))
                    {
                        driven.Add(monitor);
                        attributed.Add(monitor);
                    }
                }
            }

            GraphicsEntries.Add(new GraphicsAdapterEntry { Adapter = adapter, Monitors = driven });
        }

        foreach (MonitorViewModel monitor in Monitors)
        {
            if (!attributed.Contains(monitor))
            {
                UnmatchedMonitors.Add(monitor);
            }
        }
    }

    /// <summary>
    /// Renders the whole window content as plain text.
    /// </summary>
    /// <returns>The report, without a trailing line break.</returns>
    private string BuildReport()
    {
        StringBuilder builder = new();

        builder.AppendLine("GameMate - System information");
        builder.AppendLine(SoftwareVersionLabel);
        builder.AppendLine();

        if (Warning is { Length: > 0 })
        {
            builder.AppendLine(Warning);
            builder.AppendLine();
        }

        builder.AppendLine("Windows");
        builder.AppendLine(WindowsEdition);
        builder.AppendLine(WindowsVersion);
        builder.AppendLine();

        builder.AppendLine("Graphics");

        if (HasVideoControllers)
        {
            foreach (GraphicsAdapterEntry entry in GraphicsEntries)
            {
                builder.AppendLine(entry.Adapter.DisplayName);
                builder.AppendLine(entry.Adapter.DriverSummary);

                if (entry.HasMonitors)
                {
                    foreach (MonitorViewModel monitor in entry.Monitors)
                    {
                        // The marker flags the primary display and the indentation keeps the monitors
                        // visually beneath the adapter that drives them.
                        string marker = monitor.IsPrimary ? "* " : "  ";

                        builder.AppendLine($"  {marker}{monitor.DisplayLabel} - {monitor.ResolutionLabel}");
                    }
                }
                else
                {
                    builder.AppendLine("  No monitors connected");
                }
            }
        }
        else
        {
            builder.AppendLine("No video adapter was reported.");
        }

        if (HasUnmatchedMonitors)
        {
            builder.AppendLine();
            builder.AppendLine("Unmatched monitors");

            foreach (MonitorViewModel monitor in UnmatchedMonitors)
            {
                builder.AppendLine($"  {monitor.DisplayLabel} - {monitor.ResolutionLabel}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Feature matrix");

        foreach (FeatureGroup group in FeatureGroups)
        {
            builder.AppendLine(group.Name);

            foreach (FeatureSupport feature in group.Features)
            {
                builder.AppendLine($"  {feature.Feature}: {feature.StatusText}");
            }
        }

        return builder.ToString().TrimEnd();
    }
}
