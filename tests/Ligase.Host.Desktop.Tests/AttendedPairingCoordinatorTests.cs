using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Ligase.Host.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class AttendedPairingCoordinatorTests
{
    private static readonly ManagedPairingCore Core = new(
        LigaseEndpoint.Create(
            LigaseEndpointScheme.Http,
            "127.0.0.1",
            48989,
            source: LigaseEndpointSource.Loopback),
        "53beb7ec-9788-4c23-861a-061f153029a5",
        "start-a",
        48989);

    [TestMethod]
    public async Task ReadyTransitionEmitsOnceAndCoreSwitchRevokesOldRequest()
    {
        var pending = Request(ready: false);
        var repository = new QueueRepository(
            Projection(Core, pending),
            Projection(Core, pending with { ReadyForApproval = true }),
            Projection(Core, pending with { ReadyForApproval = true }),
            Projection(Core with { StartNonce = "start-b" }));
        await using var coordinator = new AttendedPairingCoordinator(repository);
        var ready = new List<AttendedPairingReadyEvent>();
        var removed = new List<AttendedPairingRemovedEvent>();
        coordinator.ReadyForApproval += ready.Add;
        coordinator.RequestRemoved += removed.Add;

        await coordinator.RefreshNowAsync();
        await coordinator.RefreshNowAsync();
        await coordinator.RefreshNowAsync();
        await coordinator.RefreshNowAsync();

        Assert.AreEqual(1, ready.Count);
        Assert.AreEqual(pending.RequestId, ready[0].Request.RequestId);
        Assert.AreEqual(1, removed.Count);
        Assert.AreEqual(pending.RequestId, removed[0].RequestId);
    }

    [TestMethod]
    public async Task ActionIsSingleFlightAndUsesCurrentManagedProjection()
    {
        var completion = new TaskCompletionSource<PairingRequestStatus>();
        var repository = new BlockingRepository(
            Projection(Core, Request(ready: true)),
            completion);
        await using var coordinator = new AttendedPairingCoordinator(repository);
        await coordinator.RefreshNowAsync();

        var first = coordinator.AllowAsync(Request().RequestId);
        await Assert.ThrowsExceptionAsync<AttendedPairingUnavailableException>(
            () => coordinator.RejectAsync(Request().RequestId));
        completion.SetResult(new PairingRequestStatus(
            Request().RequestId,
            "approved",
            DateTimeOffset.UtcNow.AddMinutes(1),
            null));
        await first;

        Assert.AreEqual(1, repository.AllowCount);
        Assert.AreEqual(0, repository.RejectCount);
    }

    [TestMethod]
    public async Task DifferentRequestsCanBeHandledIndependently()
    {
        var secondRequest = Request() with
        {
            RequestId = "a82164ed-573d-4cb3-a2b7-d4b22de80f71"
        };
        var repository = new PerRequestBlockingRepository(
            Projection(Core, Request(ready: true), secondRequest with
            {
                ReadyForApproval = true
            }));
        await using var coordinator = new AttendedPairingCoordinator(repository);
        await coordinator.RefreshNowAsync();

        var first = coordinator.AllowAsync(Request().RequestId);
        var second = coordinator.RejectAsync(secondRequest.RequestId);
        await repository.WaitForBothAsync();

        repository.Complete();
        await Task.WhenAll(first, second);

        CollectionAssert.AreEquivalent(
            new[] { Request().RequestId, secondRequest.RequestId },
            repository.RequestIds.ToArray());
    }

    private static PendingPairingRequest Request(bool ready = false) => new(
        "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4",
        new PendingPairingDevice("Phone", "android"),
        "pending",
        ready,
        "ABCD-2345",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddMinutes(2),
        new PairingSourceAddress("192.168.1.2", null));

    private static AttendedPairingProjection Projection(
        ManagedPairingCore core,
        params PendingPairingRequest[] requests) =>
        new(core, requests, DateTimeOffset.UtcNow);

    private sealed class QueueRepository(
        params AttendedPairingProjection[] projections)
        : IAttendedPairingRepository
    {
        private readonly Queue<AttendedPairingProjection> _values =
            new(projections);

        public Task<AttendedPairingProjection> GetPendingAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.Dequeue());

        public Task<PairingRequestStatus> AllowAsync(
            ManagedPairingCore core,
            string requestId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PairingRequestStatus> RejectAsync(
            ManagedPairingCore core,
            string requestId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingRepository(
        AttendedPairingProjection projection,
        TaskCompletionSource<PairingRequestStatus> completion)
        : IAttendedPairingRepository
    {
        public int AllowCount { get; private set; }
        public int RejectCount { get; private set; }

        public Task<AttendedPairingProjection> GetPendingAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(projection);

        public async Task<PairingRequestStatus> AllowAsync(
            ManagedPairingCore core,
            string requestId,
            CancellationToken cancellationToken = default)
        {
            AllowCount++;
            return await completion.Task.WaitAsync(cancellationToken);
        }

        public Task<PairingRequestStatus> RejectAsync(
            ManagedPairingCore core,
            string requestId,
            CancellationToken cancellationToken = default)
        {
            RejectCount++;
            throw new NotSupportedException();
        }
    }

    private sealed class PerRequestBlockingRepository(
        AttendedPairingProjection projection) : IAttendedPairingRepository
    {
        private readonly TaskCompletionSource _bothStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;

        public List<string> RequestIds { get; } = [];

        public Task<AttendedPairingProjection> GetPendingAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(projection);

        public Task<PairingRequestStatus> AllowAsync(
            ManagedPairingCore core,
            string requestId,
            CancellationToken cancellationToken = default) =>
            CompleteAsync(requestId, "approved", cancellationToken);

        public Task<PairingRequestStatus> RejectAsync(
            ManagedPairingCore core,
            string requestId,
            CancellationToken cancellationToken = default) =>
            CompleteAsync(requestId, "rejected", cancellationToken);

        public Task WaitForBothAsync() => _bothStarted.Task;

        public void Complete() => _release.SetResult();

        private async Task<PairingRequestStatus> CompleteAsync(
            string requestId,
            string state,
            CancellationToken cancellationToken)
        {
            lock (RequestIds) RequestIds.Add(requestId);
            if (Interlocked.Increment(ref _started) == 2)
                _bothStarted.SetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new PairingRequestStatus(
                requestId,
                state,
                DateTimeOffset.UtcNow.AddMinutes(1),
                null);
        }
    }
}
