using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;

namespace Ligase.Host.Desktop.Services;

public sealed class AttendedPairingCoordinator(
    IAttendedPairingRepository repository) : IAsyncDisposable
{
    private readonly object _actionSync = new();
    private readonly HashSet<string> _actionsInFlight =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, PendingPairingRequest> _known =
        new(StringComparer.Ordinal);
    private Task? _pollTask;
    private string? _instanceKey;
    private AttendedPairingProjection? _current;

    public event Action<AttendedPairingProjection>? ProjectionChanged;
    public event Action<AttendedPairingReadyEvent>? ReadyForApproval;
    public event Action<AttendedPairingRemovedEvent>? RequestRemoved;
    public event Action<string>? AvailabilityChanged;

    public AttendedPairingProjection? Current => Volatile.Read(ref _current);

    public void Start()
    {
        _pollTask ??= PollAsync(_lifetime.Token);
    }

    public async Task RefreshNowAsync(
        CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var projection = await repository.GetPendingAsync(cancellationToken);
            Apply(projection);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task<PairingRequestStatus> AllowAsync(
        string requestId,
        CancellationToken cancellationToken = default) =>
        await ActAsync(requestId, true, cancellationToken);

    public async Task<PairingRequestStatus> RejectAsync(
        string requestId,
        CancellationToken cancellationToken = default) =>
        await ActAsync(requestId, false, cancellationToken);

    private async Task<PairingRequestStatus> ActAsync(
        string requestId,
        bool allow,
        CancellationToken cancellationToken)
    {
        lock (_actionSync)
        {
            if (!_actionsInFlight.Add(requestId))
                throw new AttendedPairingUnavailableException(
                    "该配对请求正在处理中，请稍候。");
        }
        try
        {
            var projection = Current
                ?? throw new AttendedPairingUnavailableException(
                    "待批准设备尚未加载，请刷新后重试。");
            var request = projection.Requests.FirstOrDefault(
                item => item.RequestId == requestId)
                ?? throw new AttendedPairingUnavailableException(
                    "该配对请求已经失效，请等待客户端重新发起。");
            if (allow && !request.ReadyForApproval)
                throw new AttendedPairingUnavailableException(
                    "安全连接仍在建立，暂时不能允许。");
            var status = allow
                ? await repository.AllowAsync(
                    projection.Core, requestId, cancellationToken)
                : await repository.RejectAsync(
                    projection.Core, requestId, cancellationToken);
            await RefreshNowAsync(cancellationToken);
            return status;
        }
        finally
        {
            lock (_actionSync) _actionsInFlight.Remove(requestId);
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RefreshNowAsync(cancellationToken);
                AvailabilityChanged?.Invoke(string.Empty);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                ClearForUnavailable();
                AvailabilityChanged?.Invoke(exception.Message);
            }
            if (!await timer.WaitForNextTickAsync(cancellationToken)) break;
        }
    }

    private void Apply(AttendedPairingProjection projection)
    {
        if (!string.Equals(
                projection.Core.InstanceKey,
                _instanceKey,
                StringComparison.Ordinal))
        {
            foreach (var requestId in _known.Keys)
                RequestRemoved?.Invoke(
                    new AttendedPairingRemovedEvent(
                        _instanceKey ?? string.Empty,
                        requestId));
            _known.Clear();
            _instanceKey = projection.Core.InstanceKey;
        }

        var next = projection.Requests.ToDictionary(
            item => item.RequestId,
            StringComparer.Ordinal);
        foreach (var previous in _known.Keys.Except(next.Keys).ToArray())
        {
            RequestRemoved?.Invoke(
                new AttendedPairingRemovedEvent(
                    projection.Core.InstanceKey,
                    previous));
        }
        foreach (var request in projection.Requests)
        {
            if (request.ReadyForApproval &&
                (!_known.TryGetValue(request.RequestId, out var old) ||
                 !old.ReadyForApproval))
            {
                ReadyForApproval?.Invoke(
                    new AttendedPairingReadyEvent(
                        projection.Core.InstanceKey,
                        request));
            }
        }
        _known.Clear();
        foreach (var pair in next) _known[pair.Key] = pair.Value;
        Volatile.Write(ref _current, projection);
        ProjectionChanged?.Invoke(projection);
    }

    private void ClearForUnavailable()
    {
        if (_known.Count == 0 && Current is null) return;
        foreach (var requestId in _known.Keys)
            RequestRemoved?.Invoke(
                new AttendedPairingRemovedEvent(
                    _instanceKey ?? string.Empty,
                    requestId));
        _known.Clear();
        _instanceKey = null;
        Volatile.Write(ref _current, null);
    }

    public async ValueTask DisposeAsync()
    {
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
