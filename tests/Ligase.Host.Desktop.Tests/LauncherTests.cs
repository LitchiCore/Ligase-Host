using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ligase.Host.Launcher;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class LauncherTests
{
    [TestMethod]
    public void StructuredManifestResolvesOwnedDesktopFromUnicodeRoot()
    {
        using var fixture = new LauncherFixture();
        var target = LauncherManifestValidator.Resolve(fixture.LauncherPath);
        Assert.AreEqual(fixture.DesktopPath, target.ExecutablePath);
    }

    [TestMethod]
    public void MissingOrTamperedDesktopFailsClosed()
    {
        using var missing = new LauncherFixture();
        File.Delete(missing.DesktopPath);
        AssertMachineCode(
            "launcherDesktopUnavailable",
            () => LauncherManifestValidator.Resolve(missing.LauncherPath));

        using var tampered = new LauncherFixture();
        File.AppendAllText(tampered.DesktopPath, "tampered");
        AssertMachineCode(
            "launcherDesktopHashMismatch",
            () => LauncherManifestValidator.Resolve(tampered.LauncherPath));
    }

    [TestMethod]
    public void MalformedDuplicateOrUnownedDesktopManifestFailsClosed()
    {
        using var malformed = new LauncherFixture();
        File.WriteAllText(malformed.ManifestPath, "{");
        try
        {
            _ = LauncherManifestValidator.Resolve(malformed.LauncherPath);
            Assert.Fail("Malformed JSON was accepted.");
        }
        catch (JsonException)
        {
        }

        using var duplicate = new LauncherFixture();
        var valid = File.ReadAllText(duplicate.ManifestPath);
        File.WriteAllText(
            duplicate.ManifestPath,
            valid.Replace(
                "\"installLayout\":\"structured-v1\"",
                "\"installLayout\":\"structured-v1\",\"installLayout\":\"structured-v1\"",
                StringComparison.Ordinal));
        AssertMachineCode(
            "launcherManifestInvalid",
            () => LauncherManifestValidator.Resolve(duplicate.LauncherPath));

        using var unowned = new LauncherFixture();
        File.WriteAllText(
            unowned.ManifestPath,
            File.ReadAllText(unowned.ManifestPath).Replace(
                LauncherManifestValidator.DesktopRelativePath,
                "Ligase.Host.Desktop.exe",
                StringComparison.Ordinal));
        AssertMachineCode(
            "launcherManifestInvalid",
            () => LauncherManifestValidator.Resolve(unowned.LauncherPath));
    }

    [TestMethod]
    public void ChildArgumentsPreserveUnicodeSpacesAndDoNotOverrideWorkingDirectory()
    {
        var arguments = new[] { "--name=测试 主机", "--quoted=\"a b\"", "" };
        var start = Ligase.Host.Launcher.Program.CreateStartInfo(
            @"D:\Program Files\Ligase Host\Desktop\Ligase.Host.Desktop.exe",
            arguments);

        Assert.AreEqual(string.Empty, start.WorkingDirectory);
        Assert.IsFalse(start.UseShellExecute);
        CollectionAssert.AreEqual(arguments, start.ArgumentList.ToArray());
    }

    private static void AssertMachineCode(string code, Action action)
    {
        var exception = Assert.ThrowsException<InvalidDataException>(action);
        Assert.AreEqual(code, exception.Message);
    }

    private sealed class LauncherFixture : IDisposable
    {
        public LauncherFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "Ligase 启动器 " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "Desktop"));
            LauncherPath = Path.Combine(Root, "Ligase Host.exe");
            DesktopPath = Path.Combine(Root, "Desktop", "Ligase.Host.Desktop.exe");
            ManifestPath = Path.Combine(Root, "ligase-install-manifest.json");
            File.WriteAllText(LauncherPath, "launcher", Encoding.UTF8);
            File.WriteAllText(DesktopPath, "desktop", Encoding.UTF8);
            var hash = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(DesktopPath))).ToLowerInvariant();
            File.WriteAllText(
                ManifestPath,
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    installLayout = "structured-v1",
                    artifacts = new[]
                    {
                        new
                        {
                            role = "desktop",
                            relativePath = LauncherManifestValidator.DesktopRelativePath,
                            signedArtifactSha256 = hash
                        }
                    }
                }),
                new UTF8Encoding(false));
        }

        public string Root { get; }
        public string LauncherPath { get; }
        public string DesktopPath { get; }
        public string ManifestPath { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
