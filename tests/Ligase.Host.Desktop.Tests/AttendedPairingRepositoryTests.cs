using System.Net;
using System.Text;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class AttendedPairingRepositoryTests
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
    public async Task PendingListUsesOnlyManagedLoopbackProjection()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.AreEqual(
                "http://127.0.0.1:48989/ligase/v1/pairing/requests",
                request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Json(HttpStatusCode.OK,
                """
                {"version":1,"requests":[{"requestId":"9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4","device":{"name":"Phone","platform":"android"},"state":"pending","readyForApproval":true,"safetyCode":"ABCD-2345","createdAt":"2026-07-24T00:00:00Z","expiresAt":"2026-07-24T00:02:00Z","sourceAddress":{"address":"192.168.1.2"}}]}
                """));
        });
        var repository = new AttendedPairingRepository(
            new StaticResolver(Core),
            new HttpClient(handler));

        var projection = await repository.GetPendingAsync();

        Assert.AreEqual(Core.InstanceKey, projection.Core.InstanceKey);
        Assert.AreEqual(1, projection.Requests.Count);
        Assert.AreEqual("ABCD-2345", projection.Requests[0].SafetyCode);
    }

    [TestMethod]
    public async Task ActionFailsClosedWhenManagedInstanceChanges()
    {
        var changed = Core with { StartNonce = "start-b" };
        var repository = new AttendedPairingRepository(
            new StaticResolver(changed),
            new HttpClient(new RecordingHandler((_, _) =>
                Task.FromException<HttpResponseMessage>(
                    new AssertFailedException("HTTP must not be called")))));

        await Assert.ThrowsExceptionAsync<AttendedPairingUnavailableException>(
            () => repository.AllowAsync(
                Core,
                "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4"));
    }

    [TestMethod]
    public async Task AllowUsesEmptyLoopbackPostAndParsesFrozenStatus()
    {
        var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual(
                "/ligase/v1/pairing/requests/9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4/allow",
                request.RequestUri!.AbsolutePath);
            Assert.AreEqual(
                0,
                (await request.Content!.ReadAsByteArrayAsync(cancellationToken)).Length);
            return Json(HttpStatusCode.Accepted,
                """
                {"requestId":"9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4","state":"approved","expiresAt":"2026-07-24T00:02:00Z","failure":null}
                """);
        });
        var repository = new AttendedPairingRepository(
            new StaticResolver(Core),
            new HttpClient(handler));

        var status = await repository.AllowAsync(
            Core,
            "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4");

        Assert.AreEqual("approved", status.State);
    }

    [TestMethod]
    public async Task AccessSelectionUsesCanonicalRequestAndClosedJsonBody()
    {
        var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            Assert.AreEqual(HttpMethod.Put, request.Method);
            Assert.AreEqual(
                "/ligase/v1/pairing/requests/9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4/access",
                request.RequestUri!.AbsolutePath);
            Assert.AreEqual(
                """{"mode":"observe"}""",
                await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var repository = new AttendedPairingRepository(
            new StaticResolver(Core),
            new HttpClient(handler));

        await repository.SetAccessModeAsync(
            Core,
            "9DBBB480-9EF1-4E9E-BB1F-0C1D42DFF8E4",
            "observe");
    }

    [TestMethod]
    public async Task AccessSelectionFailsClosedWhenManagedInstanceChanges()
    {
        var changed = Core with { StartNonce = "start-b" };
        var repository = new AttendedPairingRepository(
            new StaticResolver(changed),
            new HttpClient(new RecordingHandler((_, _) =>
                Task.FromException<HttpResponseMessage>(
                    new AssertFailedException("HTTP must not be called")))));

        await Assert.ThrowsExceptionAsync<AttendedPairingUnavailableException>(
            () => repository.SetAccessModeAsync(
                Core,
                "9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4",
                "observe"));
    }

    [DataTestMethod]
    [DataRow(" Phone", "192.168.1.2", null)]
    [DataRow("Phone", "192.168.001.2", null)]
    [DataRow("Phone", "fe80::1", null)]
    [DataRow("Phone", "2001:DB8::1", null)]
    [DataRow("Phone", "2001:db8::1", 4u)]
    public async Task PendingListRejectsNonCanonicalDeviceOrSource(
        string name,
        string address,
        uint? scopeId)
    {
        var source = scopeId is null
            ? $$"""{"address":"{{address}}"}"""
            : $$"""{"address":"{{address}}","scopeId":{{scopeId}}}""";
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK,
                $$"""
                {"version":1,"requests":[{"requestId":"9dbbb480-9ef1-4e9e-bb1f-0c1d42dff8e4","device":{"name":"{{name}}","platform":"android"},"state":"pending","readyForApproval":false,"safetyCode":"ABCD-2345","createdAt":"2026-07-24T00:00:00Z","expiresAt":"2026-07-24T00:02:00Z","sourceAddress":{{source}}}]}
                """)));
        var repository = new AttendedPairingRepository(
            new StaticResolver(Core),
            new HttpClient(handler));

        await Assert.ThrowsExceptionAsync<AttendedPairingUnavailableException>(
            () => repository.GetPendingAsync());
    }

    private static HttpResponseMessage Json(
        HttpStatusCode status,
        string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StaticResolver(ManagedPairingCore value)
        : IManagedPairingCoreResolver
    {
        public Task<ManagedPairingCore> ResolveAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(value);
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>
        callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            callback(request, cancellationToken);
    }
}
