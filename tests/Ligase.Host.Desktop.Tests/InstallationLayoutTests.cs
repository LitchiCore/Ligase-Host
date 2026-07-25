using Ligase.Host.Core.Domain.Installation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class InstallationLayoutTests
{
    [TestMethod]
    public void StructuredLayoutResolvesAllPathsWithoutUsingCurrentDirectory()
    {
        using var fixture = new LayoutFixture(validManifest: true);
        var layout = InstallationLayoutResolver.ResolveFromDesktopBase(
            fixture.Desktop);

        Assert.AreEqual(InstallationLayoutKind.Structured, layout.Kind);
        Assert.AreEqual(
            Path.Combine(fixture.Root, "Core", "sunshine.exe"),
            layout.CoreExecutable);
        Assert.AreEqual(
            Path.Combine(
                fixture.Root,
                "Tools",
                "GameWatcher",
                "Ligase.GameWatcher.exe"),
            layout.GameWatcherExecutable);
        Assert.AreEqual(
            Path.Combine(fixture.Root, "ligase-bootstrap.json"),
            layout.BootstrapPath);
        Assert.AreEqual(
            Path.Combine(fixture.Root, "Deployment", "Firewall"),
            layout.FirewallDirectory);
    }

    [TestMethod]
    public void MalformedStructuredManifestFailsClosed()
    {
        using var fixture = new LayoutFixture(validManifest: false);

        Assert.ThrowsException<InvalidDataException>(
            () => InstallationLayoutResolver.ResolveFromDesktopBase(
                fixture.Desktop));
    }

    [TestMethod]
    public void MissingStructuredManifestFailsClosedInsteadOfBecomingDevelopment()
    {
        using var fixture = new LayoutFixture(validManifest: null);

        var exception = Assert.ThrowsException<InvalidDataException>(
            () => InstallationLayoutResolver.ResolveFromDesktopBase(
                fixture.Desktop));

        Assert.AreEqual("structuredInstallManifestMissing", exception.Message);
    }

    private sealed class LayoutFixture : IDisposable
    {
        public LayoutFixture(bool? validManifest)
        {
            Root = Path.Combine(
                Environment.GetEnvironmentVariable("LIGASE_TEMP_ROOT")
                    ?? Path.GetTempPath(),
                "installation-layout-" + Guid.NewGuid().ToString("N"));
            Desktop = Path.Combine(Root, "Desktop");
            Directory.CreateDirectory(Desktop);
            if (validManifest is not null)
            {
                File.WriteAllText(
                    Path.Combine(Root, "ligase-install-manifest.json"),
                    validManifest.Value
                        ? """
                          {"schemaVersion":1,"installMode":"packaged","installLayout":"structured-v1"}
                          """
                        : """{"schemaVersion":1,"installMode":"packaged"}""");
            }
        }

        public string Root { get; }
        public string Desktop { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
