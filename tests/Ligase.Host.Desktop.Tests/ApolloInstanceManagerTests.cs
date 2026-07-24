using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class ApolloInstanceManagerTests
{
    [TestMethod]
    public void ManagedConfigurationDisablesTheCoreTrayIcon()
    {
        var root = Path.Combine(Path.GetTempPath(), "ligase-config-test");
        var configuration = ApolloInstanceManager.BuildManagedConfiguration(
            new LigasePaths(root),
            48989);
        var lines = configuration.Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries);

        CollectionAssert.Contains(lines, "sunshine_name = Ligase Host");
        CollectionAssert.Contains(lines, "system_tray = disabled");
        Assert.AreEqual(
            1,
            lines.Count(line => line.StartsWith("system_tray =", StringComparison.Ordinal)));
    }
}
