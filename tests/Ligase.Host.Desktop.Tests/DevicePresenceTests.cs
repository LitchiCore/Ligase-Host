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
    public async Task PresenceAndSessionRemainOrthogonalAcrossRealSequence()
    {
        var source = new FakeDeviceService();
        source.Enqueue([Device("offline", "none")]);
        source.Enqueue([Device("online", "none")]);
        source.Enqueue([Device("online", "streaming")]);
        source.Enqueue([Device("online", "none")]);
        await using var coordinator = new DevicePresenceCoordinator(source, TimeSpan.FromHours(1));
        var projections = new List<DevicePresenceProjection>();
        coordinator.ProjectionChanged += projections.Add;

        for (var index = 0; index < 4; index++) await coordinator.RefreshNowAsync();

        CollectionAssert.AreEqual(
            new[] { "offline/none", "online/none", "online/streaming", "online/none" },
            projections.Select(value =>
                $"{value.Devices.Single().PresenceState}/{value.Devices.Single().SessionState}").ToArray());
    }

    [TestMethod]
    public async Task DuplicateProjectionDoesNotEmitAndReaderIsSingleFlight()
    {
        var source = new FakeDeviceService { Repeat = [Device("online", "none")] };
        await using var coordinator = new DevicePresenceCoordinator(source, TimeSpan.FromMilliseconds(5));
        var events = 0;
        coordinator.ProjectionChanged += _ => events++;
        coordinator.Start();
        coordinator.Start();
        await Task.Delay(35);
        await coordinator.DisposeAsync();

        Assert.AreEqual(1, events);
        Assert.AreEqual(1, source.MaxConcurrentReads);
    }

    [TestMethod]
    public async Task CoreFailureRetainsDevicesAsUnknownWithoutInventingSession()
    {
        var source = new FakeDeviceService();
        source.Enqueue([Device("online", "streaming")]);
        source.Enqueue(new ApolloDeviceReadException("核心正在重启。"));
        await using var coordinator = new DevicePresenceCoordinator(source, TimeSpan.FromHours(1));

        await coordinator.RefreshNowAsync();
        await coordinator.RefreshNowAsync();

        var value = coordinator.Current!;
        Assert.IsFalse(value.IsAuthoritative);
        Assert.AreEqual("unknown", value.Devices.Single().PresenceState);
        Assert.AreEqual("streaming", value.Devices.Single().SessionState);
        Assert.AreEqual("核心正在重启。", value.Message);
    }

    [TestMethod]
    public void ViewModelUpdatesSameCardWithoutSortJitterAndSeparatesSessionText()
    {
        var source = new FakeDeviceService();
        using var coordinator = new AsyncDisposableAdapter(
            new DevicePresenceCoordinator(source, TimeSpan.FromHours(1)));
        var viewModel = new DevicesViewModel(source, coordinator.Value);
        viewModel.Apply(new DevicePresenceProjection(
            [Device("online", "none", "bravo"), Device("offline", "none", "alpha")], true, null));
        var alpha = viewModel.Items[0];
        var bravo = viewModel.Items[1];

        viewModel.Apply(new DevicePresenceProjection(
            [Device("online", "observing", "bravo"), Device("online", "none", "alpha")], true, null));

        Assert.AreSame(alpha, viewModel.Items[0]);
        Assert.AreSame(bravo, viewModel.Items[1]);
        Assert.AreEqual("在线 · 无活动会话", alpha.Status);
        Assert.AreEqual("在线 · 正在观察", bravo.Status);
        Assert.AreEqual("alpha，在线 · 无活动会话", alpha.AccessibleName);
    }

    [TestMethod]
    public void CoreAndUiSourceUseHeartbeatNotRtspAsPresenceAuthority()
    {
        var root = RepositoryRoot();
        var native = File.ReadAllText(Path.Combine(root, "src", "nvhttp.cpp"));
        var model = File.ReadAllText(Path.Combine(root, "src", "Ligase.Core", "Models", "ApolloDevice.cs"));
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Ligase.Desktop", "Pages", "DevicesPage.xaml"));

        StringAssert.Contains(native, "record_presence(named_cert_p->uuid");
        StringAssert.Contains(native, "presenceState");
        StringAssert.Contains(native, "sessionState");
        StringAssert.Contains(native, "device-presence/heartbeat");
        Assert.IsFalse(model.Contains("Connected", StringComparison.Ordinal));
        StringAssert.Contains(xaml, "AutomationProperties.LiveSetting=\"Polite\"");
        StringAssert.Contains(xaml, "AccessibleHelpText");
    }

    private static ApolloDevice Device(
        string presence,
        string session,
        string name = "alpha") => new()
    {
        Name = name,
        Uuid = name == "alpha"
            ? "00000000-0000-0000-0000-000000000001"
            : "00000000-0000-0000-0000-000000000002",
        AccessMode = "operate",
        PresenceState = presence,
        SessionState = session,
        PresenceExpiresInMs = presence == "online" ? 15000 : presence == "offline" ? 0 : null
    };

    private static string RepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("LIGASE_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CMakeLists.txt"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("repositoryRootUnavailable");
    }

    private sealed class FakeDeviceService : IApolloDeviceService
    {
        private readonly Queue<object> _reads = new();
        private int _active;
        public IReadOnlyList<ApolloDevice>? Repeat { get; init; }
        public int MaxConcurrentReads { get; private set; }
        public void Enqueue(IReadOnlyList<ApolloDevice> value) => _reads.Enqueue(value);
        public void Enqueue(Exception value) => _reads.Enqueue(value);

        public async Task<IReadOnlyList<ApolloDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            MaxConcurrentReads = Math.Max(MaxConcurrentReads, active);
            try
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                var value = _reads.Count > 0 ? _reads.Dequeue() : Repeat ?? [];
                if (value is Exception exception) throw exception;
                return (IReadOnlyList<ApolloDevice>)value;
            }
            finally { Interlocked.Decrement(ref _active); }
        }

        public Task SetAccessModeAsync(string uuid, string mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string uuid, bool endActiveSession, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class AsyncDisposableAdapter(DevicePresenceCoordinator value) : IDisposable
    {
        public DevicePresenceCoordinator Value { get; } = value;
        public void Dispose() => Value.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
