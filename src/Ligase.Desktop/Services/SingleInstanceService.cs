using Microsoft.Windows.AppLifecycle;

namespace Ligase.Host.Desktop.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string InstanceKey = "Ligase.Host.Desktop.Primary";
    private AppInstance? _primary;
    private int _isExiting;

    public event Action<AppActivationArguments>? RedirectedActivation;

    public async Task<(bool IsPrimary, AppActivationArguments Arguments)>
        TryAcquireAsync()
    {
        var arguments = AppInstance.GetCurrent().GetActivatedEventArgs();
        var primary = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!primary.IsCurrent)
        {
            await primary.RedirectActivationToAsync(arguments);
            return (false, arguments);
        }

        _primary = primary;
        _primary.Activated += OnActivated;
        return (true, arguments);
    }

    public void BeginExit() => Interlocked.Exchange(ref _isExiting, 1);

    private void OnActivated(object? sender, AppActivationArguments arguments)
    {
        // Keep owning the instance key until process teardown so a concurrent
        // activation cannot elect a new primary while this process exits.
        if (Volatile.Read(ref _isExiting) != 0) return;
        RedirectedActivation?.Invoke(arguments);
    }

    public void Dispose()
    {
        if (_primary is null) return;
        _primary.Activated -= OnActivated;
        _primary.UnregisterKey();
        _primary = null;
    }
}
