using Ligase.Host.Core.Models;
using Ligase.Host.Desktop.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class ExistingItemCoverPublicRouteTests
{
    [TestMethod]
    public async Task AddedSteamCardUsesPersistedTransactionExactlyOnce()
    {
        var itemId = Guid.NewGuid();
        var appId = 3548580u;
        var artifact = Path.Combine(Path.GetTempPath(), $"{itemId:D}.png");
        var candidate = Candidate(appId);
        var workflow = new RecordingWorkflow(candidate, artifact);
        var viewModel = new AddApplicationViewModel(
            null!, null!, null!, null!, null!, null!, workflow);
        var card = new SteamGameResultViewModel(Game(appId), itemId);

        await viewModel.FindSteamCoverAsync(card);
        await viewModel.SelectCoverAsync(candidate);

        Assert.AreEqual(1, workflow.FindCalls);
        Assert.AreEqual(1, workflow.ApplyCalls);
        Assert.AreEqual(itemId, workflow.AppliedLibraryItemId);
        Assert.AreEqual(appId, workflow.AppliedSteamAppId);
        Assert.AreEqual(artifact, viewModel.SelectedCoverPath);
        StringAssert.Contains(viewModel.Message, "已持久化");
        StringAssert.Contains(viewModel.Message, "同步回读");
    }

    [TestMethod]
    public void PublicPagePassesTheAddedCardInsteadOfThePreviewOnlyGameShape()
    {
        var root = RepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Pages", "AddApplicationPage.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "ViewModels", "AddApplicationViewModel.cs"));

        StringAssert.Contains(page, "ViewModel.FindSteamCoverAsync(result)");
        Assert.IsFalse(page.Contains(
            "ViewModel.FindSteamCoverAsync(result.Game)",
            StringComparison.Ordinal));
        StringAssert.Contains(viewModel, "result.LibraryItemId is Guid libraryItemId");
        StringAssert.Contains(viewModel, "existingItemCoverWorkflow.ApplyAsync");
        Assert.IsTrue(
            viewModel.IndexOf("existingItemCoverWorkflow.ApplyAsync", StringComparison.Ordinal) <
            viewModel.IndexOf("已持久化", StringComparison.Ordinal));
    }

    private static CoverCandidate Candidate(uint appId) => new(
        "Verified capsule",
        $"steam:{appId}:library-capsule",
        "file:///D:/verified.png",
        "file:///D:/verified.png",
        "steamClientLibraryCache",
        appId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "thirdPartyArtworkLocalUseOnlyNoRedistribution",
        appId);

    private static SteamGame Game(uint appId) => new(
        appId,
        "Verified game",
        "VerifiedGame",
        @"D:\Steam",
        $@"D:\Steam\steamapps\appmanifest_{appId}.acf",
        1024);

    private static string RepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("LIGASE_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var gitMarker = Path.Combine(directory.FullName, ".git");
            if ((Directory.Exists(gitMarker) || File.Exists(gitMarker)) &&
                File.Exists(Path.Combine(directory.FullName, "CMakeLists.txt")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("repositoryRootUnavailable");
    }

    private sealed class RecordingWorkflow(
        CoverCandidate candidate,
        string artifact) : IExistingItemCoverWorkflow
    {
        public int FindCalls { get; private set; }
        public int ApplyCalls { get; private set; }
        public Guid AppliedLibraryItemId { get; private set; }
        public uint AppliedSteamAppId { get; private set; }

        public Task<IReadOnlyList<CoverCandidate>> FindVerifiedAsync(
            Guid libraryItemId,
            uint steamAppId,
            CancellationToken cancellationToken = default)
        {
            FindCalls++;
            return Task.FromResult<IReadOnlyList<CoverCandidate>>([candidate]);
        }

        public Task<ExistingItemCoverUpdateResult> ApplyAsync(
            Guid libraryItemId,
            uint steamAppId,
            CoverCandidate selected,
            CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            AppliedLibraryItemId = libraryItemId;
            AppliedSteamAppId = steamAppId;
            return Task.FromResult(new ExistingItemCoverUpdateResult(
                libraryItemId,
                new PortableGameIdentityV1("steam", steamAppId.ToString()),
                artifact,
                new string('a', 64),
                selected.SourceKind,
                selected.SourceId,
                selected.UsageRights,
                DateTimeOffset.UtcNow,
                false,
                true,
                2));
        }
    }
}
