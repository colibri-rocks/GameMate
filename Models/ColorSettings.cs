namespace GameMate.Models;

/// <summary>
/// The five colour values the user can change for one display.
/// </summary>
public sealed record ColorSettings
{
    /// <summary>
    /// Vibrance level used when no driver range has been read yet. Matches the middle of the usual
    /// 0 to 100 Digital Vibrance range.
    /// </summary>
    public const int DefaultVibrance = 50;

    /// <summary>
    /// Gets the additive brightness offset, where 0 is neutral.
    /// </summary>
    public int Brightness { get; init; }

    /// <summary>
    /// Gets the contrast gain around the mid point, where 0 is neutral.
    /// </summary>
    public int Contrast { get; init; }

    /// <summary>
    /// Gets the display gamma, where 1.0 is neutral.
    /// </summary>
    public double Gamma { get; init; } = 1.0;

    /// <summary>
    /// Gets the Digital Vibrance level, expressed in the range the driver reports.
    /// </summary>
    public int Vibrance { get; init; } = DefaultVibrance;

    /// <summary>
    /// Gets the hue angle in degrees, where 0 is neutral.
    /// </summary>
    public int Hue { get; init; }

    /// <summary>
    /// Creates the neutral settings, which leave the picture untouched.
    /// </summary>
    /// <returns>A new neutral settings instance.</returns>
    public static ColorSettings Defaults()
    {
        return new ColorSettings
        {
            Brightness = 0,
            Contrast = 0,
            Gamma = 1.0,
            Vibrance = DefaultVibrance,
            Hue = 0,
        };
    }

    /// <summary>
    /// Creates an independent copy, so a stored profile never aliases live view model state.
    /// </summary>
    /// <returns>A copy of these settings.</returns>
    /// <remarks>
    /// Named <c>Copy</c> rather than <c>Clone</c> because the C# compiler reserves members named
    /// "Clone" on record types (CS8859).
    /// </remarks>
    public ColorSettings Copy()
    {
        return this with { };
    }
}
