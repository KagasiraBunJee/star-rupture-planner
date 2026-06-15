using System.Windows.Threading;

namespace StarRupturePlanner.Services;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public bool CheckAccess() => _dispatcher.CheckAccess();

    public async Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        if (CheckAccess())
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return;
        }

        await _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);
    }

    public async Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        if (CheckAccess())
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }

        return await _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);
    }
}
