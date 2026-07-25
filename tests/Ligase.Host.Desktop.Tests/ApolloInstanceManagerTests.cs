using System.Diagnostics;
using Ligase.Host.Core.Domain.Installation;
using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class ApolloInstanceManagerTests
{
    private static readonly ApolloLifecycleTimeouts FastTimeouts = new(
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(30));

    [TestMethod]
    public void ManagedConfigurationDisablesTheCoreTrayIcon()
    {
        var root = Path.Combine(Path.GetTempPath(), "ligase-config-test");
        var configuration = ApolloInstanceManager.BuildManagedConfiguration(
            new LigasePaths(root),
            48989);
        var lines = configuration.Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries);

        CollectionAssert.Contains(lines, "sunshine_name = Ligase Host");
        CollectionAssert.Contains(lines, "system_tray = disabled");
        Assert.AreEqual(
            1,
            lines.Count(line => line.StartsWith("system_tray =", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void StructuredCoreMissingDoesNotUseLegacyFlatFallback()
    {
        var installRoot = Path.Combine(
            Path.GetTempPath(),
            "Ligase.Host.StructuredCore.Tests",
            Guid.NewGuid().ToString("N"));
        var desktop = Path.Combine(installRoot, "Desktop");
        Directory.CreateDirectory(Path.Combine(desktop, "Apollo"));
        File.WriteAllText(
            Path.Combine(installRoot, "ligase-install-manifest.json"),
            """{"schemaVersion":1,"installMode":"packaged","installLayout":"structured-v1"}""");
        File.WriteAllText(
            Path.Combine(desktop, "Apollo", "sunshine.exe"),
            "must-not-be-used");
        try
        {
            var layout = InstallationLayoutResolver.ResolveFromDesktopBase(desktop);
            var manager = new ApolloInstanceManager(
                new LigasePaths(Path.Combine(installRoot, "data")),
                new ApolloPortAllocator(),
                layout);

            Assert.IsNull(manager.ExecutablePath);
            Assert.IsFalse(manager.ManagedExecutable.IsAvailable);
        }
        finally
        {
            Directory.Delete(installRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task NeverStartedAndRepeatedStopsAreIdempotent()
    {
        var manager = Manager();

        var first = await manager.StopAsync();
        var second = await manager.StopAsync();

        Assert.AreEqual(ApolloStopCodes.AlreadyStopped, first.Code);
        Assert.AreEqual(ApolloStopCodes.AlreadyStopped, second.Code);
        Assert.IsFalse(first.ProcessStillAlive);
    }

    [TestMethod]
    public async Task GracefulExitCompletesWithoutKill()
    {
        var process = new FakeProcess { ExitOnFirstWait = true };
        var manager = Manager(process);

        var outcome = await manager.StopAsync();

        Assert.AreEqual(ApolloStopCodes.Stopped, outcome.Code);
        Assert.AreEqual(ApolloStopStages.GracefulWait, outcome.Stage);
        Assert.AreEqual(0, process.KillCount);
        Assert.IsTrue(process.Disposed);
    }

    [TestMethod]
    public async Task ConcurrentStopsShareOneRealStop()
    {
        var process = new FakeProcess { ExitWhenKilled = true };
        var manager = Manager(process);

        var first = manager.StopAsync();
        var second = manager.StopAsync();
        var outcomes = await Task.WhenAll(first, second);

        Assert.AreEqual(1, process.KillCount);
        Assert.AreEqual(ApolloStopCodes.Stopped, outcomes[0].Code);
        Assert.AreEqual(outcomes[0], outcomes[1]);
    }

    [TestMethod]
    public async Task GateContentionReturnsTypedTimeout()
    {
        var process = new FakeProcess();
        var manager = Manager(process);
        using var release = new CancellationTokenSource();
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = manager.HoldLifecycleGateForTestingAsync(entered, release.Token);
        await entered.Task;

        var outcome = await manager.StopAsync();
        release.Cancel();
        await holder;

        Assert.AreEqual(ApolloStopCodes.GateTimeout, outcome.Code);
        Assert.AreEqual(ApolloStopStages.Gate, outcome.Stage);
        Assert.IsTrue(outcome.ProcessStillAlive);
        Assert.AreEqual(ApolloStopNextActions.RetryStop, outcome.NextAction);
    }

    [TestMethod]
    public async Task KillFailureKeepsProcessTracked()
    {
        var process = new FakeProcess { ThrowSynchronouslyOnKill = true };
        var manager = Manager(process);

        var outcome = await manager.StopAsync();

        Assert.AreEqual(ApolloStopCodes.KillFailed, outcome.Code);
        Assert.IsTrue(outcome.ProcessStillAlive);
        Assert.AreEqual(process.Id, manager.ProcessId);
        Assert.IsFalse(process.Disposed);
    }

    [TestMethod]
    public async Task KillTimeoutIsBoundedAndObserved()
    {
        var process = new FakeProcess { KillNeverCompletes = true };
        var manager = Manager(process);
        var started = Stopwatch.StartNew();

        var outcome = await manager.StopAsync();

        Assert.AreEqual(ApolloStopCodes.KillTimeout, outcome.Code);
        Assert.AreEqual(ApolloStopStages.Kill, outcome.Stage);
        Assert.IsTrue(outcome.ProcessStillAlive);
        Assert.IsTrue(started.Elapsed < TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task LateExitAfterTimeoutReleasesTrackedHandle()
    {
        var process = new FakeProcess { KillNeverCompletes = true };
        var manager = Manager(process);

        var timeout = await manager.StopAsync();
        process.Exit();

        Assert.AreEqual(ApolloStopCodes.KillTimeout, timeout.Code);
        Assert.IsTrue(process.Disposed);
        Assert.IsNull(manager.ProcessId);
        Assert.AreEqual(ApolloStopCodes.Stopped, manager.LastStopOutcome?.Code);
    }

    [TestMethod]
    public async Task PostKillWaitTimeoutIsBounded()
    {
        var process = new FakeProcess();
        var manager = Manager(process);

        var outcome = await manager.StopAsync();

        Assert.AreEqual(ApolloStopCodes.PostKillTimeout, outcome.Code);
        Assert.AreEqual(ApolloStopStages.PostKillWait, outcome.Stage);
        Assert.IsTrue(outcome.ProcessStillAlive);
        Assert.AreEqual(1, process.KillCount);
    }

    [TestMethod]
    public async Task CallerCancellationDoesNotCancelInternalCleanup()
    {
        var process = new FakeProcess { KillNeverCompletes = true };
        var manager = Manager(process);
        using var cancellation = new CancellationTokenSource();

        var observing = manager.StopAsync(cancellation.Token);
        cancellation.Cancel();
        var cancelled = await observing;
        var final = await manager.StopAsync();

        Assert.AreEqual(ApolloStopCodes.CallerCancelled, cancelled.Code);
        Assert.AreEqual(ApolloStopCodes.KillTimeout, final.Code);
        Assert.AreEqual(1, process.KillCount);
    }

    [TestMethod]
    public async Task StopWinnerFinishesBeforeNewGenerationStarts()
    {
        var oldProcess = new FakeProcess { ExitWhenKilled = true };
        var newProcess = new FakeProcess();
        var manager = Manager(oldProcess);

        var stopping = manager.StopAsync();
        var starting = manager.StartProcessForTestingAsync(newProcess);
        var outcome = await stopping;

        Assert.AreEqual(ApolloStopCodes.Stopped, outcome.Code);
        Assert.IsTrue(await starting);
        Assert.AreEqual(newProcess.Id, manager.ProcessId);
        Assert.AreEqual(2L, manager.Generation);
    }

    [TestMethod]
    public async Task StartWinnerIsStoppedWithoutCreatingSecondCore()
    {
        var process = new FakeProcess { ExitWhenKilled = true };
        var manager = Manager();

        Assert.IsTrue(await manager.StartProcessForTestingAsync(process));
        var second = new FakeProcess();
        Assert.IsFalse(await manager.StartProcessForTestingAsync(second));
        var outcome = await manager.StopAsync();

        Assert.AreEqual(ApolloStopCodes.Stopped, outcome.Code);
        Assert.AreEqual(0, second.KillCount);
        Assert.AreEqual(1L, manager.Generation);
    }

    [TestMethod]
    public void OldExitedCallbackCannotOverwriteNewGeneration()
    {
        var oldProcess = new FakeProcess();
        var manager = Manager(oldProcess);
        var newProcess = new FakeProcess();
        manager.AttachProcessForTesting(newProcess, 2);

        oldProcess.Exit();

        Assert.AreEqual(newProcess.Id, manager.ProcessId);
        Assert.AreEqual(2L, manager.Generation);
        Assert.IsNull(manager.StartupError);
    }

    private static ApolloInstanceManager Manager(FakeProcess? process = null)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Ligase.Host.Lifecycle.Tests",
            Guid.NewGuid().ToString("N"));
        var manager = new ApolloInstanceManager(
            new LigasePaths(root),
            new ApolloPortAllocator(),
            new ThrowingFactory(),
            FastTimeouts);
        if (process is not null)
            manager.AttachProcessForTesting(process, 1);
        return manager;
    }

    private sealed class ThrowingFactory : IManagedApolloProcessFactory
    {
        public IManagedApolloProcess Start(
            string executable,
            string arguments,
            string workingDirectory) =>
            throw new NotSupportedException();
    }

    private sealed class FakeProcess : IManagedApolloProcess
    {
        private readonly TaskCompletionSource _exit = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _kill = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private static int _nextId = 1000;

        public event EventHandler? Exited;
        public int Id { get; } = Interlocked.Increment(ref _nextId);
        public int ExitCode { get; private set; }
        public bool HasExited { get; private set; }
        public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;
        public bool ExitOnFirstWait { get; init; }
        public bool ExitWhenKilled { get; init; }
        public bool KillNeverCompletes { get; init; }
        public bool ThrowSynchronouslyOnKill { get; init; }
        public Exception? KillException { get; init; }
        public int KillCount { get; private set; }
        public bool Disposed { get; private set; }

        public Task KillAsync()
        {
            KillCount++;
            if (ThrowSynchronouslyOnKill)
                throw new InvalidOperationException();
            if (KillException is not null)
                return Task.FromException(KillException);
            if (KillNeverCompletes)
                return _kill.Task;
            if (ExitWhenKilled)
                Exit();
            return Task.CompletedTask;
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            if (ExitOnFirstWait && !HasExited)
                Exit();
            return _exit.Task;
        }

        public void Exit(int exitCode = 0)
        {
            if (HasExited) return;
            ExitCode = exitCode;
            HasExited = true;
            _exit.TrySetResult();
            Exited?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose() => Disposed = true;
    }
}
