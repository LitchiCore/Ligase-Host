using System.Text;
using Ligase.Host.Core.Application.LayoutCatalog;
using Ligase.Host.Core.Domain.LayoutCatalog;
using Ligase.Host.Core.Infrastructure.Storage;
using Ligase.Host.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests.LayoutCatalog;

[TestClass]
public sealed class LayoutCatalogRepositoryTests
{
    private string _root = null!;
    private string _file = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "Ligase.Host.LayoutCatalog.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _file = Path.Combine(_root, "layout-catalog.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [TestMethod]
    public async Task SaveAndLoadUseCanonicalOrderingWithoutContentPayload()
    {
        var first = Descriptor(
            "20000000-0000-0000-0000-000000000000",
            2,
            "published",
            "40000000-0000-0000-0000-000000000000");
        var second = Descriptor(
            "10000000-0000-0000-0000-000000000000",
            1,
            "draft",
            "30000000-0000-0000-0000-000000000000");
        var repository = new JsonLayoutCatalogRepository(_file);

        await repository.SaveAsync(new LayoutCatalogSnapshot(
            1,
            [first, second],
            [Binding(second.LayoutId, second.Revision)]));
        var loaded = await repository.LoadAsync();
        var json = await File.ReadAllTextAsync(_file);

        Assert.AreEqual(second.LayoutId, loaded.InstalledRevisions[0].LayoutId);
        Assert.IsTrue(json.IndexOf(second.LayoutId, StringComparison.Ordinal) <
                      json.IndexOf(first.LayoutId, StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("content", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("payload", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void UnknownFieldFailsClosedWithStableJsonPointer()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "installedRevisions": [],
              "explicitBindings": [],
              "z": true,
              "a/b~": true
            }
            """;

        var exception = Assert.ThrowsException<LayoutCatalogException>(
            () => JsonLayoutCatalogRepository.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.AreEqual(LayoutCatalogCodes.UnknownField, exception.Code);
        Assert.AreEqual("/a~1b~0", exception.Detail);
    }

    [TestMethod]
    public void FutureSchemaFailsClosed()
    {
        const string json =
            """{"schemaVersion":2,"installedRevisions":[],"explicitBindings":[]}""";

        var exception = Assert.ThrowsException<LayoutCatalogException>(
            () => JsonLayoutCatalogRepository.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.AreEqual(LayoutCatalogCodes.InvalidSchema, exception.Code);
    }

    [TestMethod]
    public void DuplicateJsonPropertyFailsClosed()
    {
        const string json =
            """{"schemaVersion":1,"schemaVersion":1,"installedRevisions":[],"explicitBindings":[]}""";

        var exception = Assert.ThrowsException<LayoutCatalogException>(
            () => JsonLayoutCatalogRepository.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.AreEqual(LayoutCatalogCodes.InvalidSchema, exception.Code);
    }

    [TestMethod]
    public void DuplicateRevisionFailsClosed()
    {
        var descriptor = Descriptor(
            "10000000-0000-0000-0000-000000000000",
            1,
            "published",
            "30000000-0000-0000-0000-000000000000");

        var exception = Assert.ThrowsException<LayoutCatalogException>(() =>
            LayoutCatalogValidator.Validate(new LayoutCatalogSnapshot(
                1,
                [descriptor, descriptor],
                [])));

        Assert.AreEqual(LayoutCatalogCodes.DuplicateRevision, exception.Code);
    }

    [TestMethod]
    public void RevisionMustBeSafeIntegerToken()
    {
        var json = """
            {
              "schemaVersion":1,
              "installedRevisions":[{
                "schemaVersion":1,
                "layoutId":"10000000-0000-0000-0000-000000000000",
                "revision":1.0,
                "portableIdentities":[],
                "compatibility":{"minClientContractVersion":1,"minLayoutRuntimeVersion":1},
                "publicationStatus":"draft",
                "variants":[{
                  "variantId":"30000000-0000-0000-0000-000000000000",
                  "inputProfile":"touch",
                  "deviceClasses":["phone"],
                  "orientations":["landscape"]
                }]
              }],
              "explicitBindings":[]
            }
            """;

        var exception = Assert.ThrowsException<LayoutCatalogException>(
            () => JsonLayoutCatalogRepository.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.AreEqual(LayoutCatalogCodes.InvalidSchema, exception.Code);
    }

    [TestMethod]
    public async Task QueryReportsAvailableRetiredAndMissingBindings()
    {
        var available = Descriptor(
            "10000000-0000-0000-0000-000000000000",
            1,
            "published",
            "30000000-0000-0000-0000-000000000000");
        var retired = Descriptor(
            "20000000-0000-0000-0000-000000000000",
            2,
            "retired",
            "40000000-0000-0000-0000-000000000000");
        var repository = new MemoryRepository(new LayoutCatalogSnapshot(
            1,
            [available, retired],
            [
                Binding(available.LayoutId, available.Revision, app: "60000000-0000-0000-0000-000000000000"),
                Binding(retired.LayoutId, retired.Revision, app: "70000000-0000-0000-0000-000000000000"),
                Binding("80000000-0000-0000-0000-000000000000", 1, app: "90000000-0000-0000-0000-000000000000")
            ]));

        var overview = await new LayoutCatalogService(repository).QueryAsync();

        CollectionAssert.AreEqual(
            new[]
            {
                LayoutCatalogBindingStatuses.Available,
                LayoutCatalogBindingStatuses.Retired,
                LayoutCatalogBindingStatuses.Missing
            },
            overview.ExplicitBindings.Select(binding => binding.Status).ToArray());
    }

    [TestMethod]
    public async Task FailedReadBackRestoresPreviousCatalog()
    {
        var repository = new JsonLayoutCatalogRepository(_file);
        var original = new LayoutCatalogSnapshot(
            1,
            [Descriptor(
                "10000000-0000-0000-0000-000000000000",
                1,
                "published",
                "30000000-0000-0000-0000-000000000000")],
            []);
        await repository.SaveAsync(original);
        var originalBytes = await File.ReadAllBytesAsync(_file);
        var failing = new JsonLayoutCatalogRepository(
            _file,
            _ => throw new IOException("injected read-back failure"));

        var exception = await Assert.ThrowsExceptionAsync<LayoutCatalogException>(
            () => failing.SaveAsync(new LayoutCatalogSnapshot(
                1,
                [Descriptor(
                    "20000000-0000-0000-0000-000000000000",
                    1,
                    "draft",
                    "40000000-0000-0000-0000-000000000000")],
                [])));

        Assert.AreEqual(LayoutCatalogCodes.AtomicWriteFailed, exception.Code);
        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(_file));
        Assert.IsFalse(File.Exists(_file + ".tmp"));
        Assert.IsFalse(File.Exists(_file + ".bak"));
    }

    private static LayoutDescriptorV1 Descriptor(
        string layoutId,
        long revision,
        string status,
        string variantId) =>
        new(
            1,
            layoutId,
            revision,
            [new PortableGameIdentityV1("steam", "3548580")],
            new LayoutCompatibilityV1(1, 1),
            status,
            [
                new LayoutVariantV1(
                    variantId,
                    "touch",
                    ["tablet", "phone"],
                    ["portrait", "landscape"])
            ]);

    private static LayoutCatalogBinding Binding(
        string layoutId,
        long revision,
        string app = "50000000-0000-0000-0000-000000000000") =>
        new(
            new LayoutCatalogInstanceIdentity(
                "60000000-0000-0000-0000-000000000000",
                app),
            new LayoutBindingV1(layoutId, revision));

    private sealed class MemoryRepository(LayoutCatalogSnapshot snapshot)
        : ILayoutCatalogRepository
    {
        public Task<LayoutCatalogSnapshot> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task SaveAsync(
            LayoutCatalogSnapshot value,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
