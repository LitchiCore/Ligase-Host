using Ligase.Host.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class CoreReadinessLoopTests
{
    [TestMethod]
    public async Task ZeroZeroExactOneStopsAndUnlocksThroughDispatcherOnce()
    {
        var sequence = new Queue<CoreReadinessAttempt>([
            CoreReadinessAttempt.Waiting,
            CoreReadinessAttempt.Waiting,
            CoreReadinessAttempt.Ready]);
        var now = DateTimeOffset.UnixEpoch;
        var dispatchCount = 0;
        var readyUpdates = 0;
        var addEntryUnlocked = false;
        using var loop = new CoreReadinessLoop(
            _ =>
            {
                var result = sequence.Dequeue();
                if (result == CoreReadinessAttempt.Ready)
                {
                    readyUpdates++;
                    addEntryUnlocked = true;
                }
                return Task.FromResult(result);
            },
            _ => throw new AssertFailedException("timeout must not publish"),
            async (action, token) =>
            {
                dispatchCount++;
                return await action(token);
            },
            () => now,
            (delay, _) =>
            {
                now += delay;
                return Task.CompletedTask;
            });

        await loop.StartAsync();

        Assert.AreEqual(3, dispatchCount);
        Assert.AreEqual(1, readyUpdates);
        Assert.IsTrue(addEntryUnlocked);
        Assert.AreEqual(0, sequence.Count);
    }

    [TestMethod]
    public async Task PermanentZeroPublishesExplicitTimeoutAtOverallDeadline()
    {
        var now = DateTimeOffset.UnixEpoch;
        var attempts = 0;
        var timeoutUpdates = 0;
        using var loop = new CoreReadinessLoop(
            _ =>
            {
                attempts++;
                return Task.FromResult(CoreReadinessAttempt.Waiting);
            },
            _ =>
            {
                timeoutUpdates++;
                return Task.FromResult(CoreReadinessAttempt.Terminal);
            },
            (action, token) => action(token),
            () => now,
            (delay, _) =>
            {
                now += delay;
                return Task.CompletedTask;
            },
            interval: TimeSpan.FromMilliseconds(400),
            overallTimeout: TimeSpan.FromSeconds(1));

        await loop.StartAsync();

        Assert.IsTrue(attempts >= 3);
        Assert.AreEqual(1, timeoutUpdates);
        Assert.AreEqual(TimeSpan.FromSeconds(1), now - DateTimeOffset.UnixEpoch);
    }

    [TestMethod]
    public async Task WindowLifetimeCancellationStopsWithoutPublishingTimeout()
    {
        var firstAttempt = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutUpdates = 0;
        using var lifetime = new CancellationTokenSource();
        using var loop = new CoreReadinessLoop(
            _ =>
            {
                firstAttempt.TrySetResult();
                return Task.FromResult(CoreReadinessAttempt.Waiting);
            },
            _ =>
            {
                timeoutUpdates++;
                return Task.FromResult(CoreReadinessAttempt.Terminal);
            },
            (action, token) => action(token),
            delay: (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));

        var running = loop.StartAsync(lifetime.Token);
        await firstAttempt.Task;
        lifetime.Cancel();
        await running;

        Assert.AreEqual(0, timeoutUpdates);
    }

    [TestMethod]
    public async Task DuplicateStartReturnsSameActiveTaskAndDoesNotRunConcurrently()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var peakActive = 0;
        var attempts = 0;
        using var loop = new CoreReadinessLoop(
            async _ =>
            {
                attempts++;
                var current = Interlocked.Increment(ref active);
                peakActive = Math.Max(peakActive, current);
                entered.TrySetResult();
                await release.Task;
                Interlocked.Decrement(ref active);
                return CoreReadinessAttempt.Ready;
            },
            _ => throw new AssertFailedException("timeout must not publish"),
            (action, token) => action(token));

        var first = loop.StartAsync();
        await entered.Task;
        var duplicate = loop.StartAsync();
        Assert.AreSame(first, duplicate);
        release.TrySetResult();
        await first;

        Assert.AreEqual(1, attempts);
        Assert.AreEqual(1, peakActive);
    }
}
