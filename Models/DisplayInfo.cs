using NvDisplay = NvAPIWrapper.Display.Display;

namespace GameMate.Models;

/// <summary>
/// Immutable description of one active display: how Windows identifies it, plus the correlated
/// NVIDIA identity that colour control needs.
/// </summary>
public sealed record DisplayInfo
{
    /// <summary>
    /// Gets the stable key used to persist the per-monitor override of a profile. Prefers the CCD
    /// device path, falling back to the GDI name and finally to the ordinal.
    /// </summary>
    public required string DeviceKey { get; init; }

    /// <summary>
    /// Gets the GDI device name (for example <c>\\.\DISPLAY1</c>) that is passed to <c>CreateDC</c>
    /// when the gamma ramp is applied.
    /// </summary>
    public required string DeviceName { get; init; }

    /// <summary>
    /// Gets the human readable monitor name shown in the display selector. Never empty.
    /// </summary>
    public required string FriendlyName { get; init; }

    /// <summary>
    /// Gets the one-based ordinal used for the "1." prefix in the display selector.
    /// </summary>
    public required int Ordinal { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is the primary desktop display.
    /// </summary>
    public bool IsPrimary { get; init; }

    /// <summary>
    /// Gets the horizontal resolution of the display's current mode in pixels, or zero when the mode
    /// could not be read.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Gets the vertical resolution of the display's current mode in pixels, or zero when the mode
    /// could not be read.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Gets the numeric NVAPI display id, or <see langword="null"/> when this display could not be
    /// correlated with an NVIDIA display.
    /// </summary>
    public uint? NvidiaDisplayId { get; init; }

    /// <summary>
    /// Gets the NVAPI display object that owns the Digital Vibrance and Hue accessors, or
    /// <see langword="null"/> when this display is not driven by an NVIDIA GPU.
    /// </summary>
    /// <remarks>
    /// The wrapper object is kept rather than a raw handle because
    /// <c>NvAPIWrapper.Native.DisplayApi.SetDVCLevelEx</c> and <c>SetHUEAngle</c> need a
    /// <c>DisplayHandle</c> that is only reachable through this instance. Retaining it avoids a
    /// second lookup on every slider change.
    /// </remarks>
    public NvDisplay? NvidiaDisplay { get; init; }

    /// <summary>
    /// Gets the resolution of the current mode as text, for example <c>3840 x 2160</c>.
    /// </summary>
    /// <remarks>
    /// A display whose mode could not be read says so in words rather than showing "0 x 0", which would
    /// look like a rendering fault.
    /// </remarks>
    public string ResolutionLabel =>
        Width > 0 && Height > 0 ? $"{Width} x {Height}" : "Resolution not reported";

    /// <summary>
    /// Gets a value indicating whether Digital Vibrance and the NVAPI-backed Hue control are
    /// available for this display.
    /// </summary>
    public bool SupportsNvidiaColor => NvidiaDisplay is not null;
}
