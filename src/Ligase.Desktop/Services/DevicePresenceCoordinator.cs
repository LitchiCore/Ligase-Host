using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;

namespace Ligase.Host.Desktop.Services;

public sealed record DevicePresenceProjection(
    IReadOnlyList<ApolloDevice> Devices,
    bool IsAuthoritative,
    string? Message);

public sealed class DevicePresenceCoordinator : IAsyncDisposable
{
    private readonly IApolloDeviceService _devices;
    private readonly TimeSpan _pollInterval;
    private readonly object _startSync = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _pollTask;
    private DevicePresenceProjection? _current;
    private int _disposed;

    public DevicePresenceCoordinator(IApolloDeviceService devices)
        : this(devices, TimeSpan.FromSeconds(1))
    {
    }

    internal DevicePresenceCoordinator(
        IApolloDeviceService devices,
        TimeSpan pollInterval)
    {
        _devices = devices;
        _pollInterval = pollInterval;
    }

    public event Action<DevicePresenceProjection>? ProjectionChanged;
    public DevicePresenceProjection? Current => Volatile.Read(ref _current);

    public void Start()
    {
        lock (_startSync)
            _pollTask ??= PollAsync(_lifetime.Token);
    }

    public async Task RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                var snapshot = await _devices.GetDevicesAsync(cancellationToken);
                Apply(new DevicePresenceProjection(
                    snapshot.OrderBy(device => device.Uuid, StringComparer.OrdinalIgnoreCase).ToArray(),
                    true,
                    null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ApplyUnavailable(exception.Message);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        while (!cancellationToken.IsCancellationRequested)
        {
            await RefreshNowAsync(cancellationToken);
            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken)) break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void ApplyUnavailable(string message)
    {
        var retained = Current?.Devices.Select(AsUnknown).ToArray() ?? [];
        Apply(new DevicePresenceProjection(
            retained,
            false,
            string.IsNullOrWhiteSpace(message)
                ? "设备在线状态暂时无法确认，请稍后刷新。"
                : message));
    }

    private void Apply(DevicePresenceProjection projection)
    {
        var previous = Current;
        if (previous is not null && Equivalent(previous, projection)) return;
        Volatile.Write(ref _current, projection);
        ProjectionChanged?.Invoke(projection);
    }

    private static ApolloDevice AsUnknown(ApolloDevice device) => new()
    {
        Name = device.Name,
        Uuid = device.Uuid,
        DisplayMode = device.DisplayMode,
        Permissions = device.Permissions,
        AccessMode = device.AccessMode,
        AllowClientCommands = device.AllowClientCommands,
        AlwaysUseVirtualDisplay = device.AlwaysUseVirtualDisplay,
        PresenceState = "unknown",
        SessionState = device.SessionState,
        PresenceExpiresInMs = null
    };

    private static bool Equivalent(DevicePresenceProjection left, DevicePresenceProjection right)
    {
        if (left.IsAuthoritative != right.IsAuthoritative ||
            !string.Equals(left.Message, right.Message, StringComparison.Ordinal) ||
            left.Devices.Count != right.Devices.Count)
            return false;

        for (var index = 0; index < left.Devices.Count; index++)
        {
            var a = left.Devices[index];
            var b = right.Devices[index];
            if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal) ||
                !string.Equals(a.Uuid, b.Uuid, StringComparison.Ordinal) ||
                !string.Equals(a.DisplayMode, b.DisplayMode, StringComparison.Ordinal) ||
                a.Permissions != b.Permissions ||
                !string.Equals(a.AccessMode, b.AccessMode, StringComparison.Ordinal) ||
                a.AllowClientCommands != b.AllowClientCommands ||
                a.AlwaysUseVirtualDisplay != b.AlwaysUseVirtualDisplay ||
                !string.Equals(a.PresenceState, b.PresenceState, StringComparison.Ordinal) ||
                !string.Equals(a.SessionState, b.SessionState, StringComparison.Ordinal) ||
                a.PresenceExpiresInMs != b.PresenceExpiresInMs)
                return false;
        }
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        if (_pollTask is not null)
        {
            try { await _pollTask; }
            catch (OperationCanceledException) { }
        }
        _lifetime.Dispose();
        _refreshGate.Dispose();
    }
}
