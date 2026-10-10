using GameMate.Models;

namespace GameMate.ViewModels;

/// <summary>
/// One entry of the merged Graphics section: a video adapter together with the monitors it drives.
/// </summary>
/// <remarks>
/// The type lives beside the view models rather than in the model layer because it holds
/// <see cref="MonitorViewModel"/> instances.
/// </remarks>
public sealed record GraphicsAdapterEntry
{
    /// <summary>
    /// Gets the video adapter.
    /// </summary>
    public required VideoControllerInfo Adapter { get; init; }

    /// <summary>
    /// Gets the monitors driven by this adapter, in the order Windows reports them. Empty when the adapter
    /// drives no output, which is normal for the integrated GPU of a desktop machine.
    /// </summary>
    public required IReadOnlyList<MonitorViewModel> Monitors { get; init; }

    /// <summary>
    /// Gets a value indicating whether this adapter drives at least one monitor.
    /// </summary>
    public bool HasMonitors => Monitors.Count > 0;
}
