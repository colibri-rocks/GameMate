using GameMate.Localization;

namespace GameMate.Models;

/// <summary>
/// A group of feature matrix rows sharing the API that provides them, for example the Windows gamma
/// ramp or the NVIDIA API.
/// </summary>
/// <remarks>
/// Like the rows, the group stores a resource key so its heading is resolved in the selected language. The
/// product and API names inside the heading stay verbatim: only the wording around them is translated.
/// </remarks>
public sealed record FeatureGroup
{
    /// <summary>
    /// Gets the resource key of the heading, for example <c>Feature.Group.Nvapi</c>.
    /// </summary>
    public required string NameKey { get; init; }

    /// <summary>
    /// Gets the heading shown above the group in the selected language.
    /// </summary>
    public string Name => LanguageService.Current[NameKey];

    /// <summary>
    /// Gets the rows of the group.
    /// </summary>
    public required IReadOnlyList<FeatureSupport> Features { get; init; }
}
