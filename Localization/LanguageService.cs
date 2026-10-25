using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using GameMate.Models;

namespace GameMate.Localization;

/// <summary>
/// Supplies the interface strings for the selected language and notifies bindings when that language changes.
/// </summary>
/// <remarks>
/// <para>
/// The text lives in <c>Localization\Strings.en.json</c> and <c>Localization\Strings.ru.json</c>, copied next
/// to the executable rather than compiled in, so a translation can be corrected without a rebuild. Each file is
/// a flat dictionary keyed by a dotted identifier, and the two files carry the same key set.
/// </para>
/// <para>
/// The selected language deliberately does NOT change <see cref="CultureInfo.CurrentCulture"/>. The five colour
/// value boxes are editable and parse their text with the current culture, so switching to a culture whose
/// decimal separator is a comma would reject a typed <c>1.0</c> and would silently change how the stored values
/// round-trip. Only the strings are localized.
/// </para>
/// <para>
/// Lookup falls back in a fixed order - selected language, then English, then the key name - so a partially
/// translated file degrades to readable English, and a wholly missing file degrades to visible key names rather
/// than blank text or an exception.
/// </para>
/// </remarks>
public sealed class LanguageService : INotifyPropertyChanged
{
    /// <summary>
    /// Folder, relative to the executable, that holds the language files.
    /// </summary>
    private const string FolderName = "Localization";

    /// <summary>
    /// Code of the fallback language, which is also the language whose file is always consulted second.
    /// </summary>
    private const string EnglishCode = "en";

    /// <summary>
    /// File name code of each language, keyed by the language itself.
    /// </summary>
    private static readonly IReadOnlyDictionary<AppLanguage, string> FileCodes = new Dictionary<AppLanguage, string>
    {
        [AppLanguage.English] = EnglishCode,
        [AppLanguage.Russian] = "ru",
        [AppLanguage.German] = "de",
        [AppLanguage.ChineseSimplified] = "zh-Hans",
    };

    /// <summary>
    /// Shared empty dictionary, used before any file has been read and as the result of a failed load.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> NoStrings = new Dictionary<string, string>();

    /// <summary>
    /// Lazily created process-wide instance; the settings window and every binding read the same one.
    /// </summary>
    private static readonly Lazy<LanguageService> SharedInstance = new(() => new LanguageService());

    /// <summary>
    /// Dictionaries already read from disk, keyed by language, so a switch back does not touch the file again.
    /// </summary>
    private readonly Dictionary<AppLanguage, IReadOnlyDictionary<string, string>> _loaded = [];

    private IReadOnlyDictionary<string, string> _current = NoStrings;
    private IReadOnlyDictionary<string, string> _english = NoStrings;
    private bool _initialised;

    /// <summary>
    /// Gets the process-wide instance.
    /// </summary>
    public static LanguageService Current => SharedInstance.Value;

    /// <summary>
    /// Gets the language currently in use.
    /// </summary>
    public AppLanguage Language { get; private set; } = AppLanguage.English;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the text for a key in the selected language.
    /// </summary>
    /// <param name="key">Dotted key, for example <c>Main.Heading</c>.</param>
    /// <returns>
    /// The text for the selected language, else the English text, else <paramref name="key"/> itself.
    /// </returns>
    public string this[string key] => Resolve(key);

    /// <summary>
    /// Switches the interface language and notifies every binding that reads this instance.
    /// </summary>
    /// <param name="language">Language to switch to.</param>
    public void SetLanguage(AppLanguage language)
    {
        // The English file is always loaded, because it is the fallback for any key missing from the selected
        // language's file.
        _english = Load(AppLanguage.English);

        // Guarding on _initialised rather than Language alone: at startup Language already reads as English, so
        // an equality check would skip the very first load and leave every key unresolved.
        if (language == Language && _initialised)
        {
            return;
        }

        _initialised = true;
        Language = language;
        _current = Load(language);

        // "Item[]" is the name an indexer binding listens for; the null name tells every other binding on this
        // source to re-read as well, which is what repaints text held in code rather than in a binding.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    /// <summary>
    /// Formats a keyed message that carries arguments, for example a status sentence naming a display.
    /// </summary>
    /// <param name="key">Dotted key of the message.</param>
    /// <param name="args">Arguments substituted into the message.</param>
    /// <returns>The formatted message, or the unresolved key when no text exists for it.</returns>
    public string Format(string key, params object?[] args)
    {
        string template = Resolve(key);

        return args.Length == 0
            ? template
            : string.Format(CultureInfo.CurrentCulture, template, args);
    }

    /// <summary>
    /// Reads one language file, caching the result.
    /// </summary>
    /// <param name="language">Language to read.</param>
    /// <returns>The keyed text, or an empty dictionary when the file could not be read.</returns>
    private IReadOnlyDictionary<string, string> Load(AppLanguage language)
    {
        if (_loaded.TryGetValue(language, out IReadOnlyDictionary<string, string>? cached))
        {
            return cached;
        }

        IReadOnlyDictionary<string, string> loaded = NoStrings;

        try
        {
            // An unmapped language falls back to English rather than to a missing file.
            string code = FileCodes.TryGetValue(language, out string? mapped) ? mapped : EnglishCode;
            string path = Path.Combine(AppContext.BaseDirectory, FolderName, $"Strings.{code}.json");

            if (File.Exists(path))
            {
                loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                    ?? NoStrings;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A missing, locked or malformed language file must never stop the application from starting. The
            // fallback chain turns this into English text, or into visible key names at worst.
            loaded = NoStrings;
        }

        _loaded[language] = loaded;
        return loaded;
    }

    /// <summary>
    /// Resolves a key through the fallback chain.
    /// </summary>
    /// <param name="key">Dotted key to resolve.</param>
    /// <returns>The best available text, never <see langword="null"/>.</returns>
    private string Resolve(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (_current.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (_english.TryGetValue(key, out string? english) && !string.IsNullOrEmpty(english))
        {
            return english;
        }

        return key;
    }
}
