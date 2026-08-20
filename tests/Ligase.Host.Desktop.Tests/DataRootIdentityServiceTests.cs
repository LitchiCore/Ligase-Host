using Ligase.Host.Core.Domain.Installation;
using Ligase.Host.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class DataRootIdentityServiceTests
{
    [TestMethod]
    public void ProgramDataInstanceAndMatchingBootstrapAreVisibleWithoutSecrets()
    {
        var instanceId = Guid.Parse("8e7fc517-8069-4222-987b-81f6947e4e46");
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Ligase Host",
            "Instances",
            instanceId.ToString("D"));
        var temporary = Path.Combine(
            Path.GetTempPath(),
            "Ligase.DataRootIdentity.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var bootstrap = Path.Combine(temporary, "ligase-bootstrap.json");
        File.WriteAllText(bootstrap, System.Text.Json.JsonSerializer.Serialize(new { dataRoot }));
        try
        {
            var state = new DataRootIdentityService(
                new LigasePaths(dataRoot),
                Layout(temporary, bootstrap)).Read();

            Assert.AreEqual(Path.GetFullPath(dataRoot), state.DataRoot);
            Assert.AreEqual(instanceId.ToString("D"), state.InstanceIdentity);
            StringAssert.Contains(state.StorageStatus, "ProgramData");
            StringAssert.Contains(state.BootstrapStatus, "一致");
            Assert.IsFalse(state.BootstrapStatus.Contains("token", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(temporary, true);
        }
    }

    private static InstallationLayout Layout(string root, string bootstrap) => new(
        InstallationLayoutKind.Development,
        root,
        root,
        Path.Combine(root, "desktop.exe"),
        root,
        Path.Combine(root, "sunshine.exe"),
        root,
        Path.Combine(root, "watcher.exe"),
        root,
        root,
        root,
        bootstrap,
        Path.Combine(root, "manifest.json"));
}
