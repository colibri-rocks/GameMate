using GameMate.Models;
using NvAPIWrapper.Display;
using NvAPIWrapper.Native;

namespace GameMate.Services;

/// <inheritdoc />
public sealed class NvidiaColorService : INvidiaColorService
{
    /// <summary>
    /// Reason reported when the monitor has no correlated NVIDIA display, which is the common case on
    /// a machine without an NVIDIA GPU.
    /// </summary>
    private const string NoNvidiaDisplayMessage =
        "No NVIDIA display is associated with this monitor, so Digital Vibrance and Hue cannot be controlled through NVAPI.";

    /// <inheritdoc />
    public bool TryReadDigitalVibrance(DisplayInfo display, out ColorLevelRange? range, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(display);

        range = null;

        if (display.NvidiaDisplay is not { } nvidiaDisplay)
        {
            failureReason = NoNvidiaDisplayMessage;
            return false;
        }

        try
        {
            DVCInformation information = nvidiaDisplay.DigitalVibranceControl;

            range = new ColorLevelRange(
                information.MinimumLevel,
                information.MaximumLevel,
                information.CurrentLevel,
                information.DefaultLevel);

            failureReason = null;
            return true;
        }
        catch (Exception exception)
        {
            failureReason = DescribeFailure("read the Digital Vibrance range", exception);
            return false;
        }
    }

    /// <inheritdoc />
    public bool TrySetDigitalVibrance(DisplayInfo display, int level, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(display);

        if (display.NvidiaDisplay is not { } nvidiaDisplay)
        {
            failureReason = NoNvidiaDisplayMessage;
            return false;
        }

        try
        {
            // The driver is the authority on the accepted range; re-reading it here keeps a stale UI
            // range from sending a value outside it.
            DVCInformation information = nvidiaDisplay.DigitalVibranceControl;
            int clampedLevel = Math.Clamp(level, information.MinimumLevel, information.MaximumLevel);

            // Min applies no vibrance at all and Max applies the full effect.
            DisplayApi.SetDVCLevelEx(nvidiaDisplay.Handle, clampedLevel);

            failureReason = null;
            return true;
        }
        catch (Exception exception)
        {
            failureReason = DescribeFailure("set the Digital Vibrance level", exception);
            return false;
        }
    }

    /// <inheritdoc />
    public bool TryReadHue(DisplayInfo display, out ColorLevelRange? range, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(display);

        range = null;

        if (display.NvidiaDisplay is not { } nvidiaDisplay)
        {
            failureReason = NoNvidiaDisplayMessage;
            return false;
        }

        try
        {
            HUEInformation information = nvidiaDisplay.HUEControl;

            // NVAPI reports the current and default angle but no limits, so the gamma-ramp hue domain
            // is used for the bounds. Both paths must agree, otherwise switching between them would
            // move the colour.
            range = new ColorLevelRange(
                GammaRampBuilder.HueMinimum,
                GammaRampBuilder.HueMaximum,
                information.CurrentAngle,
                information.DefaultAngle);

            failureReason = null;
            return true;
        }
        catch (Exception exception)
        {
            failureReason = DescribeFailure("read the hue angle", exception);
            return false;
        }
    }

    /// <inheritdoc />
    public bool TrySetHue(DisplayInfo display, int angle, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(display);

        if (display.NvidiaDisplay is not { } nvidiaDisplay)
        {
            failureReason = NoNvidiaDisplayMessage;
            return false;
        }

        try
        {
            int clampedAngle = Math.Clamp(angle, GammaRampBuilder.HueMinimum, GammaRampBuilder.HueMaximum);

            // This is the genuine colour rotation. Because it is applied here, the caller must pass
            // hue 0 to the gamma-ramp builder so the effect is not applied twice.
            DisplayApi.SetHUEAngle(nvidiaDisplay.Handle, clampedAngle);

            failureReason = null;
            return true;
        }
        catch (Exception exception)
        {
            failureReason = DescribeFailure("set the hue angle", exception);
            return false;
        }
    }

    /// <summary>
    /// Formats an NVAPI failure for the status area.
    /// </summary>
    /// <param name="operation">Operation that failed, phrased as a verb.</param>
    /// <param name="exception">Exception raised by the wrapper.</param>
    /// <returns>A message naming the operation and the underlying error.</returns>
    private static string DescribeFailure(string operation, Exception exception)
    {
        return $"Failed to {operation} ({exception.GetType().Name}: {exception.Message}).";
    }
}
