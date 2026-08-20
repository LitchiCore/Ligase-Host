using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class LibraryReloadContractTests
{
    [TestMethod]
    public void CoreReloadReturnsTypedBranchesAndSkipsUnrelatedVirtualDisplayInitialization()
    {
        var root = FindRoot();
        var http = File.ReadAllText(Path.Combine(root, "src", "nvhttp.cpp"));
        var process = File.ReadAllText(Path.Combine(root, "src", "process.cpp"));
        var header = File.ReadAllText(Path.Combine(root, "src", "process.h"));

        StringAssert.Contains(http, "reload_result(\"rejected\", \"precondition\", \"sessionActive\")");
        StringAssert.Contains(http, "reload_result(\"failed\", refresh.stage, refresh.reason_code)");
        StringAssert.Contains(http, "reload_result(\"completed\", \"readback\", \"none\")");
        StringAssert.Contains(http, "config::stream.file_apps, false, false, true");
        Assert.AreEqual(1, Count(http, "config::stream.file_apps, false, false, true"));

        StringAssert.Contains(header, "bool initialize_virtual_display = true");
        StringAssert.Contains(process, "if (initialize_virtual_display)");
        StringAssert.Contains(process, "initVDisplayDriver();");
        StringAssert.Contains(process, "if (strict_catalog_only) return std::nullopt;");
        StringAssert.Contains(process, "catalogMalformed");
        StringAssert.Contains(process, "catalogUnreadable");
        StringAssert.Contains(process, "catalogLoadFailed");
    }

    [TestMethod]
    public void DesktopDeadlineRemainsThreeSecondsAndConsumesTypedReloadMetadata()
    {
        var root = FindRoot();
        var service = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Core", "Services", "LibraryAuthorityService.cs"));
        var addApplication = File.ReadAllText(Path.Combine(
            root, "src", "Ligase.Desktop", "ViewModels", "AddApplicationViewModel.cs"));
        var actualFixture = File.ReadAllText(Path.Combine(
            root, "scripts", "ligase", "test-authority-readback.ps1"));

        StringAssert.Contains(service, "TimeSpan.FromSeconds(3)");
        StringAssert.Contains(service, "document.Reload.SchemaVersion != 1");
        StringAssert.Contains(service, "reloadMetadataMismatch");
        StringAssert.Contains(service, "deadlineExceeded");
        Assert.IsFalse(service.Contains("TimeSpan.FromSeconds(5)", StringComparison.Ordinal));
        StringAssert.Contains(addApplication, "Message = $\"未能添加“{result.Name}”：{exception.Message}");
        Assert.IsFalse(addApplication.Contains("请确认 Host 核心正在运行后重试", StringComparison.Ordinal));
        Assert.IsFalse(addApplication.Contains("核心没有确认游戏库更新。已恢复更新前状态，请重新启动", StringComparison.Ordinal));
        StringAssert.Contains(actualFixture, "catalogMalformed");
        StringAssert.Contains(actualFixture, "catalogUnreadable");
        StringAssert.Contains(actualFixture, "catalogLoadFailed");
        StringAssert.Contains(actualFixture, "Failed reload $Reason replaced the active app catalog");
    }

    private static int Count(string value, string needle) =>
        value.Split(needle, StringSplitOptions.None).Length - 1;

    private static string FindRoot()
    {
        var configured = Environment.GetEnvironmentVariable("LIGASE_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "nvhttp.cpp")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Ligase source root not found.");
    }
}
