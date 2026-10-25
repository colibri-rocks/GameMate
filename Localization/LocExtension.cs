using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace GameMate.Localization;

/// <summary>
/// Supplies the localized text for a key as a binding, so the element repaints when the language changes.
/// </summary>
/// <remarks>
/// Used as <c>{loc:Loc Main.Heading}</c>. Returning a binding rather than a plain string is what makes a
/// language switch repaint an element that is already on screen: <see cref="LanguageService"/> raises
/// <c>Item[]</c>, and every such binding re-reads its indexer entry.
/// <para>
/// The binding is explicitly <see cref="BindingMode.OneWay"/>. That is not cosmetic: <c>Run.Text</c> binds
/// TwoWay by default, so an unqualified binding to a read-only indexer would throw at load time with
/// "A TwoWay or OneWayToSource binding cannot work on the read-only property".
/// </para>
/// </remarks>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LocExtension"/> class, used by the XAML parser when the key
    /// is supplied positionally.
    /// </summary>
    public LocExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LocExtension"/> class for a specific key.
    /// </summary>
    /// <param name="key">Dotted key of the text to show.</param>
    public LocExtension(string key)
    {
        Key = key;
    }

    /// <summary>
    /// Gets or sets the dotted key of the text to show.
    /// </summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional converter applied to the resolved text.
    /// </summary>
    public IValueConverter? Converter { get; set; }

    /// <summary>
    /// Gets or sets the parameter passed to <see cref="Converter"/>.
    /// </summary>
    public object? ConverterParameter { get; set; }

    /// <summary>
    /// Gets or sets an optional composite format applied to the resolved text.
    /// </summary>
    public string? StringFormat { get; set; }

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        Binding binding = new()
        {
            Source = LanguageService.Current,
            Path = new PropertyPath($"[{Key}]"),
            Mode = BindingMode.OneWay,
        };

        if (Converter is not null)
        {
            binding.Converter = Converter;
        }

        if (ConverterParameter is not null)
        {
            binding.ConverterParameter = ConverterParameter;
        }

        if (StringFormat is not null)
        {
            binding.StringFormat = StringFormat;
        }

        return binding.ProvideValue(serviceProvider);
    }
}
