using System.Globalization;
using System.Windows.Data;

namespace GameMate.Infrastructure;

/// <summary>
/// Maps the display gamma to the position of the gamma slider, and back, so that the neutral value 1.0
/// sits at the exact middle of the slider.
/// </summary>
/// <remarks>
/// Gamma is multiplicative, so the slider is scaled logarithmically: the slider carries a position in
/// [-1, 1] and the gamma is base 3 raised to that position. The ends therefore land exactly on 1/3 and
/// 3.0 while position 0 is exactly 1.0. A linear slider from 0.3 to 3.0 could not do this, because its
/// midpoint would be 1.65.
/// </remarks>
public sealed class GammaSliderPositionConverter : IValueConverter
{
    /// <summary>
    /// Base of the logarithmic scale. The cube endpoints 1/3 and 3.0 are what make 1.0 the centre.
    /// </summary>
    private const double ScaleBase = 3.0;

    /// <summary>
    /// Lowest slider position, which corresponds to the lowest gamma the ramp accepts.
    /// </summary>
    private const double MinimumPosition = -1.0;

    /// <summary>
    /// Highest slider position, which corresponds to the highest gamma the ramp accepts.
    /// </summary>
    private const double MaximumPosition = 1.0;

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double gamma || gamma <= 0)
        {
            // A missing or non-positive gamma cannot be scaled logarithmically; the neutral position is
            // the only sensible fallback and keeps the slider usable.
            return 0.0;
        }

        double position = Math.Log(gamma) / Math.Log(ScaleBase);

        // A stored profile may hold a gamma outside the current domain (0.3, for example, which predates
        // the 1/3 lower bound); clamping keeps the slider on scale instead of leaving it off the track.
        return Math.Clamp(position, MinimumPosition, MaximumPosition);
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double position)
        {
            return 1.0;
        }

        double clamped = Math.Clamp(position, MinimumPosition, MaximumPosition);

        return Math.Pow(ScaleBase, clamped);
    }
}
