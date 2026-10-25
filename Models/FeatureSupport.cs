using GameMate.Localization;

namespace GameMate.Models;

/// <summary>
/// One row of the feature matrix: a colour control the application offers and whether this machine
/// supports it.
/// </summary>
/// <remarks>
/// The row stores a resource key rather than display text, so the label is resolved on every read and a
/// language switch is reflected as soon as the matrix is rebuilt.
/// </remarks>
public sealed record FeatureSupport
{
    /// <summary>
    /// Gets the resource key naming the control, for example <c>Feature.Vibrance</c>.
    /// </summary>
    public required string FeatureKey { get; init; }

    /// <summary>
    /// Gets the name of the colour control in the selected language.
    /// </summary>
    public string Feature => LanguageService.Current[FeatureKey];

    /// <summary>
    /// Gets a value indicating whether the control is available on this machine.
    /// </summary>
    public required bool IsSupported { get; init; }

    /// <summary>
    /// Gets the Segoe MDL2 Assets glyph shown in the matrix: a tick or a cross.
    /// </summary>
    /// <remarks>
    /// The E-code points are used because the plain Unicode tick and cross are not present in the
    /// installed Segoe MDL2 Assets font, which would render them as a missing-glyph box. They are symbols
    /// rather than words, so they are never translated.
    /// </remarks>
    public string Marker => IsSupported ? "\uE73E" : "\uE711";

    /// <summary>
    /// Gets the wording used outside the window, for example in the clipboard report.
    /// </summary>
    public string StatusText => IsSupported
        ? LanguageService.Current["Feature.Supported"]
        : LanguageService.Current["Feature.NotSupported"];
}
