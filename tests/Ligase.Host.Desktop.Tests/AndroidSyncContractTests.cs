using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class AndroidSyncContractTests
{
    private const string AppUuid = "2c42a3d0-79f1-4bb6-98f8-40c18cd5bc91";
    private const string LayoutUuid = "0b7cd40f-64ae-4eac-845a-fb41dfed80d0";
    private const string CoverSha = "431ced6916a2a21a156e38701afe55bbd7f88969fbbfc56d7fe099d47f265460";

    [TestMethod]
    public void PortableIdentityHasOneObjectShapeAndMatchesSteamId()
    {
        AndroidSyncContractV1Validator.Validate(Document(Item()));

        var leadingZero = Item(identity: new("steam", "0123"));
        Assert.ThrowsException<InvalidDataException>(() =>
            AndroidSyncContractV1Validator.Validate(Document(leadingZero)));

        var mismatch = Item(identity: new("steam", "124"));
        Assert.ThrowsException<InvalidDataException>(() =>
            AndroidSyncContractV1Validator.Validate(Document(mismatch)));

        var nonSteam = Item(
            kind: LibraryItemKind.Executable,
            steamAppId: null,
            identity: new("steam", "123"));
        Assert.ThrowsException<InvalidDataException>(() =>
            AndroidSyncContractV1Validator.Validate(Document(nonSteam)));
    }

    [TestMethod]
    public void LayoutBindingIsCanonicalSafeAndNullableByAbsence()
    {
        var absent = Item(includeLayoutBinding: false);
        AndroidSyncContractV1Validator.Validate(Document(absent));

        foreach (var binding in new[]
                 {
                     new LayoutBindingV1(LayoutUuid.ToUpperInvariant(), 7),
                     new LayoutBindingV1(LayoutUuid, 0),
                     new LayoutBindingV1(LayoutUuid, 9_007_199_254_740_992)
                 })
        {
            Assert.ThrowsException<InvalidDataException>(() =>
                AndroidSyncContractV1Validator.Validate(Document(Item(layoutBinding: binding))));
        }
    }

    [TestMethod]
    public void CoverAuthorityIsAllOrNothingAndProviderCorrelated()
    {
        AndroidSyncContractV1Validator.Validate(Document(Item()));

        var partial = Item(coverSourceId: null);
        Assert.ThrowsException<InvalidDataException>(() =>
            AndroidSyncContractV1Validator.Validate(Document(partial)));

        var mismatch = Item(coverSourceId: "124");
        Assert.ThrowsException<InvalidDataException>(() =>
            AndroidSyncContractV1Validator.Validate(Document(mismatch)));
    }

    [TestMethod]
    public void SchemaVectorsAndNativeProjectionAreFixedInputs()
    {
        var root = RepositoryRoot();
        var schema = File.ReadAllText(Path.Combine(
            root, "docs", "ligase-host", "android-sync-v1.schema.json"));
        var vectors = File.ReadAllText(Path.Combine(
            root, "tests", "fixtures", "android-sync-v1-vectors.json"));
        var native = File.ReadAllText(Path.Combine(root, "src", "nvhttp.cpp"));

        StringAssert.Contains(schema, "\"additionalProperties\": false");
        StringAssert.Contains(schema, "\"provider\": { \"const\": \"steam\" }");
        StringAssert.Contains(vectors, "\"portable-string\"");
        StringAssert.Contains(vectors, "\"bindingRetired\"");
        StringAssert.Contains(native, "validate_appasset");
        StringAssert.Contains(native, "X-Ligase-App-Uuid");
        StringAssert.Contains(native, "X-Ligase-Cover-Sha256");
        StringAssert.Contains(native, "Content-Length");
    }

    private static LibrarySyncItem Item(
        LibraryItemKind kind = LibraryItemKind.Steam,
        uint? steamAppId = 123,
        LayoutBindingV1? layoutBinding = null,
        PortableGameIdentityV1? identity = null,
        string? coverSourceId = "123",
        bool includeLayoutBinding = true) => new()
    {
        Id = Guid.Parse(AppUuid),
        Kind = kind,
        Name = "Example",
        SteamAppId = steamAppId,
        PortableIdentity = identity ?? (kind == LibraryItemKind.Steam
            ? new PortableGameIdentityV1("steam", "123")
            : null),
        LayoutBinding = includeLayoutBinding
            ? layoutBinding ?? new LayoutBindingV1(LayoutUuid, 7)
            : null,
        CoverSha256 = CoverSha,
        CoverSourceKind = "steamClientLibraryCache",
        CoverSourceId = coverSourceId,
        CoverUsageRights = "thirdPartyArtworkLocalUseOnlyNoRedistribution",
        System = false,
        PublishedToClients = true,
        AddedAt = DateTimeOffset.Parse("2026-08-16T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-08-16T00:00:00Z")
    };

    private static LigaseSyncDocument Document(params LibrarySyncItem[] items) => new()
    {
        Library = new LibrarySyncState
        {
            Revision = 1,
            UpdatedAt = DateTimeOffset.Parse("2026-08-16T00:00:00Z"),
            SortMode = LibrarySortMode.Manual,
            Items = items
        },
        Streaming = new StreamingSettingsState()
    };

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Ligase.Host.sln")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("repositoryRootUnavailable");
    }
}
