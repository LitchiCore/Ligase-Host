using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class DesktopAccessibilityContractTests
{
    [TestMethod]
    public void SteamSearchHasDedicatedAccessibleNameFocusAndExactBinding()
    {
        var xaml = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "Ligase.Desktop",
            "Pages",
            "AddApplicationPage.xaml"));

        StringAssert.Contains(xaml, "x:Name=\"SteamGameSearchBox\"");
        StringAssert.Contains(xaml, "x:Name=\"SteamGameSearchLabel\"");
        StringAssert.Contains(xaml, "AutomationProperties.Name=\"搜索 Steam 游戏\"");
        StringAssert.Contains(xaml, "AutomationProperties.HelpText=\"按 Steam 游戏名称、App ID 或安装目录筛选结果\"");
        StringAssert.Contains(xaml, "AutomationProperties.LabeledBy=\"{Binding ElementName=SteamGameSearchLabel}\"");
        StringAssert.Contains(xaml, "IsTabStop=\"True\"");
        StringAssert.Contains(xaml, "TabIndex=\"0\"");
        StringAssert.Contains(xaml, "Loaded=\"OnSteamGameSearchLoaded\"");
        StringAssert.Contains(
            xaml,
            "Text=\"{x:Bind ViewModel.SteamSearchText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"");

        var codeBehind = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "Ligase.Desktop",
            "Pages",
            "AddApplicationPage.xaml.cs"));
        StringAssert.Contains(codeBehind, "FindDescendantTextBox(searchBox)");
        StringAssert.Contains(codeBehind, "AutomationProperties.SetName(editor, \"搜索 Steam 游戏\")");
        StringAssert.Contains(codeBehind, "AutomationProperties.SetLabeledBy(editor, SteamGameSearchLabel)");
        StringAssert.Contains(codeBehind, "editor.IsTabStop = true");
        StringAssert.Contains(codeBehind, "editor.TabIndex = 0");
    }

    [TestMethod]
    public void SteamAddPageStacksSearchActionsAndCardsBelow760Pixels()
    {
        var root = RepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Pages", "AddApplicationPage.xaml"));
        var card = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Controls", "SteamGameResultCard.xaml"));

        StringAssert.Contains(page, "x:Name=\"WindowWidthStates\"");
        StringAssert.Contains(page, "x:Name=\"NarrowWindow\"");
        StringAssert.Contains(page, "<AdaptiveTrigger MinWindowWidth=\"760\" />");
        StringAssert.Contains(page, "Target=\"CoverSearchButton.(Grid.Row)\" Value=\"1\"");
        StringAssert.Contains(page, "Target=\"SteamScanButton.(Grid.Row)\" Value=\"1\"");
        StringAssert.Contains(page, "Target=\"SteamScanButton.HorizontalAlignment\" Value=\"Stretch\"");
        StringAssert.Contains(page, "AutomationProperties.Name=\"重新扫描 Steam 游戏库\"");
        StringAssert.Contains(card, "x:Name=\"Narrow\"");
        StringAssert.Contains(card, "Target=\"ActionPanel.(Grid.Row)\" Value=\"1\"");
        StringAssert.Contains(card, "AutomationProperties.HeadingLevel=\"Level3\"");
        StringAssert.Contains(card, "AutomationProperties.LiveSetting=\"Polite\"");
        StringAssert.Contains(card, "Result.CardAccessibleName");
        StringAssert.Contains(card, "Result.CoverActionAccessibleName");
        StringAssert.Contains(card, "Result.AddActionAccessibleName");
        StringAssert.Contains(card, "Result.RemoveActionAccessibleName");
    }

    [TestMethod]
    public void CoreReadinessUsesDispatcherUnlocksAddEntryAndOffersSafeRefresh()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "Ligase.Desktop",
            "MainWindow.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "Ligase.Desktop",
            "MainWindow.xaml"));

        StringAssert.Contains(source, "DispatchCoreReadinessAsync");
        StringAssert.Contains(source, "DispatcherQueue.TryEnqueue");
        StringAssert.Contains(source, "AddApplicationNavigationItem.IsEnabled = authority.CanWrite");
        StringAssert.Contains(source, "if (_core.IsRunning)");
        StringAssert.Contains(source, "设备接口启动超时 · 可刷新状态");
        StringAssert.Contains(xaml, "AutomationProperties.Name=\"刷新核心状态\"");
    }

    [TestMethod]
    public void ShortcutImportRequiresPreviewConfirmationAndHasKeyboardAlternative()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Pages", "AddApplicationPage.xaml"));
        var code = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Pages", "AddApplicationPage.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "ViewModels", "AddApplicationViewModel.cs"));

        StringAssert.Contains(xaml, "x:Name=\"ShortcutDropZone\"");
        StringAssert.Contains(xaml, "AllowDrop=\"True\"");
        StringAssert.Contains(xaml, "Drop=\"OnShortcutDrop\"");
        StringAssert.Contains(xaml, "Click=\"OnBrowseShortcut\"");
        StringAssert.Contains(xaml, "AutomationProperties.Name=\"选择 Windows 快捷方式进行预览\"");
        StringAssert.Contains(xaml, "Click=\"OnConfirmShortcut\"");
        StringAssert.Contains(xaml, "Click=\"OnCancelShortcut\"");
        StringAssert.Contains(xaml, "MaxWidth=\"760\"");
        Assert.IsFalse(xaml.Contains("<Border Width=\"760\"", StringComparison.Ordinal));

        StringAssert.Contains(code, "GetStorageItemsAsync");
        StringAssert.Contains(code, "PreviewShortcutPaths");
        StringAssert.Contains(viewModel, "if (paths.Count != 1)");
        StringAssert.Contains(viewModel, "shortcutResolver.Revalidate(pending)");
        StringAssert.Contains(viewModel, "mutationCoordinator.AddExecutableAsync");
        Assert.IsTrue(
            viewModel.IndexOf("shortcutResolver.Revalidate(pending)", StringComparison.Ordinal) <
            viewModel.IndexOf("mutationCoordinator.AddExecutableAsync", StringComparison.Ordinal));
    }

    [TestMethod]
    public void LayoutBindingIsReachableAndUsesUuidOnlyAuthorityProjection()
    {
        var root = RepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Presentation", "LayoutCatalog",
            "LayoutCatalogPage.xaml"));
        var page = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Presentation", "LayoutCatalog",
            "LayoutCatalogPage.xaml.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Core", "Services", "LibraryMutationCoordinator.cs"));
        var native = File.ReadAllText(Path.Combine(root, "src", "nvhttp.cpp"));

        StringAssert.Contains(xaml, "AutomationProperties.Name=\"管理所选布局的游戏绑定\"");
        StringAssert.Contains(xaml, "Click=\"OnManageBinding\"");
        StringAssert.Contains(page, "LayoutBindingGameOption");
        StringAssert.Contains(page, "ViewModel.SetBindingAsync");
        StringAssert.Contains(coordinator, "layoutCatalogService.SetBindingAsync");
        StringAssert.Contains(coordinator, "repository.SetLayoutBindingAsync");
        StringAssert.Contains(native, "{\"portableIdentity\"");
        StringAssert.Contains(native, "{\"layoutBinding\"");
        Assert.IsFalse(page.Contains("SteamAppId", StringComparison.Ordinal));
        Assert.IsFalse(page.Contains("item.Name ==", StringComparison.Ordinal));
    }

    [TestMethod]
    public void VirtualDesktopManagementAndPreviewSourceAreReachableAndAccessible()
    {
        var root = RepositoryRoot();
        var library = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Pages", "GameLibraryPage.xaml.cs"));
        var settings = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Pages", "SettingsPage.xaml"));
        var monitor = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "Pages", "StreamMonitorPage.xaml"));

        StringAssert.Contains(library, "ShowVirtualDesktopManagerAsync");
        StringAssert.Contains(library, "PrimaryButtonText = state.IsEnabled ? \"停用虚拟桌面\" : \"创建并启用\"");
        StringAssert.Contains(settings, "AutomationProperties.Name=\"虚拟桌面当前状态\"");
        StringAssert.Contains(settings, "AutomationProperties.Name=\"刷新虚拟桌面状态\"");
        StringAssert.Contains(monitor, "AutomationProperties.Name=\"预览显示源\"");
        StringAssert.Contains(monitor, "DisplayMemberPath=\"Label\"");
        StringAssert.Contains(monitor, "Text=\"{x:Bind ViewModel.SourceLabel, Mode=OneWay}\"");
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
                File.Exists(Path.Combine(directory.FullName, "CMakeLists.txt")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "Ligase.Desktop")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("repositoryRootUnavailable");
    }
}
