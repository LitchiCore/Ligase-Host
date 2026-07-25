using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class FreshInstallPackagingTests
{
    [TestMethod]
    public async Task DryRunReturnsTypedReadinessWithoutMutatingBootstrap()
    {
        using var fixture = new InstallFixture();

        var result = await fixture.RunAsync("DryRun");

        Assert.AreEqual(0, result.ExitCode, result.Output);
        using var document = JsonDocument.Parse(result.Output);
        Assert.AreEqual("dryRunReady", document.RootElement.GetProperty("code").GetString());
        Assert.IsTrue(document.RootElement.GetProperty("success").GetBoolean());
        Assert.AreEqual(
            "createFreshInstance",
            document.RootElement.GetProperty("dataRootAction").GetString());
        Assert.AreEqual(3, document.RootElement.GetProperty("artifacts").GetArrayLength());
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "ligase-bootstrap.json")));
    }

    [TestMethod]
    public async Task ReadbackRejectsAnyRequiredArtifactHashMismatch()
    {
        using var fixture = new InstallFixture();
        await File.AppendAllTextAsync(
            Path.Combine(fixture.Root, "Apollo", "sunshine.exe"),
            "changed");

        var result = await fixture.RunAsync("Readback");

        Assert.AreEqual(10, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.AreEqual(
            "artifactReadbackFailed",
            document.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(document.RootElement.GetProperty("success").GetBoolean());
        Assert.IsFalse(result.Output.Contains(fixture.Root, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task LegacyDriverTrustCleanupRequiresAnExplicitConfirmation()
    {
        using var fixture = new InstallFixture();

        var result = await fixture.RunAsync("CleanupLegacyDriverTrust");

        Assert.AreEqual(21, result.ExitCode, result.Output);
        using var document = JsonDocument.Parse(result.Output);
        Assert.AreEqual(
            "userConfirmationRequired",
            document.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(document.RootElement.GetProperty("success").GetBoolean());
    }

    [TestMethod]
    public void NsIsOwnsOnlyLigaseIntegrationAndKeepsVirtualDisplayOptional()
    {
        var repo = FindRepositoryRoot();
        var nsis = File.ReadAllText(
            Path.Combine(repo, "packaging", "windows", "ligase", "LigaseHost.nsi"));
        var build = File.ReadAllText(
            Path.Combine(repo, "packaging", "windows", "ligase", "Build-LigaseInstaller.ps1"));

        StringAssert.Contains(nsis, "SectionIn RO");
        StringAssert.Contains(nsis, "Section /o \"Ligase Virtual Display (optional)\"");
        StringAssert.Contains(nsis, "-ConfigureFirewall");
        StringAssert.Contains(nsis, "-DataDisposition $3");
        StringAssert.Contains(nsis, "-Action InstallVirtualDisplay");
        StringAssert.Contains(nsis, "-Action UninstallVirtualDisplay");
        Assert.IsFalse(nsis.Contains("migrate-config", StringComparison.Ordinal));
        Assert.IsFalse(nsis.Contains("add-firewall-rule.bat", StringComparison.Ordinal));
        StringAssert.Contains(build, "-p:Platform=$Platform");
        StringAssert.Contains(build, "tools/Ligase.GameWatcher/Ligase.GameWatcher.csproj");
        StringAssert.Contains(build, "Apollo/sunshine.exe");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CMakeLists.txt")) &&
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("repositoryRootUnavailable");
    }

    private sealed class InstallFixture : IDisposable
    {
        private readonly string _script;

        public InstallFixture()
        {
            var repo = FindRepositoryRoot();
            _script = Path.Combine(
                repo,
                "packaging",
                "windows",
                "ligase",
                "Manage-LigaseInstallation.ps1");
            Root = Path.Combine(
                Path.GetTempPath(),
                "ligase-install-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "Apollo"));
            WriteArtifact("Ligase.Host.Desktop.exe", "desktop");
            WriteArtifact(Path.Combine("Apollo", "sunshine.exe"), "core");
            WriteArtifact("Ligase.GameWatcher.exe", "watcher");

            var artifacts = new[]
            {
                Artifact("desktop", "Ligase.Host.Desktop.exe"),
                Artifact("managedCore", "Apollo/sunshine.exe"),
                Artifact("gameWatcher", "Ligase.GameWatcher.exe")
            };
            var manifest = new
            {
                schemaVersion = 1,
                sourceHead = new string('a', 40),
                configuration = "Release",
                platform = "x64",
                installMode = "packaged",
                releaseKind = "UnsignedDev",
                artifacts,
                privilegedHelpers = Array.Empty<object>(),
                virtualDisplay = new
                {
                    required = false,
                    installer = "Drivers/sudovda/install.bat",
                    uninstaller = "Drivers/sudovda/uninstall.bat",
                    certificateThumbprint = "",
                    selfSigned = true,
                    timestamped = false,
                    trust = "untrusted"
                },
                firewall = new
                {
                    required = false,
                    manifest = "Firewall/ligase-firewall-v1.json",
                    script = "Firewall/Manage-LigaseFirewall.ps1",
                    basePort = 48989
                }
            };
            File.WriteAllText(
                Path.Combine(Root, "ligase-install-manifest.json"),
                JsonSerializer.Serialize(manifest),
                new UTF8Encoding(false));
        }

        public string Root { get; }

        public async Task<(int ExitCode, string Output)> RunAsync(string action)
        {
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(_script);
            start.ArgumentList.Add("-Action");
            start.ArgumentList.Add(action);
            start.ArgumentList.Add("-InstallDirectory");
            start.ArgumentList.Add(Root);
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("testProcessStartFailed");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var error = await stderr;
            Assert.AreEqual(string.Empty, error, error);
            return (process.ExitCode, (await stdout).Trim());
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        private void WriteArtifact(string relativePath, string value)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value, new UTF8Encoding(false));
        }

        private object Artifact(string role, string relativePath)
        {
            var path = Path.Combine(Root, relativePath);
            using var stream = File.OpenRead(path);
            return new
            {
                role,
                relativePath = relativePath.Replace('\\', '/'),
                unsignedContentSha256 =
                    Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
                signedArtifactSha256 =
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
                size = new FileInfo(path).Length,
                signature = new
                {
                    status = "nonRelease",
                    signerSubject = (string?)null,
                    signerThumbprint = (string?)null,
                    timestamped = false
                }
            };
        }
    }
}
