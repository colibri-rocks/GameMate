using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GameMate.Infrastructure;

/// <summary>
/// Base class for observable view models. Supplies the <see cref="INotifyPropertyChanged"/>
/// plumbing that the WPF binding engine depends on.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises <see cref="PropertyChanged"/> for the supplied property.
    /// </summary>
    /// <param name="propertyName">
    /// Name of the property that changed. Supplied automatically by the compiler through
    /// <see cref="CallerMemberNameAttribute"/> when called from a property setter.
    /// </param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Stores <paramref name="value"/> in <paramref name="field"/> and raises a change notification,
    /// but only when the incoming value actually differs from the stored one.
    /// </summary>
    /// <typeparam name="T">Type of the property.</typeparam>
    /// <param name="field">Reference to the backing field of the property.</param>
    /// <param name="value">Value to store.</param>
    /// <param name="propertyName">Filled in by the compiler when omitted.</param>
    /// <returns><see langword="true"/> when the value changed; otherwise <see langword="false"/>.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        // EqualityComparer<T>.Default avoids boxing value types, unlike object.Equals.
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
