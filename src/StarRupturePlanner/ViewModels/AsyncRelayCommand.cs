using System.Windows.Input;

namespace StarRupturePlanner.ViewModels;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _executeAsync;
    private readonly Func<bool>? _canExecute;
    private CancellationTokenSource? _executionCancellation;
    private bool _isRunning;

    public AsyncRelayCommand(Func<CancellationToken, Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value)
            {
                return;
            }

            _isRunning = value;
            RaiseCanExecuteChanged();
        }
    }

    public bool CanExecute(object? parameter) => !IsRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        await ExecuteAsync();
    }

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        _executionCancellation = new CancellationTokenSource();
        IsRunning = true;
        try
        {
            await _executeAsync(_executionCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _executionCancellation.Dispose();
            _executionCancellation = null;
            IsRunning = false;
        }
    }

    public void Cancel() => _executionCancellation?.Cancel();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
