using Microsoft.Windows.AppLifecycle;

namespace Ligase.Host.Desktop.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string InstanceKey = "Ligase.Host.Desktop.Primary";
    private AppInstance? _primary;

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

    private void OnActivated(object? sender, AppActivationArguments arguments) =>
        RedirectedActivation?.Invoke(arguments);

    public void Dispose()
    {
        if (_primary is null) return;
        _primary.Activated -= OnActivated;
        _primary.UnregisterKey();
        _primary = null;
    }
}
