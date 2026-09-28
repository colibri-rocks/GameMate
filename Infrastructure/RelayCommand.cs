using System.Windows.Input;

namespace GameMate.Infrastructure;

/// <summary>
/// Lightweight <see cref="ICommand"/> implementation that forwards <see cref="Execute"/> and
/// <see cref="CanExecute"/> to delegates owned by the view model.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>
    /// Initializes a command that ignores the command parameter.
    /// </summary>
    /// <param name="execute">Work performed when the command is executed.</param>
    /// <param name="canExecute">
    /// Optional predicate reporting whether the command is currently available.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="execute"/> is <see langword="null"/>.</exception>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = _ => execute();

        // The parameterless overload is adapted instead of delegating to the other constructor so
        // that the missing parameter is explicit at the call site.
        if (canExecute is null)
        {
            _canExecute = null;
        }
        else
        {
            _canExecute = _ => canExecute();
        }
    }

    /// <summary>
    /// Initializes a command that receives the bound command parameter.
    /// </summary>
    /// <param name="execute">Work performed when the command is executed.</param>
    /// <param name="canExecute">
    /// Optional predicate reporting whether the command is currently available.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="execute"/> is <see langword="null"/>.</exception>
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke(parameter) ?? true;
    }

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            _execute(parameter);
        }
    }

    /// <summary>
    /// Signals the UI that the outcome of <see cref="CanExecute"/> may have changed, for example
    /// after a selection or a busy flag was updated.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
