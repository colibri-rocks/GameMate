using System.Windows.Threading;

namespace GameMate.Infrastructure;

/// <summary>
/// Merges a burst of requests into a single deferred callback on the WPF dispatcher thread.
/// </summary>
/// <remarks>
/// Dragging a slider raises a value change for every pixel of movement. Pushing each change
/// straight to the display driver produces a visibly laggy ramp and hammers the driver, so this
/// type keeps only the newest request and waits for a short quiet period before running it.
/// </remarks>
public sealed class Debouncer : IDisposable
{
    private readonly DispatcherTimer _timer;
    private Action? _pendingAction;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Debouncer"/> class.
    /// </summary>
    /// <param name="interval">
    /// Quiet period that must elapse before the pending action runs. Defaults to 70 milliseconds
    /// when <see langword="null"/>, which is imperceptible while dragging yet far cheaper than
    /// applying every intermediate value.
    /// </param>
    public Debouncer(TimeSpan? interval = null)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = interval ?? TimeSpan.FromMilliseconds(70),
        };

        _timer.Tick += OnTimerTick;
    }

    /// <summary>
    /// Schedules <paramref name="action"/> to run once the debounce interval elapses without any
    /// further call. A previously scheduled action is superseded.
    /// </summary>
    /// <param name="action">Work to perform after the quiet period.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance was already disposed.</exception>
    public void Invoke(Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(action);

        _pendingAction = action;

        // Restarting the timer on every call turns a fixed-rate throttle into a debounce: the
        // callback fires only after the caller stops producing new values.
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>
    /// Cancels any pending action and detaches the dispatcher timer subscription.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _pendingAction = null;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.Stop();

        // Detach the action before invoking it so that a callback scheduling new work is not lost.
        Action? action = _pendingAction;
        _pendingAction = null;

        action?.Invoke();
    }
}
