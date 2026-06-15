namespace StarRupturePlanner.Services;

public sealed class BackgroundTaskRunner : IBackgroundTaskRunner
{
    public Task RunAsync(Action action, CancellationToken cancellationToken = default)
    {
        return Task.Run(action, cancellationToken);
    }

    public Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        return Task.Run(action, cancellationToken);
    }
}
