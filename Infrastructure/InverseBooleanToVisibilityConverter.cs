using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GameMate.Infrastructure;

/// <summary>
/// Converts a <see cref="bool"/> to a <see cref="Visibility"/> the opposite way round from the built-in
/// converter: <see langword="true"/> produces <see cref="Visibility.Collapsed"/>.
/// </summary>
/// <remarks>
/// The profile slot shows either its button or its rename text box depending on a single flag, so one
/// of the two needs the inverted mapping.
/// </remarks>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean into the inverted visibility.
    /// </summary>
    /// <param name="value">Value produced by the binding source.</param>
    /// <param name="targetType">Type of the binding target; ignored.</param>
    /// <param name="parameter">Converter parameter; ignored.</param>
    /// <param name="culture">Culture used by the binding; ignored.</param>
    /// <returns><see cref="Visibility.Collapsed"/> for <see langword="true"/>, otherwise <see cref="Visibility.Visible"/>.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Not supported: this converter is only used in one direction.
    /// </summary>
    /// <param name="value">Value produced by the binding target.</param>
    /// <param name="targetType">Type of the binding source; ignored.</param>
    /// <param name="parameter">Converter parameter; ignored.</param>
    /// <param name="culture">Culture used by the binding; ignored.</param>
    /// <returns>This method never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
