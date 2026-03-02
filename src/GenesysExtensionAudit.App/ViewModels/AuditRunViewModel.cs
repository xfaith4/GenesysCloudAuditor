using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using GenesysExtensionAudit.Application;

namespace GenesysExtensionAudit.ViewModels;

/// <summary>
/// ViewModel for running an audit.
/// Inputs:  PageSize, IncludeInactive
/// Controls: Start / Cancel
/// Feedback: Progress (percent + message), Error surface
/// </summary>
public sealed class RunAuditViewModel : INotifyPropertyChanged
{
    private readonly IAuditRunner _auditRunner;

    private int _pageSize = 100;
    private bool _includeInactive;
    private bool _isRunning;
    private int _progressPercent;
    private string _progressMessage = string.Empty;
    private string _statusMessage = "Ready.";
    private string? _errorMessage;
    private CancellationTokenSource? _cts;

    public RunAuditViewModel(IAuditRunner auditRunner)
    {
        _auditRunner = auditRunner ?? throw new ArgumentNullException(nameof(auditRunner));

        StartCommand = new RelayCommand(StartAsync, () => !IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Page size used when calling the Genesys Cloud paginated endpoints.
    /// Valid range: 1–500.
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set
        {
            var v = Math.Clamp(value, 1, 500);
            SetField(ref _pageSize, v);
        }
    }

    public bool IncludeInactive
    {
        get => _includeInactive;
        set => SetField(ref _includeInactive, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
            {
                RaiseCommandCanExecuteChanged();
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanCancel));
            }
        }
    }

    public bool CanStart => !IsRunning;
    public bool CanCancel => IsRunning;

    public int ProgressPercent
    {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, value);
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        private set => SetField(ref _progressMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ICommand StartCommand { get; }
    public ICommand CancelCommand { get; }

    private async void StartAsync()
    {
        if (IsRunning) return;

        ErrorMessage = null;
        ProgressPercent = 0;
        ProgressMessage = string.Empty;

        IsRunning = true;
        StatusMessage = "Starting audit...";

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var progress = new Progress<AuditProgress>(p =>
        {
            try
            {
                if (p.Percent is >= 0 and <= 100)
                    ProgressPercent = p.Percent;

                if (!string.IsNullOrWhiteSpace(p.Message))
                    ProgressMessage = p.Message;

                if (!string.IsNullOrWhiteSpace(p.Status))
                    StatusMessage = p.Status;
            }
            catch
            {
                // ignore progress update failures
            }
        });

        try
        {
            StatusMessage = "Running audit...";
            await _auditRunner.RunAsync(new AuditRunOptions
            {
                PageSize = PageSize,
                IncludeInactiveUsers = IncludeInactive
            }, progress, ct).ConfigureAwait(true);

            ProgressPercent = 100;
            ProgressMessage = "Completed.";
            StatusMessage = "Audit completed successfully.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Audit cancelled.";
            ProgressMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Audit failed.";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
        }
    }

    private void Cancel()
    {
        try
        {
            _cts?.Cancel();
            StatusMessage = "Cancelling...";
        }
        catch
        {
            // ignore
        }
    }

    private void RaiseCommandCanExecuteChanged()
    {
        if (StartCommand is RelayCommand s) s.RaiseCanExecuteChanged();
        if (CancelCommand is RelayCommand c) c.RaiseCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Simple synchronous command with CanExecute support.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
