using System.ComponentModel;
using System.Runtime.InteropServices;
using GameMate.Models;

namespace GameMate.Services;

/// <summary>
/// Applies and reads a display gamma ramp through the Windows GDI display device context.
/// </summary>
/// <remarks>
/// The gamma ramp is the only desktop colour vehicle the operating system exposes, and the NVIDIA
/// driver resolves it for the display. This is the same path NVIDIA Control Panel uses for its
/// brightness, contrast and gamma sliders.
/// </remarks>
public sealed class GammaRampDevice : IGammaRampDevice
{
    /// <summary>
    /// Library that exports every function used here.
    /// </summary>
    private const string GraphicsLibrary = "gdi32.dll";

    // Every entry point is spelled out exactly so the CLR never appends an "A"/"W" suffix:
    // SetDeviceGammaRamp and GetDeviceGammaRamp have no character-set variants at all, and CreateDC
    // is pinned to the Unicode variant because device names are Unicode paths.

    /// <summary>
    /// Creates a device context for a device name such as <c>\\.\DISPLAY1</c>.
    /// </summary>
    /// <param name="driver">Print driver name; <see langword="null"/> for a display device.</param>
    /// <param name="device">Device name to open.</param>
    /// <param name="port">Output port; <see langword="null"/> when the device name is enough.</param>
    /// <param name="initialData">Unused for display devices.</param>
    /// <returns>A device context handle, or <see cref="IntPtr.Zero"/> on failure.</returns>
    [DllImport(GraphicsLibrary, EntryPoint = "CreateDCW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr CreateDeviceContext(string? driver, string? device, string? port, IntPtr initialData);

    /// <summary>
    /// Releases a device context created by <see cref="CreateDeviceContext"/>.
    /// </summary>
    /// <param name="deviceContext">Device context to release.</param>
    /// <returns><see langword="true"/> when the context was released.</returns>
    [DllImport(GraphicsLibrary, EntryPoint = "DeleteDC", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDeviceContext(IntPtr deviceContext);

    /// <summary>
    /// Uploads a gamma ramp to the display behind the device context.
    /// </summary>
    /// <param name="deviceContext">Display device context.</param>
    /// <param name="ramp">768 entries laid out as red, green and blue.</param>
    /// <returns><see langword="true"/> when the driver accepted the ramp.</returns>
    [DllImport(GraphicsLibrary, EntryPoint = "SetDeviceGammaRamp", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDeviceGammaRamp(IntPtr deviceContext, ushort[] ramp);

    /// <summary>
    /// Reads the gamma ramp the display currently holds.
    /// </summary>
    /// <param name="deviceContext">Display device context.</param>
    /// <param name="ramp">Buffer of 768 entries filled by the driver.</param>
    /// <returns><see langword="true"/> when the driver reported the ramp.</returns>
    [DllImport(GraphicsLibrary, EntryPoint = "GetDeviceGammaRamp", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDeviceGammaRamp(IntPtr deviceContext, [In, Out] ushort[] ramp);

    /// <inheritdoc />
    public bool TryApply(DisplayInfo display, ushort[] ramp, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(ramp);

        if (ramp.Length != GammaRampBuilder.RampLength)
        {
            failureReason = $"A gamma ramp must hold {GammaRampBuilder.RampLength} entries, but {ramp.Length} were supplied.";
            return false;
        }

        if (!TryOpenDeviceContext(display, out IntPtr deviceContext, out failureReason))
        {
            return false;
        }

        try
        {
            if (!SetDeviceGammaRamp(deviceContext, ramp))
            {
                failureReason = DescribeLastError("SetDeviceGammaRamp", display);
                return false;
            }

            failureReason = null;
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            failureReason = $"The Windows display API is not available ({exception.Message}).";
            return false;
        }
        finally
        {
            // The ramp belongs to the display device, not to the context, so the context can be
            // released immediately without undoing the change.
            DeleteDeviceContext(deviceContext);
        }
    }

    /// <inheritdoc />
    public bool TryReadCurrent(DisplayInfo display, out ushort[]? ramp, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(display);

        ramp = null;

        if (string.IsNullOrWhiteSpace(display.DeviceName))
        {
            failureReason = $"The display \"{display.FriendlyName}\" has no GDI device name, so its gamma ramp cannot be read.";
            return false;
        }

        if (!TryOpenDeviceContext(display, out IntPtr deviceContext, out failureReason))
        {
            return false;
        }

        try
        {
            ushort[] buffer = new ushort[GammaRampBuilder.RampLength];

            if (!GetDeviceGammaRamp(deviceContext, buffer))
            {
                failureReason = DescribeLastError("GetDeviceGammaRamp", display);
                return false;
            }

            ramp = buffer;
            failureReason = null;
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            failureReason = $"The Windows display API is not available ({exception.Message}).";
            return false;
        }
        finally
        {
            DeleteDeviceContext(deviceContext);
        }
    }

    /// <inheritdoc />
    public ushort[] ReadCurrentOrDefault(DisplayInfo display)
    {
        ArgumentNullException.ThrowIfNull(display);

        if (TryReadCurrent(display, out ushort[]? ramp, out _) && ramp is not null)
        {
            return ramp;
        }

        // Some drivers and remote sessions refuse GetDeviceGammaRamp, so the UI is initialised from
        // the neutral ramp rather than being left without a baseline.
        return GammaRampBuilder.CreateIdentity();
    }

    /// <summary>
    /// Opens a device context for a display.
    /// </summary>
    /// <param name="display">Display whose <see cref="DisplayInfo.DeviceName"/> is opened.</param>
    /// <param name="deviceContext">Receives the device context handle.</param>
    /// <param name="failureReason">User readable reason when opening fails; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a device context was obtained.</returns>
    private static bool TryOpenDeviceContext(DisplayInfo display, out IntPtr deviceContext, out string? failureReason)
    {
        deviceContext = IntPtr.Zero;

        if (string.IsNullOrWhiteSpace(display.DeviceName))
        {
            failureReason = $"The display \"{display.FriendlyName}\" has no GDI device name, so its gamma ramp cannot be changed.";
            return false;
        }

        try
        {
            // A null driver name means "open the display device named by the second argument", which
            // is how one specific monitor is addressed instead of the whole desktop.
            deviceContext = CreateDeviceContext(null, display.DeviceName, null, IntPtr.Zero);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            failureReason = $"The Windows display API is not available ({exception.Message}).";
            return false;
        }

        if (deviceContext == IntPtr.Zero)
        {
            failureReason = DescribeLastError("CreateDC", display);
            return false;
        }

        failureReason = null;
        return true;
    }

    /// <summary>
    /// Builds a user readable message from the last Win32 error.
    /// </summary>
    /// <param name="operation">Operation that failed.</param>
    /// <param name="display">Display the operation was performed on.</param>
    /// <returns>A message naming the operation, the display and the system error text.</returns>
    private static string DescribeLastError(string operation, DisplayInfo display)
    {
        int error = Marshal.GetLastWin32Error();

        string message = error == 0
            ? "the system reported no error code"
            : new Win32Exception(error).Message;

        return $"{operation} failed for \"{display.FriendlyName}\" ({display.DeviceName}): {message}.";
    }
}
