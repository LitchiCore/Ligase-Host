using System.Net;
using System.Text;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

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
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        using var fixture = new Fixture(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Png(png)
        });
        var candidate = new CoverCandidate(
            "Example",
            "igdb_42",
            "https://images.igdb.com/preview.jpg",
            "https://images.igdb.com/cover.png");

        var path = await fixture.Service.DownloadAsync(candidate);

        StringAssert.StartsWith(
            path,
            Path.Combine(fixture.Paths.CoversDirectory, "igdb_42_"));
        CollectionAssert.AreEqual(png, await File.ReadAllBytesAsync(path));
        var authority = CoverArtService.TryReadAuthority(fixture.Paths, path);
        Assert.IsNotNull(authority);
        Assert.AreEqual("gameDbIgdb", authority.SourceKind);

        await File.WriteAllBytesAsync(path, [0x89, 0x50, 0x4e, 0x47]);
        Assert.IsNull(CoverArtService.TryReadAuthority(fixture.Paths, path));
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

    [TestMethod]
    public async Task DownloadAsync_RejectsHtmlEvenWhenBodyStartsWithPngSignature()
    {
        var content = new ByteArrayContent(
            [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x3c, 0x68, 0x74, 0x6d, 0x6c]);
        content.Headers.ContentType = new("text/html");
        using var fixture = new Fixture(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        });
        var candidate = new CoverCandidate(
            "Example", "html", "https://images.igdb.com/preview.jpg",
            "https://images.igdb.com/cover.png");

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => fixture.Service.DownloadAsync(candidate));
        Assert.IsFalse(Directory.Exists(fixture.Paths.CoversDirectory));
    }

    [TestMethod]
    public async Task PrepareAsyncRetainsNoWriteLeaseAndRollbackDeletesOnlyCreatedArtifact()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        using var fixture = new Fixture(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Png(png)
        });
        var candidate = new CoverCandidate(
            "Example", "retained", "https://images.igdb.com/preview.jpg",
            "https://images.igdb.com/cover.png");

        var prepared = await fixture.Service.PrepareAsync(candidate);

        await Assert.ThrowsExceptionAsync<IOException>(() =>
            File.WriteAllBytesAsync(prepared.Path, [1, 2, 3]));
        await fixture.Service.RollbackAsync(prepared);
        Assert.IsFalse(File.Exists(prepared.Path));
    }

    [TestMethod]
    public async Task PrepareAsyncDoesNotClaimSameBytesWithoutOwnedAuthority()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        using var fixture = new Fixture(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Png(png)
        });
        var candidate = new CoverCandidate(
            "Example", "foreign", "https://images.igdb.com/preview.jpg",
            "https://images.igdb.com/cover.png");
        var path = await fixture.Service.DownloadAsync(candidate);
        File.Delete(fixture.Paths.CoverCacheAuthorityFile);
        var before = await File.ReadAllBytesAsync(path);

        var error = await Assert.ThrowsExceptionAsync<ExistingItemCoverUpdateException>(() =>
            fixture.Service.PrepareAsync(candidate));

        Assert.AreEqual("coverDestinationCollision", error.Code);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path));
    }

    [TestMethod]
    public async Task PruneUnreferencedAsync_DeletesOnlyOwnedCachedCover()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        using var fixture = new Fixture(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Png(png)
        });
        var candidate = new CoverCandidate(
            "Example", "igdb_42", "https://images.igdb.com/preview.jpg",
            "https://images.igdb.com/cover.png");
        var path = await fixture.Service.DownloadAsync(candidate);
        var foreign = Path.Combine(fixture.Paths.RootDirectory, "foreign.png");
        await File.WriteAllBytesAsync(foreign, png);

        await fixture.Service.PruneUnreferencedAsync([]);

        Assert.IsFalse(File.Exists(path));
        Assert.IsTrue(File.Exists(foreign));
    }

    [TestMethod]
    public async Task FindSteamAsync_BindsExactManifestAppIdAndClientCachePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "ligase-steam-cover-tests", Guid.NewGuid().ToString("N"));
        var steam = Path.Combine(root, "Steam");
        var library = Path.Combine(root, "Library");
        var manifest = Path.Combine(library, "steamapps", "appmanifest_3548580.acf");
        var cover = Path.Combine(steam, "appcache", "librarycache", "3548580", "library_600x900.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        Directory.CreateDirectory(Path.GetDirectoryName(cover)!);
        File.WriteAllText(manifest,
            "\"AppState\"\n{\n\"appid\" \"3548580\"\n\"name\" \"Chill\"\n\"installdir\" \"Chill\"\n}");
        File.WriteAllBytes(cover, await MakeJpegAsync(600, 900));
        try
        {
            var service = new CoverArtService(
                new HttpClient(new Handler(_ => throw new AssertFailedException("HTTP must not run"))),
                new LigasePaths(Path.Combine(root, "Data")),
                new FixedSteamLocator(steam));
            var game = new SteamGame(3548580, "Chill", "Chill", library, manifest, 1);

            var candidates = await service.FindSteamAsync(game);

            Assert.AreEqual(1, candidates.Count);
            Assert.AreEqual((uint)3548580, candidates[0].SteamAppId);
            Assert.AreEqual("steamClientLibraryCache", candidates[0].SourceKind);
            Assert.IsTrue(new Uri(candidates[0].DownloadUrl).IsFile);
            var cached = await service.DownloadAsync(candidates[0]);
            var cachedBytes = await File.ReadAllBytesAsync(cached);
            CollectionAssert.AreEqual(
                new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a },
                cachedBytes[..8]);
            Assert.AreEqual((uint)3548580,
                CoverArtService.TryReadAuthority(new LigasePaths(Path.Combine(root, "Data")), cached)!.SteamAppId);

            var spoofed = game with { AppId = 3548581 };
            Assert.AreEqual(0, (await service.FindSteamAsync(spoofed)).Count);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task FindSteamAsync_AcceptsExactLocalizedHalfSizeClientCapsule()
    {
        var root = Path.Combine(Path.GetTempPath(), "ligase-steam-cover-tests", Guid.NewGuid().ToString("N"));
        var steam = Path.Combine(root, "Steam");
        var library = Path.Combine(root, "Library");
        var manifest = Path.Combine(library, "steamapps", "appmanifest_3548580.acf");
        var cover = Path.Combine(steam, "appcache", "librarycache", "3548580",
            "1b5ad8544076c2db8ba8810f79737575fef28191", "library_capsule_schinese.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        Directory.CreateDirectory(Path.GetDirectoryName(cover)!);
        File.WriteAllText(manifest,
            "\"AppState\"\n{\n\"appid\" \"3548580\"\n\"name\" \"Chill\"\n" +
            "\"installdir\" \"Chill\"\n\"UserConfig\" { \"language\" \"schinese\" }\n}");
        File.WriteAllBytes(cover, await MakeJpegAsync(300, 450));
        try
        {
            var paths = new LigasePaths(Path.Combine(root, "Data"));
            var service = new CoverArtService(
                new HttpClient(new Handler(_ => throw new AssertFailedException("HTTP must not run"))),
                paths,
                new FixedSteamLocator(steam));
            var game = new SteamGame(3548580, "Chill", "Chill", library, manifest, 1);

            var candidates = await service.FindSteamAsync(game);

            Assert.AreEqual(1, candidates.Count);
            Assert.AreEqual(Path.GetFullPath(cover), new Uri(candidates[0].DownloadUrl).LocalPath);
            var cached = await service.DownloadAsync(candidates[0]);
            Assert.AreEqual((uint)3548580, CoverArtService.TryReadAuthority(paths, cached)!.SteamAppId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task FindSteamAsync_RejectsWrongLanguageAndAmbiguousLocalizedCapsules()
    {
        var root = Path.Combine(Path.GetTempPath(), "ligase-steam-cover-tests", Guid.NewGuid().ToString("N"));
        var steam = Path.Combine(root, "Steam");
        var library = Path.Combine(root, "Library");
        var manifest = Path.Combine(library, "steamapps", "appmanifest_3548580.acf");
        var appCache = Path.Combine(steam, "appcache", "librarycache", "3548580");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllText(manifest,
            "\"AppState\"\n{\n\"appid\" \"3548580\"\n\"name\" \"Chill\"\n" +
            "\"installdir\" \"Chill\"\n\"UserConfig\" { \"language\" \"schinese\" }\n}");
        var wrongLanguage = Path.Combine(appCache,
            "1b5ad8544076c2db8ba8810f79737575fef28191", "library_capsule_english.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(wrongLanguage)!);
        File.WriteAllBytes(wrongLanguage, await MakeJpegAsync(300, 450));
        try
        {
            var service = new CoverArtService(
                new HttpClient(new Handler(_ => throw new AssertFailedException("HTTP must not run"))),
                new LigasePaths(Path.Combine(root, "Data")),
                new FixedSteamLocator(steam));
            var game = new SteamGame(3548580, "Chill", "Chill", library, manifest, 1);

            Assert.AreEqual(0, (await service.FindSteamAsync(game)).Count);

            foreach (var cacheKey in new[]
                     {
                         "2b5ad8544076c2db8ba8810f79737575fef28191",
                         "3b5ad8544076c2db8ba8810f79737575fef28191"
                     })
            {
                var duplicate = Path.Combine(appCache, cacheKey, "library_capsule_schinese.jpg");
                Directory.CreateDirectory(Path.GetDirectoryName(duplicate)!);
                File.WriteAllBytes(duplicate, await MakeJpegAsync(300, 450));
            }

            Assert.AreEqual(0, (await service.FindSteamAsync(game)).Count);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task DownloadAsync_RejectsLocalizedCapsuleWithWrongDimensions()
    {
        var root = Path.Combine(Path.GetTempPath(), "ligase-steam-cover-tests", Guid.NewGuid().ToString("N"));
        var steam = Path.Combine(root, "Steam");
        var library = Path.Combine(root, "Library");
        var manifest = Path.Combine(library, "steamapps", "appmanifest_3548580.acf");
        var cover = Path.Combine(steam, "appcache", "librarycache", "3548580",
            "1b5ad8544076c2db8ba8810f79737575fef28191", "library_capsule_schinese.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        Directory.CreateDirectory(Path.GetDirectoryName(cover)!);
        File.WriteAllText(manifest,
            "\"AppState\"\n{\n\"appid\" \"3548580\"\n\"name\" \"Chill\"\n" +
            "\"installdir\" \"Chill\"\n\"UserConfig\" { \"language\" \"schinese\" }\n}");
        File.WriteAllBytes(cover, await MakeJpegAsync(300, 449));
        try
        {
            var service = new CoverArtService(
                new HttpClient(new Handler(_ => throw new AssertFailedException("HTTP must not run"))),
                new LigasePaths(Path.Combine(root, "Data")),
                new FixedSteamLocator(steam));
            var game = new SteamGame(3548580, "Chill", "Chill", library, manifest, 1);
            var candidate = (await service.FindSteamAsync(game)).Single();

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => service.DownloadAsync(candidate));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static ByteArrayContent Png(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("image/png");
        return content;
    }

    private static async Task<byte[]> MakeJpegAsync(uint width, uint height)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        var pixels = new byte[checked((int)(width * height * 4))];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 0x44;
            pixels[index + 1] = 0x88;
            pixels[index + 2] = 0xcc;
            pixels[index + 3] = 0xff;
        }
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            width,
            height,
            96,
            96,
            pixels);
        await encoder.FlushAsync();
        var bytes = new byte[checked((int)stream.Size)];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)bytes.Length);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "ligase-cover-tests", Guid.NewGuid().ToString("N"));

        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            Paths = new LigasePaths(_root);
            Service = new CoverArtService(
                new HttpClient(new Handler(response)),
                Paths,
                new FixedSteamLocator(null));
        }

        public LigasePaths Paths { get; }
        public CoverArtService Service { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private sealed class FixedSteamLocator(string? path) : ISteamInstallationLocator
    {
        public string? FindSteamPath() => path;
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
