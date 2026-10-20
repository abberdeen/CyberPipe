using System.Windows.Input;
using Avalonia.Threading;

namespace CyberPipe.Desktop.Common;

/// <summary>
/// Синхронная реализация ICommand для привязки действий в XAML.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
    {
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged()
    {
        EventHandler? handler = CanExecuteChanged;
        if (handler is null)
        {
            return;
        }

        try
        {
            Dispatcher dispatcher = Dispatcher.UIThread;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Post(() => handler.Invoke(this, EventArgs.Empty));
                return;
            }
        }
        catch
        {
            // Unit tests or environments without Avalonia UIThread dispatcher
        }

        handler.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Асинхронная реализация ICommand с защитой от повторного запуска во время выполнения.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _executeAsync;
    private readonly Func<object?, bool>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<object?, Task> executeAsync, Func<object?, bool>? canExecute = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
    }

    public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
        : this(_ => executeAsync(), canExecute != null ? _ => canExecute() : null)
    {
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (_isExecuting != value)
            {
                _isExecuting = value;
                RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);
    }

    public void Execute(object? parameter)
    {
        _ = ExecuteAsync(parameter);
    }

    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            IsExecuting = true;
            await _executeAsync(parameter).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(ex.ToString());
        }
        finally
        {
            IsExecuting = false;
        }
    }

    public void RaiseCanExecuteChanged()
    {
        EventHandler? handler = CanExecuteChanged;
        if (handler is null)
        {
            return;
        }

        try
        {
            Dispatcher dispatcher = Dispatcher.UIThread;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Post(() => handler.Invoke(this, EventArgs.Empty));
                return;
            }
        }
        catch
        {
            // Unit tests or environments without Avalonia UIThread dispatcher
        }

        handler.Invoke(this, EventArgs.Empty);
    }
}
