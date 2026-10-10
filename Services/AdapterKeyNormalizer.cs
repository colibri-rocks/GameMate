namespace GameMate.Services;

/// <summary>
/// Reduces the different spellings of a PCI adapter path to a single comparable key, so a monitor can
/// be matched to the video adapter that drives it.
/// </summary>
/// <remarks>
/// The display configuration API reports the adapter as
/// <c>\\?\PCI#VEN_10DE&amp;DEV_2C02...#7C0390...#{interface-guid}</c> while WMI reports the same device as
/// <c>PCI\VEN_10DE&amp;DEV_2C02...\7C0390...</c>. Both forms are reduced here to the WMI spelling.
/// </remarks>
public static class AdapterKeyNormalizer
{
    private static readonly string[] DevicePrefixes = [@"\\?\", @"\\.\"];

    /// <summary>
    /// Normalises a device path into the comparable adapter key.
    /// </summary>
    /// <param name="devicePath">Path reported by the display configuration API or by WMI.</param>
    /// <returns>The normalised key, or an empty string when nothing usable was supplied.</returns>
    public static string Normalize(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return string.Empty;
        }

        string normalized = devicePath.Trim();

        foreach (string prefix in DevicePrefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[prefix.Length..];
                break;
            }
        }

        // The display configuration API separates the PCI parts with '#' and appends the interface class
        // GUID; WMI uses '\' and has no GUID, so the separator is unified and the GUID dropped.
        normalized = normalized.Replace('#', '\\');

        int interfaceGuid = normalized.IndexOf("\\{", StringComparison.Ordinal);

        if (interfaceGuid >= 0)
        {
            normalized = normalized[..interfaceGuid];
        }

        return normalized.Trim().TrimEnd('\\');
    }
}
