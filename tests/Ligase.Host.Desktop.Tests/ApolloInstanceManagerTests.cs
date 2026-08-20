using System.Diagnostics;
using System.Net;
using System.Text;
using Ligase.Host.Core.Domain.Installation;
using Ligase.Host.Core.Models;
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
    public async Task InstallerStopUsesManagedSignalAndNeverKills()
    {
        var process = new FakeProcess {
            ExitWhenGracefullySignaled = true,
            ThrowOnIdAfterDispose = true
        };
        var manager = Manager(process);

        var outcome = await manager.StopForInstallerAsync();

        Assert.AreEqual(ApolloStopCodes.Stopped, outcome.Code);
        Assert.AreEqual(1, process.GracefulSignalCount);
        Assert.AreEqual(0, process.KillCount);
        Assert.IsFalse(outcome.ProcessStillAlive);
    }

    [TestMethod]
    public async Task InstallerStopRejectsUnavailableSignalWithoutKill()
    {
        var process = new FakeProcess { GracefulSignalAccepted = false };
        var manager = Manager(process);

        var outcome = await manager.StopForInstallerAsync();

        Assert.AreEqual(ApolloStopCodes.GracefulSignalUnavailable, outcome.Code);
        Assert.AreEqual(0, process.KillCount);
        Assert.IsTrue(outcome.ProcessStillAlive);
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

    [TestMethod]
    public async Task LibraryAuthorityAcceptsExactManagedCore()
    {
        var manager = Manager(new FakeProcess());
        var endpoint = new ApolloCoreEndpoint(
            Ligase.Host.Core.Models.LigaseEndpoint.Create(
                Ligase.Host.Core.Models.LigaseEndpointScheme.Http,
                "127.0.0.1",
                48989,
                source: Ligase.Host.Core.Models.LigaseEndpointSource.Loopback),
            "host-id",
            "Ligase Host");
        var service = new LibraryAuthorityService(
            manager,
            _ => Task.FromResult<IReadOnlyList<ApolloCoreEndpoint>>([endpoint]),
            (_, _, _) => Task.FromResult<AuthorityReadbackDocument?>(new(
                1,
                manager.AuthorityToken,
                manager.StartNonce,
                manager.RootFingerprint,
                "host-id",
                [],
                [])));

        var state = await service.GetStateAsync();

        Assert.AreEqual(Ligase.Host.Core.Models.LibraryAuthorityKind.ManagedAuthoritative, state.Kind);
        Assert.IsTrue(state.CanWrite);
    }

    [TestMethod]
    public async Task LibraryAuthorityReportsStartingInsteadOfMultipleForZeroDiscovery()
    {
        var manager = Manager(new FakeProcess());
        var discoveryCount = 0;
        var service = new LibraryAuthorityService(
            manager,
            _ =>
            {
                discoveryCount++;
                return Task.FromResult<IReadOnlyList<ApolloCoreEndpoint>>([]);
            });

        var state = await service.GetStateAsync();

        Assert.AreEqual(Ligase.Host.Core.Models.LibraryAuthorityKind.Unavailable, state.Kind);
        Assert.AreEqual("coreStarting", state.Code);
        Assert.AreEqual(1, discoveryCount);
        Assert.IsFalse(state.CanWrite);
        Assert.IsFalse(state.Message.Contains("多个", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ReloadPreservesSessionActiveTypedFailure()
    {
        var service = AuthorityService(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = Json("""
                {"error":"sessionActive","reload":{"schemaVersion":1,"resultCode":"rejected","stage":"precondition","reasonCode":"sessionActive","elapsedMs":7}}
                """)
        });

        var error = await Assert.ThrowsExceptionAsync<LibraryAuthorityReadbackException>(
            () => service.RequireReadbackAsync(Endpoint, reload: true));

        Assert.AreEqual("rejected", error.Failure.ResultCode);
        Assert.AreEqual("reload.precondition", error.Failure.Stage);
        Assert.AreEqual("sessionActive", error.Failure.ReasonCode);
        Assert.AreEqual(409, error.Failure.HttpStatusCode);
        Assert.AreEqual(7L, error.Failure.ElapsedMs);
        StringAssert.Contains(error.Message, "活动串流");
    }

    [TestMethod]
    public async Task ReloadPreservesServerFailureAndTransportTimeout()
    {
        var failed = AuthorityService(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = Json("""
                {"error":"reloadFailed","reload":{"schemaVersion":1,"resultCode":"failed","stage":"appCatalogReload","reasonCode":"reloadFailed","elapsedMs":11}}
                """)
        });
        var serverError = await Assert.ThrowsExceptionAsync<LibraryAuthorityReadbackException>(
            () => failed.RequireReadbackAsync(Endpoint, reload: true));
        Assert.AreEqual("reload.appCatalogReload", serverError.Failure.Stage);
        Assert.AreEqual("reloadFailed", serverError.Failure.ReasonCode);
        Assert.AreEqual("failed", serverError.Failure.ResultCode);
        Assert.AreEqual(500, serverError.Failure.HttpStatusCode);

        var timeout = AuthorityService(new ThrowingHandler(new TaskCanceledException()));
        var timeoutError = await Assert.ThrowsExceptionAsync<LibraryAuthorityReadbackException>(
            () => timeout.RequireReadbackAsync(Endpoint, reload: true));
        Assert.AreEqual("timeout", timeoutError.Failure.ResultCode);
        Assert.AreEqual("reload.transport", timeoutError.Failure.Stage);
        Assert.AreEqual("deadlineExceeded", timeoutError.Failure.ReasonCode);
        Assert.IsNull(timeoutError.Failure.HttpStatusCode);
    }

    [TestMethod]
    public async Task ReloadRequiresExactCompletedMetadata()
    {
        var service = AuthorityService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""
                {"schemaVersion":1,"authorityToken":"token","startNonce":"nonce","rootFingerprint":"root","hostUniqueId":"host-id","libraryItems":[],"apps":[],"reload":{"schemaVersion":1,"resultCode":"completed","stage":"readback","reasonCode":"wrong","elapsedMs":1}}
                """)
        });

        var error = await Assert.ThrowsExceptionAsync<LibraryAuthorityReadbackException>(
            () => service.RequireReadbackAsync(Endpoint, reload: true));
        Assert.AreEqual("invalidResponse", error.Failure.ResultCode);
        Assert.AreEqual("reload.semantic", error.Failure.Stage);
        Assert.AreEqual("reloadMetadataMismatch", error.Failure.ReasonCode);
    }

    [TestMethod]
    public async Task ReloadRejectsStatusMetadataCrossSplice()
    {
        var service = AuthorityService(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = Json("""
                {"error":"sessionActive","reload":{"schemaVersion":1,"resultCode":"failed","stage":"appCatalogReload","reasonCode":"reloadFailed","elapsedMs":3}}
                """)
        });

        var error = await Assert.ThrowsExceptionAsync<LibraryAuthorityReadbackException>(
            () => service.RequireReadbackAsync(Endpoint, reload: true));
        Assert.AreEqual("invalidResponse", error.Failure.ResultCode);
        Assert.AreEqual("reload.semantic", error.Failure.Stage);
        Assert.AreEqual("reloadMetadataMismatch", error.Failure.ReasonCode);
        Assert.AreEqual(409, error.Failure.HttpStatusCode);
    }

    [TestMethod]
    public async Task ReloadNormalizesEveryUnexpectedHttpStatusToClosedTuple()
    {
        foreach (var status in new[]
                 {
                     HttpStatusCode.Created,
                     HttpStatusCode.NoContent,
                     (HttpStatusCode)418,
                     HttpStatusCode.BadGateway
                 })
        {
            var service = AuthorityService(new HttpResponseMessage(status)
            {
                Content = Json("{}")
            });
            var error = await Assert.ThrowsExceptionAsync<LibraryAuthorityReadbackException>(
                () => service.RequireReadbackAsync(Endpoint, reload: true));
            Assert.AreEqual("invalidResponse", error.Failure.ResultCode);
            Assert.AreEqual("reload.response", error.Failure.Stage);
            Assert.AreEqual("unexpectedHttpStatus", error.Failure.ReasonCode);
            Assert.AreEqual((int)status, error.Failure.HttpStatusCode);
            LibraryMutationOutcomeSemanticValidator.ValidateAttempt(
                new LibraryMutationAttempt(
                    error.Failure.ResultCode,
                    error.Failure.Stage,
                    error.Failure.ReasonCode,
                    error.Failure.HttpStatusCode,
                    error.Failure.ElapsedMs),
                "addSteam");
        }
    }

    [TestMethod]
    public async Task ReloadNormalizesTransportExceptionWithStatusToClosedTuple()
    {
        var service = AuthorityService(new ThrowingHandler(
            new HttpRequestException(
                "injected status transport failure", null, HttpStatusCode.BadGateway)));

        var error = await Assert.ThrowsExceptionAsync<LibraryAuthorityReadbackException>(
            () => service.RequireReadbackAsync(Endpoint, reload: true));

        Assert.AreEqual("transportFailed", error.Failure.ResultCode);
        Assert.AreEqual("reload.transport", error.Failure.Stage);
        Assert.AreEqual("httpStatusFailure", error.Failure.ReasonCode);
        Assert.AreEqual(502, error.Failure.HttpStatusCode);
        LibraryMutationOutcomeSemanticValidator.ValidateAttempt(
            new LibraryMutationAttempt(
                error.Failure.ResultCode,
                error.Failure.Stage,
                error.Failure.ReasonCode,
                error.Failure.HttpStatusCode,
                error.Failure.ElapsedMs),
            "addSteam");
    }

    [TestMethod]
    public async Task ReloadFailureMessagesDescribeTheTypedBranchWithoutGenericCoreAdvice()
    {
        var cases = new[]
        {
            (HttpStatusCode.Conflict, "rejected", "precondition", "sessionActive", "结束会话"),
            (HttpStatusCode.InternalServerError, "failed", "appCatalogParse", "catalogMalformed", "格式无效"),
            (HttpStatusCode.InternalServerError, "failed", "appCatalogLoad", "catalogUnreadable", "无法读取"),
            (HttpStatusCode.Forbidden, "rejected", "authorityValidation", "authorityMismatch", "身份校验失败")
        };
        foreach (var (status, result, stage, reason, expectedText) in cases)
        {
            var service = AuthorityService(new HttpResponseMessage(status)
            {
                Content = Json(
                    "{\"error\":\"" + reason +
                    "\",\"reload\":{\"schemaVersion\":1,\"resultCode\":\"" + result +
                    "\",\"stage\":\"" + stage +
                    "\",\"reasonCode\":\"" + reason +
                    "\",\"elapsedMs\":2}}")
            });
            var error = await Assert.ThrowsExceptionAsync<LibraryAuthorityReadbackException>(
                () => service.RequireReadbackAsync(Endpoint, reload: true));
            StringAssert.Contains(error.Message, expectedText);
            Assert.IsFalse(error.Message.Contains(
                "请确认 Host 核心正在运行后重试", StringComparison.Ordinal));
        }
    }

    private static readonly ApolloCoreEndpoint Endpoint = new(
        LigaseEndpoint.Create(LigaseEndpointScheme.Http, "127.0.0.1", 48989,
            source: LigaseEndpointSource.Loopback),
        "host-id",
        "Ligase Host");

    private static LibraryAuthorityService AuthorityService(HttpResponseMessage response) =>
        AuthorityService(new FixedHandler(response));

    private static LibraryAuthorityService AuthorityService(HttpMessageHandler handler)
    {
        var manager = Manager(new FakeProcess());
        return new LibraryAuthorityService(
            manager,
            _ => Task.FromResult<IReadOnlyList<ApolloCoreEndpoint>>([Endpoint]),
            client: new HttpClient(handler));
    }

    private static StringContent Json(string value) =>
        new(value, Encoding.UTF8, "application/json");

    private sealed class FixedHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(exception);
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

        private readonly int _id = Interlocked.Increment(ref _nextId);
        public event EventHandler? Exited;
        public int Id => Disposed && ThrowOnIdAfterDispose
            ? throw new ObjectDisposedException(nameof(FakeProcess))
            : _id;
        public int ExitCode { get; private set; }
        public bool HasExited { get; private set; }
        public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;
        public bool ExitOnFirstWait { get; init; }
        public bool ExitWhenKilled { get; init; }
        public bool KillNeverCompletes { get; init; }
        public bool ThrowSynchronouslyOnKill { get; init; }
        public Exception? KillException { get; init; }
        public int KillCount { get; private set; }
        public bool GracefulSignalAccepted { get; init; } = true;
        public bool ExitWhenGracefullySignaled { get; init; }
        public bool ThrowOnIdAfterDispose { get; init; }
        public int GracefulSignalCount { get; private set; }
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

        public bool RequestGracefulExit()
        {
            GracefulSignalCount++;
            if (GracefulSignalAccepted && ExitWhenGracefullySignaled) Exit();
            return GracefulSignalAccepted;
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
