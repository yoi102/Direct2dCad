using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Lang;

namespace Direct2dCad.ViewModels.Services.Documents;

public partial class CadLongOperation : ObservableObject, IDisposable
{
    private CancellationTokenSource? _current;
    private bool _disposed;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(CancelCommand))] public partial bool IsRunning { get; private set; }
    [ObservableProperty] public partial bool IsVisible { get; private set; }
    [ObservableProperty] public partial string Message { get; private set; } = "";
    public Task RunAsync(string message,Func<CancellationToken,Task> operation,CancellationToken token=default)=>
        RunAsync(message,async ct=>{await operation(ct);return true;},token);
    public async Task<T> RunAsync<T>(string message, Func<CancellationToken, Task<T>> operation, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if (IsRunning) throw new InvalidOperationException("An operation is already running for this document.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        _current = lifetime;
        IsRunning = true;
        Message = message;
        _ = ShowAfterDelayAsync(lifetime);
        try { return await operation(lifetime.Token); }
        finally
        {
            if (ReferenceEquals(_current, lifetime)) { _current = null; IsRunning = false; IsVisible = false; Message = ""; }
            lifetime.Cancel();
        }
    }
    private async Task ShowAfterDelayAsync(CancellationTokenSource lifetime)
    {
        try
        {
            await Task.Delay(300, lifetime.Token);
            if (ReferenceEquals(_current, lifetime)) IsVisible = true;
        }
        catch (OperationCanceledException) { }
    }
    [RelayCommand(CanExecute=nameof(IsRunning))] private void Cancel() { Message = CadUiText.Get(LangKeys.Cancelling); _current?.Cancel(); }
    public void Dispose() { _disposed=true; _current?.Cancel(); }
}
