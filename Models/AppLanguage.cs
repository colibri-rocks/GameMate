namespace GameMate.Models;

/// <summary>
/// Language the interface is presented in.
/// </summary>
/// <remarks>
/// The value is persisted as a number in <c>profiles.json</c>, so members may only be appended: renumbering
/// or reordering an existing member would silently change the stored language of every existing user.
/// </remarks>
public enum AppLanguage
{
    /// <summary>
    /// English interface, which is also the fallback for any language without its own file.
    /// </summary>
    English,

    /// <summary>
    /// Russian interface.
    /// </summary>
    Russian,

    /// <summary>
    /// German interface.
    /// </summary>
    German,

    /// <summary>
    /// Simplified Chinese interface.
    /// </summary>
    ChineseSimplified,
}
