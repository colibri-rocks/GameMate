namespace GameMate.Models;

/// <summary>
/// One row of the feature matrix: a colour control the application offers and whether this machine
/// supports it.
/// </summary>
public sealed record FeatureSupport
{
    /// <summary>
    /// Gets the name of the colour control, for example <c>Digital Vibrance</c>.
    /// </summary>
    public required string Feature { get; init; }

    /// <summary>
    /// Gets a value indicating whether the control is available on this machine.
    /// </summary>
    public required bool IsSupported { get; init; }

    /// <summary>
    /// Gets the Segoe MDL2 Assets glyph shown in the matrix: a tick or a cross.
    /// </summary>
    /// <remarks>
    /// The E-code points are used because the plain Unicode tick and cross are not present in the
    /// installed Segoe MDL2 Assets font, which would render them as a missing-glyph box.
    /// </remarks>
    public string Marker => IsSupported ? "\uE73E" : "\uE711";

    /// <summary>
    /// Gets the wording used outside the window, for example in the clipboard report.
    /// </summary>
    public string StatusText => IsSupported ? "Supported" : "Not supported";
}
