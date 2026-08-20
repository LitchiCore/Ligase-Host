using Ligase.Host.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class AddApplicationChromeTests
{
    [TestMethod]
    public void ScrollUsesHysteresisAndDoesNotFlapNearThreshold()
    {
        var state = new AddApplicationChromeState();

        Assert.IsFalse(state.Update(95));
        Assert.IsTrue(state.Update(96));
        Assert.IsTrue(state.IsCompact);
        Assert.IsFalse(state.Update(95));
        Assert.IsFalse(state.Update(25));
        Assert.IsTrue(state.Update(24));
        Assert.IsFalse(state.IsCompact);
    }

    [TestMethod]
    public void ExplicitExpandIsIdempotent()
    {
        var state = new AddApplicationChromeState();
        state.Update(120);

        Assert.IsTrue(state.Expand());
        Assert.IsFalse(state.Expand());
        Assert.IsFalse(state.IsCompact);
    }

    [TestMethod]
    public void XamlKeepsCompactSearchKeyboardAndAutomationEntry()
    {
        var xaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Ligase.Desktop", "Pages",
            "AddApplicationPage.xaml"));

        StringAssert.Contains(xaml, "ViewChanged=\"OnSteamResultsViewChanged\"");
        StringAssert.Contains(xaml, "AutomationProperties.Name=\"搜索 Steam 游戏\"");
        StringAssert.Contains(xaml, "Click=\"OnFocusSteamSearch\"");
        StringAssert.Contains(xaml, "x:Name=\"CompactHeader\"");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CMakeLists.txt")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "Ligase.Desktop")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException();
    }
}
