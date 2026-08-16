using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class ExistingItemCoverAccessibilityContractTests
{
    [TestMethod]
    public void ExistingSteamCoverRequiresPreviewConfirmationAndPersistedReadback()
    {
        var root = RepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Pages", "GameLibraryPage.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "ViewModels", "ExistingItemCoverViewModel.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Core", "Services", "LibraryMutationCoordinator.cs"));

        StringAssert.Contains(page, "PrimaryButtonText = item.Kind == LibraryItemKind.Steam ? \"选择封面\"");
        StringAssert.Contains(page, "ShowExistingCoverPickerAsync(item)");
        StringAssert.Contains(page, "PrimaryButtonText = \"使用此封面\"");
        Assert.IsTrue(
            page.IndexOf("confirm.ShowAsync()", StringComparison.Ordinal) <
            page.IndexOf("coverViewModel.ApplyAsync", StringComparison.Ordinal));
        StringAssert.Contains(page, "封面已保存并同步");
        StringAssert.Contains(viewModel, "applicationLibrary.LoadAsync");
        StringAssert.Contains(viewModel, "steamLibraryService.DiscoverGamesAsync");
        StringAssert.Contains(viewModel, "UpdateExistingSteamCoverAsync");
        StringAssert.Contains(coordinator, "RequireReadbackAsync");
        StringAssert.Contains(coordinator, "HasPublishedItem(readback, value.Updated)");
    }

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
}
