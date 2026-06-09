using System.Windows.Input;

namespace FileUnblocker.ViewModels;

/// <summary>Minimal ICommand implementation for MVVM bindings.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
     _execute    = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
 : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
  // Belt-and-suspenders: never execute if CanExecute is false,
      // even if a binding or code-behind calls Execute directly.
  if (!CanExecute(parameter)) return;
     try { _execute(parameter); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RelayCommand] {ex.Message}"); }
    }

    /// <summary>Call this to re-evaluate CanExecute and refresh bound controls.</summary>
    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Async RelayCommand — executes an async void delegate.</summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    // volatile: prevents the JIT from caching the value in a register across
    // rapid re-clicks, which could allow double-execution of async operations.
private volatile bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
    _execute    = execute;
      _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (_isRunning) return;   // re-entrancy guard — belt-and-suspenders
        _isRunning = true;
     RaiseCanExecuteChanged();
      try
        {
            await _execute();
        }
        catch (Exception ex)
   {
            // Swallow unexpected exceptions so the app stays alive.
      // In a production build this would go to a logging sink.
      System.Diagnostics.Debug.WriteLine($"[AsyncRelayCommand] Unhandled: {ex}");
    }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

  public void RaiseCanExecuteChanged() =>
  CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Generic typed RelayCommand.</summary>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute    = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? p) => _canExecute?.Invoke(p is T t ? t : default) ?? true;
    public void Execute(object? p)    => _execute(p is T t ? t : default);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Generic typed AsyncRelayCommand.</summary>
public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private volatile bool _isRunning;

    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
    {
        _execute  = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

public bool CanExecute(object? p) =>
        !_isRunning && (_canExecute?.Invoke(p is T t ? t : default) ?? true);

    public async void Execute(object? p)
    {
        if (_isRunning) return;
        _isRunning = true;
        RaiseCanExecuteChanged();
   try
     {
            await _execute(p is T t ? t : default);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AsyncRelayCommand<T>] Unhandled: {ex}");
        }
        finally
        {
   _isRunning = false;
  RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
