using System.Net;
using System.Text;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class CoverArtServiceTests
{
    [TestMethod]
    public async Task SearchAsync_ReturnsPrefixMatchesInDatabaseOrder()
    {
        using var fixture = new Fixture(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/buckets/ch.json"))
                return Json("""{"2":{"name":"Chess"},"1":{"name":"Chill Story"},"3":{"name":"Chill Zone"}}""");
            var id = Path.GetFileNameWithoutExtension(request.RequestUri.AbsolutePath);
            return Json(
                "{\"name\":\"Game " + id +
                "\",\"cover\":{\"url\":\"//images.igdb.com/igdb/image/upload/t_thumb/cover_" +
                id + ".jpg\"}}");
        });

        var results = await fixture.Service.SearchAsync("Chill");

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual("Game 1", results[0].Name);
        Assert.AreEqual("igdb_1", results[0].Key);
    }

    [TestMethod]
    public async Task DownloadAsync_ValidatesPngAndStoresUnderManagedRoot()
    {
        var png = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00
        };
        using var fixture = new Fixture(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(png)
        });
        var candidate = new CoverCandidate(
            "Example",
            "igdb_42",
            "https://images.igdb.com/preview.jpg",
            "https://images.igdb.com/cover.png");

        var path = await fixture.Service.DownloadAsync(candidate);

        Assert.AreEqual(Path.Combine(fixture.Paths.CoversDirectory, "igdb_42.png"), path);
        CollectionAssert.AreEqual(png, await File.ReadAllBytesAsync(path));
    }

    [TestMethod]
    public async Task DownloadAsync_RejectsUntrustedImageHost()
    {
        using var fixture = new Fixture(_ => throw new AssertFailedException("HTTP must not run"));
        var candidate = new CoverCandidate(
            "Example",
            "bad",
            "https://example.com/preview.jpg",
            "https://example.com/cover.png");

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => fixture.Service.DownloadAsync(candidate));
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "ligase-cover-tests", Guid.NewGuid().ToString("N"));

        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            Paths = new LigasePaths(_root);
            Service = new CoverArtService(
                new HttpClient(new Handler(response)),
                Paths);
        }

        public LigasePaths Paths { get; }
        public CoverArtService Service { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
