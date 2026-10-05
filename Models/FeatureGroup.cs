namespace GameMate.Models;

/// <summary>
/// A group of feature matrix rows sharing the API that provides them, for example the Windows gamma
/// ramp or the NVIDIA API.
/// </summary>
public sealed record FeatureGroup
{
    /// <summary>
    /// Gets the heading shown above the group, for example <c>Windows API (gamma ramp)</c>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the rows of the group.
    /// </summary>
    public required IReadOnlyList<FeatureSupport> Features { get; init; }
}
