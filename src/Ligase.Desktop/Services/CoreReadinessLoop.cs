namespace Ligase.Host.Desktop.Services;

internal enum CoreReadinessAttempt
{
    Waiting,
    Ready,
    Terminal
}

internal sealed class CoreReadinessLoop : IDisposable
{
    internal static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(400);
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly object _sync = new();
    private readonly Func<CancellationToken, Task<CoreReadinessAttempt>> _attempt;
    private readonly Func<CancellationToken, Task<CoreReadinessAttempt>> _timeout;
    private readonly Func<
        Func<CancellationToken, Task<CoreReadinessAttempt>>,
        CancellationToken,
        Task<CoreReadinessAttempt>> _dispatch;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _overallTimeout;
    private CancellationTokenSource? _activeCancellation;
    private Task? _activeTask;
    private long _generation;

    internal CoreReadinessLoop(
        Func<CancellationToken, Task<CoreReadinessAttempt>> attempt,
        Func<CancellationToken, Task<CoreReadinessAttempt>> timeout,
        Func<
            Func<CancellationToken, Task<CoreReadinessAttempt>>,
            CancellationToken,
            Task<CoreReadinessAttempt>> dispatch,
        Func<DateTimeOffset>? utcNow = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? interval = null,
        TimeSpan? overallTimeout = null)
    {
        _attempt = attempt;
        _timeout = timeout;
        _dispatch = dispatch;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? Task.Delay;
        _interval = interval ?? DefaultInterval;
        _overallTimeout = overallTimeout ?? DefaultTimeout;
        if (_interval <= TimeSpan.Zero || _overallTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));
    }

    internal Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_activeTask is { IsCompleted: false }) return _activeTask;

            _activeCancellation?.Dispose();
            _activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            var generation = ++_generation;
            _activeTask = RunAndClearAsync(generation, _activeCancellation);
            return _activeTask;
        }
    }

    internal void Cancel()
    {
        lock (_sync) _activeCancellation?.Cancel();
    }

    private async Task RunAndClearAsync(
        long generation,
        CancellationTokenSource cancellation)
    {
        await Task.Yield();
        try
        {
            var deadline = _utcNow() + _overallTimeout;
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var result = await _dispatch(_attempt, cancellation.Token);
                if (result != CoreReadinessAttempt.Waiting) return;

                var remaining = deadline - _utcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    await _dispatch(_timeout, cancellation.Token);
                    return;
                }

                await _delay(
                    remaining < _interval ? remaining : _interval,
                    cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_sync)
            {
                if (_generation == generation)
                {
                    _activeTask = null;
                    _activeCancellation = null;
                    cancellation.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        Cancel();
        lock (_sync)
        {
            if (_activeTask is null)
            {
                _activeCancellation?.Dispose();
                _activeCancellation = null;
            }
        }
    }
}
