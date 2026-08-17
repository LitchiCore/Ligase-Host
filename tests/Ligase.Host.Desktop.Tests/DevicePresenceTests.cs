using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Ligase.Host.Desktop.Services;
using Ligase.Host.Desktop.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class DevicePresenceTests
{
    [TestMethod]
    public async Task ExplicitCoreStateTransitionsOfflineOnlineAndReconnect()
    {
        var source = new FakeDeviceService();
        source.Enqueue([Device("alpha", false)]);
        source.Enqueue([Device("alpha", true)]);
        source.Enqueue([Device("alpha", false)]);
        source.Enqueue([Device("alpha", true)]);
        await using var coordinator = new DevicePresenceCoordinator(
            source, TimeSpan.FromHours(1));
        var projections = new List<DevicePresenceProjection>();
        coordinator.ProjectionChanged += projections.Add;

        await coordinator.RefreshNowAsync();
        await coordinator.RefreshNowAsync();
        await coordinator.RefreshNowAsync();
        await coordinator.RefreshNowAsync();

        CollectionAssert.AreEqual(
            new bool?[] { false, true, false, true },
            projections.Select(item => item.Devices.Single().Connected).ToArray());
        Assert.IsTrue(projections.All(item => item.IsAuthoritative));
    }

    [TestMethod]
    public async Task DuplicateCoreProjectionDoesNotEmitDuplicateEvent()
    {
        var source = new FakeDeviceService();
        source.Enqueue([Device("alpha", true)]);
        source.Enqueue([Device("alpha", true)]);
        await using var coordinator = new DevicePresenceCoordinator(
            source, TimeSpan.FromHours(1));
        var events = 0;
        coordinator.ProjectionChanged += _ => events++;

        await coordinator.RefreshNowAsync();
        await coordinator.RefreshNowAsync();

        Assert.AreEqual(1, events);
        Assert.AreEqual(2, source.ReadCalls);
    }

    [TestMethod]
    public async Task FailedOrRestartingCoreMarksRetainedDevicesUnknownUntilFreshRead()
    {
        var source = new FakeDeviceService();
        source.Enqueue([Device("alpha", true)]);
        source.Enqueue(new ApolloDeviceReadException("核心正在重启。"));
        source.Enqueue([Device("alpha", false)]);
        await using var coordinator = new DevicePresenceCoordinator(
            source, TimeSpan.FromHours(1));

        await coordinator.RefreshNowAsync();
        await coordinator.RefreshNowAsync();
        var stale = coordinator.Current!;
        Assert.IsFalse(stale.IsAuthoritative);
        Assert.IsNull(stale.Devices.Single().Connected);
        Assert.AreEqual("核心正在重启。", stale.Message);

        await coordinator.RefreshNowAsync();
        Assert.IsTrue(coordinator.Current!.IsAuthoritative);
        Assert.AreEqual(false, coordinator.Current.Devices.Single().Connected);
    }

    [TestMethod]
    public async Task CancellationStopsSinglePollLoopWithoutStartingAnotherReader()
    {
        var source = new FakeDeviceService();
        source.Repeat = [Device("alpha", false)];
        await using var coordinator = new DevicePresenceCoordinator(
            source, TimeSpan.FromMilliseconds(5));

        coordinator.Start();
        coordinator.Start();
        await Task.Delay(35);
        await coordinator.DisposeAsync();
        var callsAtDispose = source.ReadCalls;
        await Task.Delay(20);

        Assert.IsTrue(callsAtDispose >= 1);
        Assert.AreEqual(callsAtDispose, source.ReadCalls);
        Assert.AreEqual(1, source.MaxConcurrentReads);
    }

    [TestMethod]
    public async Task ViewModelUpdatesCardsInPlaceWithoutPresenceSortJitter()
    {
        var source = new FakeDeviceService();
        await using var coordinator = new DevicePresenceCoordinator(
            source, TimeSpan.FromHours(1));
        var viewModel = new DevicesViewModel(source, coordinator);
        viewModel.Apply(new DevicePresenceProjection(
            [Device("bravo", false), Device("alpha", true)], true, null));
        var alpha = viewModel.Items[0];
        var bravo = viewModel.Items[1];

        viewModel.Apply(new DevicePresenceProjection(
            [Device("bravo", true), Device("alpha", false)], true, null));

        Assert.AreSame(alpha, viewModel.Items[0]);
        Assert.AreSame(bravo, viewModel.Items[1]);
        Assert.AreEqual("离线", alpha.Status);
        Assert.AreEqual("在线", bravo.Status);
        Assert.AreEqual("alpha，离线", alpha.AccessibleName);
    }

    [TestMethod]
    public async Task UnknownAndCrossDeviceUpdatesRemainIsolated()
    {
        var source = new FakeDeviceService();
        await using var coordinator = new DevicePresenceCoordinator(
            source, TimeSpan.FromHours(1));
        var viewModel = new DevicesViewModel(source, coordinator);
        viewModel.Apply(new DevicePresenceProjection(
            [Device("alpha", true), Device("bravo", false)], true, null));
        var bravo = viewModel.Items.Single(item => item.Name == "bravo");

        viewModel.Apply(new DevicePresenceProjection(
            [Device("alpha", null), Device("bravo", false)], false, "状态读取失败"));

        Assert.AreEqual("状态未知", viewModel.Items.Single(item => item.Name == "alpha").Status);
        Assert.AreEqual("离线", bravo.Status);
        Assert.AreEqual("状态读取失败", viewModel.ErrorMessage);
        Assert.AreEqual(2, viewModel.Items.Count);
    }

    [TestMethod]
    public async Task FreshCoordinatorAfterHostRestartUsesOnlyFreshCoreProjection()
    {
        var oldSource = new FakeDeviceService();
        oldSource.Enqueue([Device("alpha", true)]);
        await using (var oldCoordinator = new DevicePresenceCoordinator(
                         oldSource, TimeSpan.FromHours(1)))
        {
            await oldCoordinator.RefreshNowAsync();
            Assert.AreEqual(true, oldCoordinator.Current!.Devices.Single().Connected);
        }

        var restartedSource = new FakeDeviceService();
        restartedSource.Enqueue([Device("alpha", false)]);
        await using var restartedCoordinator = new DevicePresenceCoordinator(
            restartedSource, TimeSpan.FromHours(1));
        await restartedCoordinator.RefreshNowAsync();

        Assert.AreEqual(false, restartedCoordinator.Current!.Devices.Single().Connected);
        Assert.IsTrue(restartedCoordinator.Current.IsAuthoritative);
    }

    [TestMethod]
    public void DevicePageHasAccessibleLivePresenceAndDispatcherProjection()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Pages", "DevicesPage.xaml"));
        var code = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Pages", "DevicesPage.xaml.cs"));

        StringAssert.Contains(xaml, "AutomationProperties.Name=\"{x:Bind AccessibleName, Mode=OneWay}\"");
        StringAssert.Contains(xaml, "AutomationProperties.HelpText=\"{x:Bind AccessibleHelpText, Mode=OneWay}\"");
        StringAssert.Contains(xaml, "AutomationProperties.LiveSetting=\"Polite\"");
        StringAssert.Contains(code, "DispatcherQueue.TryEnqueue(() => ViewModel.Apply(projection))");
        StringAssert.Contains(code, "当前状态：{device.Status}");
    }

    [TestMethod]
    public void CoreDeviceProjectionUsesCurrentRtspSessionUuidAuthority()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "nvhttp.cpp"));
        var start = source.IndexOf("nlohmann::json get_all_clients()", StringComparison.Ordinal);
        var end = source.IndexOf("namespace {", start, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start);
        var projection = source[start..end];

        StringAssert.Contains(projection, "rtsp_stream::get_all_session_uuids()");
        StringAssert.Contains(projection, "if (*it == named_cert->uuid)");
        StringAssert.Contains(projection, "named_cert_node[\"connected\"] = connected");
        Assert.IsFalse(projection.Contains("lastSeen", StringComparison.OrdinalIgnoreCase));
    }

    private static ApolloDevice Device(string name, bool? connected) => new()
    {
        Name = name,
        Uuid = name == "alpha"
            ? "00000000-0000-0000-0000-000000000001"
            : "00000000-0000-0000-0000-000000000002",
        AccessMode = "operate",
        Connected = connected
    };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CMakeLists.txt")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "Ligase.Desktop")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class FakeDeviceService : IApolloDeviceService
    {
        private readonly Queue<object> _reads = new();
        private int _activeReads;
        public IReadOnlyList<ApolloDevice>? Repeat { get; set; }
        public int ReadCalls { get; private set; }
        public int MaxConcurrentReads { get; private set; }

        public void Enqueue(IReadOnlyList<ApolloDevice> devices) => _reads.Enqueue(devices);
        public void Enqueue(Exception exception) => _reads.Enqueue(exception);

        public async Task<IReadOnlyList<ApolloDevice>> GetDevicesAsync(
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            var active = Interlocked.Increment(ref _activeReads);
            MaxConcurrentReads = Math.Max(MaxConcurrentReads, active);
            try
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                var value = _reads.Count > 0 ? _reads.Dequeue() : Repeat ?? [];
                if (value is Exception exception) throw exception;
                return (IReadOnlyList<ApolloDevice>)value;
            }
            finally
            {
                Interlocked.Decrement(ref _activeReads);
            }
        }

        public Task SetAccessModeAsync(
            string uuid,
            string mode,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(
            string uuid,
            bool endActiveSession,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
