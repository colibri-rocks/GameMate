namespace GameMate.Services;

/// <summary>
/// Composes the per-display gamma ramp that carries brightness, contrast, gamma and (only when the
/// native NVIDIA path is unusable) the hue approximation to the display driver.
/// </summary>
/// <remarks>
/// NVIDIA Control Panel's desktop brightness, contrast and gamma sliders are not NVAPI properties:
/// the driver realises them through the display gamma ramp. That ramp is therefore the vehicle for
/// these settings, while Digital Vibrance and Hue travel through NVAPI directly whenever it is
/// reachable (see <see cref="INvidiaColorService"/>).
/// </remarks>
public static class GammaRampBuilder
{
    /// <summary>
    /// Number of entries in a gamma ramp: three channels of <see cref="ChannelLength"/> entries.
    /// </summary>
    public const int RampLength = 768;

    /// <summary>
    /// Number of entries per colour channel.
    /// </summary>
    public const int ChannelLength = 256;

    /// <summary>
    /// Offset of the red channel inside the ramp array.
    /// </summary>
    public const int RedChannelOffset = 0;

    /// <summary>
    /// Offset of the green channel inside the ramp array.
    /// </summary>
    public const int GreenChannelOffset = 256;

    /// <summary>
    /// Offset of the blue channel inside the ramp array.
    /// </summary>
    public const int BlueChannelOffset = 512;

    /// <summary>
    /// Largest value a single ramp entry can hold.
    /// </summary>
    public const ushort MaximumEntryValue = 65535;

    /// <summary>Lowest brightness value the ramp accepts; 0 is neutral.</summary>
    public const int BrightnessMinimum = -100;

    /// <summary>Highest brightness value the ramp accepts; 0 is neutral.</summary>
    public const int BrightnessMaximum = 100;

    /// <summary>Lowest contrast value the ramp accepts; 0 is neutral.</summary>
    public const int ContrastMinimum = -100;

    /// <summary>Highest contrast value the ramp accepts; 0 is neutral.</summary>
    public const int ContrastMaximum = 100;

    /// <summary>Lowest display gamma the ramp accepts; 1.0 is neutral. One third, so that the
    /// logarithmic slider centred on 1.0 reaches exactly this value at its left end.</summary>
    public const double GammaMinimum = 1.0 / 3.0;

    /// <summary>Highest display gamma the ramp accepts; 1.0 is neutral.</summary>
    public const double GammaMaximum = 3.0;

    /// <summary>Lowest hue angle in degrees; 0 is neutral.</summary>
    public const int HueMinimum = -180;

    /// <summary>Highest hue angle in degrees; 0 is neutral.</summary>
    public const int HueMaximum = 180;

    /// <summary>
    /// Lowest per-channel gain the hue approximation may produce.
    /// </summary>
    private const double MinimumHueGain = 0.25;

    /// <summary>
    /// Highest per-channel gain the hue approximation may produce.
    /// </summary>
    private const double MaximumHueGain = 1.75;

    /// <summary>
    /// Lowest contrast gain. A gain at or below zero would invert the curve and produce a ramp the
    /// driver rejects.
    /// </summary>
    private const double MinimumContrastGain = 0.1;

    /// <summary>
    /// Highest contrast gain, which already saturates the whole range.
    /// </summary>
    private const double MaximumContrastGain = 2.0;

    /// <summary>
    /// Builds the three-channel gamma ramp for the supplied settings.
    /// </summary>
    /// <param name="brightness">
    /// Additive brightness offset inside [<see cref="BrightnessMinimum"/>, <see cref="BrightnessMaximum"/>].
    /// Values outside the range are clamped.
    /// </param>
    /// <param name="contrast">
    /// Contrast gain around the 0.5 midpoint inside [<see cref="ContrastMinimum"/>, <see cref="ContrastMaximum"/>].
    /// Values outside the range are clamped.
    /// </param>
    /// <param name="gamma">
    /// Display gamma inside [<see cref="GammaMinimum"/>, <see cref="GammaMaximum"/>]. Values outside
    /// the range are clamped.
    /// </param>
    /// <param name="hue">
    /// Hue angle in degrees inside [<see cref="HueMinimum"/>, <see cref="HueMaximum"/>]. Pass 0 when
    /// the hue is being applied through NVAPI, because that is the real colour rotation and applying
    /// both would double the effect. Values outside the range are clamped.
    /// </param>
    /// <returns>
    /// A ramp of <see cref="RampLength"/> entries laid out as red, green and blue, each holding
    /// values from 0 to <see cref="MaximumEntryValue"/>. The result is non-decreasing per channel.
    /// </returns>
    public static ushort[] Build(int brightness, int contrast, double gamma, int hue)
    {
        int clampedBrightness = Math.Clamp(brightness, BrightnessMinimum, BrightnessMaximum);
        int clampedContrast = Math.Clamp(contrast, ContrastMinimum, ContrastMaximum);
        double clampedGamma = Math.Clamp(gamma, GammaMinimum, GammaMaximum);
        int clampedHue = Math.Clamp(hue, HueMinimum, HueMaximum);

        // Brightness is an additive offset covering half the range in each direction, so -100 crushes
        // the blacks completely, +100 lifts them to white and 0 is exactly neutral.
        double brightnessOffset = clampedBrightness / 100.0 * 0.5;

        // Contrast is a gain around the 0.5 midpoint rather than around zero, so changing it does not
        // also shift the overall brightness.
        double contrastGain = Math.Clamp(
            (100.0 + clampedContrast) / 100.0,
            MinimumContrastGain,
            MaximumContrastGain);

        // The ramp applies the reciprocal of the display gamma: a larger slider value raises the mid
        // tones instead of darkening them, which is what the NVIDIA slider does.
        double gammaExponent = 1.0 / clampedGamma;

        (double redGain, double greenGain, double blueGain) = ComputeHueChannelGains(clampedHue);

        ushort[] ramp = new ushort[RampLength];

        FillChannel(ramp, RedChannelOffset, brightnessOffset, contrastGain, gammaExponent, redGain);
        FillChannel(ramp, GreenChannelOffset, brightnessOffset, contrastGain, gammaExponent, greenGain);
        FillChannel(ramp, BlueChannelOffset, brightnessOffset, contrastGain, gammaExponent, blueGain);

        return ramp;
    }

    /// <summary>
    /// Builds the neutral ramp, which leaves the picture untouched.
    /// </summary>
    /// <returns>A ramp with <see cref="RampLength"/> entries.</returns>
    public static ushort[] CreateIdentity()
    {
        return Build(0, 0, 1.0, 0);
    }

    /// <summary>
    /// Fills one channel of the ramp with the composed curve.
    /// </summary>
    /// <param name="ramp">Ramp being built.</param>
    /// <param name="channelOffset">Offset of the channel inside <paramref name="ramp"/>.</param>
    /// <param name="brightnessOffset">Additive brightness offset.</param>
    /// <param name="contrastGain">Contrast gain around the midpoint.</param>
    /// <param name="gammaExponent">Exponent applied to the value.</param>
    /// <param name="hueGain">Per-channel hue gain.</param>
    private static void FillChannel(
        ushort[] ramp,
        int channelOffset,
        double brightnessOffset,
        double contrastGain,
        double gammaExponent,
        double hueGain)
    {
        // A gamma ramp has to be non-decreasing; the driver rejects anything else. Each entry is
        // therefore also clamped against its predecessor, which only bites at the extremes where the
        // composed curve saturates.
        ushort previous = 0;

        for (int index = 0; index < ChannelLength; index++)
        {
            double value = (double)index / (ChannelLength - 1);

            // 1. Brightness: additive offset.
            value += brightnessOffset;

            // 2. Contrast: gain around the 0.5 midpoint.
            value = ((value - 0.5) * contrastGain) + 0.5;

            // Clamp before the power call: Math.Pow returns NaN for a negative base and both the
            // offset and the contrast gain can push the value outside [0, 1].
            value = Math.Clamp(value, 0.0, 1.0);

            // 3. Gamma.
            value = Math.Pow(value, gammaExponent);

            // 4. Hue approximation. This stays at 1.0 unless NVAPI's hue control is unavailable.
            value *= hueGain;

            value = Math.Clamp(value, 0.0, 1.0);

            ushort entry = (ushort)Math.Round(value * MaximumEntryValue, MidpointRounding.AwayFromZero);

            if (entry < previous)
            {
                entry = previous;
            }

            ramp[channelOffset + index] = entry;
            previous = entry;
        }
    }

    /// <summary>
    /// Derives a per-channel gain triple from a hue angle.
    /// </summary>
    /// <param name="hue">Hue angle in degrees, already clamped by the caller.</param>
    /// <returns>The red, green and blue gains, all strictly positive.</returns>
    /// <remarks>
    /// A true hue rotation is a colour-space transform, which the desktop colour path does not
    /// expose, so the angle is approximated with per-channel gains that keep the ramp monotonic.
    /// The gains are the diagonal of the standard luminance-preserving hue-rotation matrix.
    /// The row sums of that matrix are deliberately not used: every row sums to exactly 1 for all
    /// angles, so using them would leave the ramp completely unchanged.
    /// The NVAPI hue control is always preferred when it is reachable; this approximation is only the
    /// fallback, and it loses brightness accuracy beyond roughly +/-90 degrees, where the diagonal
    /// turns negative and the gains have to be clamped into a usable band.
    /// </remarks>
    private static (double Red, double Green, double Blue) ComputeHueChannelGains(int hue)
    {
        if (hue == 0)
        {
            return (1.0, 1.0, 1.0);
        }

        double angle = hue * Math.PI / 180.0;
        double cosine = Math.Cos(angle);
        double sine = Math.Sin(angle);

        double red = 0.213 + (0.787 * cosine) - (0.213 * sine);
        double green = 0.715 + (0.285 * cosine) + (0.140 * sine);
        double blue = 0.072 + (0.928 * cosine) + (0.072 * sine);

        if (red > 0.0 && green > 0.0 && blue > 0.0)
        {
            // Renormalising by the mean keeps the overall brightness stable while the hue shifts.
            double average = (red + green + blue) / 3.0;

            return (red / average, green / average, blue / average);
        }

        // The matrix diagonal turned non-positive, so it no longer describes a hue shift. Clamping
        // keeps the gains positive (a negative gain would black out the channel) at the cost of
        // brightness accuracy, which is the documented limitation of this fallback.
        return (
            Math.Clamp(red, MinimumHueGain, MaximumHueGain),
            Math.Clamp(green, MinimumHueGain, MaximumHueGain),
            Math.Clamp(blue, MinimumHueGain, MaximumHueGain));
    }
}
