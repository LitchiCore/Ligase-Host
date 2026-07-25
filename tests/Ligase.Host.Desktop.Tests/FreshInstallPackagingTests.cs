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
            "createDefaultFreshInstance",
            document.RootElement.GetProperty("dataRootAction").GetString());
        Assert.AreEqual(3, document.RootElement.GetProperty("artifacts").GetArrayLength());
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "ligase-bootstrap.json")));
    }

    [TestMethod]
    public async Task ReadbackRejectsAnyRequiredArtifactHashMismatch()
    {
        using var fixture = new InstallFixture();
        await File.AppendAllTextAsync(
            Path.Combine(fixture.Root, "Core", "sunshine.exe"),
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
    public async Task UpgradePreservesValidBootstrapBytesAndRemovesOnlyOwnedLegacyFiles()
    {
        using var fixture = new InstallFixture();
        var dataRoot = Path.Combine(fixture.Root, "existing-data");
        Directory.CreateDirectory(dataRoot);
        var bootstrap = Path.Combine(fixture.Root, "ligase-bootstrap.json");
        var original = Encoding.UTF8.GetBytes(
            $"{{ \"schemaVersion\": 1, \"dataRoot\": {JsonSerializer.Serialize(dataRoot)} }}");
        await File.WriteAllBytesAsync(bootstrap, original);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "legacy-locale"));
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "legacy-locale", "legacy-owned.dll"),
            "owned");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, "unknown-user-file.txt"),
            "preserve");
        fixture.SetLegacyOwnedEntries(["legacy-locale/legacy-owned.dll"]);

        var result = await fixture.RunAsync("Install");

        Assert.AreEqual(0, result.ExitCode, result.Output);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(bootstrap));
        Assert.IsFalse(File.Exists(
            Path.Combine(fixture.Root, "legacy-locale", "legacy-owned.dll")));
        Assert.IsFalse(Directory.Exists(
            Path.Combine(fixture.Root, "legacy-locale")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "unknown-user-file.txt")));
    }

    [TestMethod]
    public async Task InvalidExistingBootstrapFailsClosedWithoutReplacement()
    {
        using var fixture = new InstallFixture();
        var bootstrap = Path.Combine(fixture.Root, "ligase-bootstrap.json");
        var original = Encoding.UTF8.GetBytes(
            "{\"schemaVersion\":1,\"dataRoot\":\"relative\"}");
        await File.WriteAllBytesAsync(bootstrap, original);

        var result = await fixture.RunAsync("Install");

        Assert.AreEqual(10, result.ExitCode);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(bootstrap));
        using var document = JsonDocument.Parse(result.Output);
        Assert.AreEqual(
            "bootstrapInvalid",
            document.RootElement.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task DuplicateBootstrapAuthorityFailsClosedWithoutReplacement()
    {
        using var fixture = new InstallFixture();
        var dataRoot = Path.Combine(fixture.Root, "existing-data");
        Directory.CreateDirectory(dataRoot);
        var bootstrap = Path.Combine(fixture.Root, "ligase-bootstrap.json");
        var original = Encoding.UTF8.GetBytes(
            $$"""{"schemaVersion":1,"dataRoot":{{JsonSerializer.Serialize(dataRoot)}},"dataRoot":{{JsonSerializer.Serialize(dataRoot)}}}""");
        await File.WriteAllBytesAsync(bootstrap, original);

        var result = await fixture.RunAsync("Install");

        Assert.AreEqual(10, result.ExitCode);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(bootstrap));
        using var document = JsonDocument.Parse(result.Output);
        Assert.AreEqual(
            "bootstrapInvalid",
            document.RootElement.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task FreshInstallAcceptsExplicitAbsoluteDataRoot()
    {
        using var fixture = new InstallFixture();
        var dataRoot = Path.Combine(fixture.Root, "explicit-data");

        var result = await fixture.RunAsync("Install", dataRoot);

        Assert.AreEqual(0, result.ExitCode, result.Output);
        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(fixture.Root, "ligase-bootstrap.json")));
        Assert.AreEqual(
            Path.GetFullPath(dataRoot),
            document.RootElement.GetProperty("dataRoot").GetString());
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
        Assert.AreEqual(
            1,
            System.Text.RegularExpressions.Regex.Matches(
                nsis,
                "RMDir /r \"\\$INSTDIR\"").Count,
            "Only the explicit uninstall section may recursively remove the owned install root.");
        StringAssert.Contains(build, "-p:Platform=$Platform");
        StringAssert.Contains(build, "-p:LigaseStructuredPackage=true");
        StringAssert.Contains(build, "-Filter \"Ligase.GameWatcher.*\"");
        StringAssert.Contains(build, "tools/Ligase.GameWatcher/Ligase.GameWatcher.csproj");
        StringAssert.Contains(build, "Core/sunshine.exe");
        StringAssert.Contains(
            nsis,
            "$INSTDIR\\Desktop\\Ligase.Host.Desktop.exe");
        StringAssert.Contains(nsis, "InstallDirRegKey HKLM");
        StringAssert.Contains(nsis, "/InstallDirectory=");
        StringAssert.Contains(nsis, "\"InstallLocation\" \"$INSTDIR\"");
        StringAssert.Contains(build, "Resolve-LigaseInstallDirectory.ps1");
    }

    [TestMethod]
    public async Task InstallDirectoryResolverSupportsExplicitDAndRegisteredUpgrade()
    {
        const string explicitD = @"D:\Program Files\Ligase Host";
        const string registeredD = @"D:\Applications\Ligase Host";

        var explicitResult = await RunInstallDirectoryResolverAsync(
            $"\"/InstallDirectory={explicitD}\"",
            registeredD,
            @"C:\Program Files\Ligase Host");
        var upgradeResult = await RunInstallDirectoryResolverAsync(
            string.Empty,
            registeredD,
            @"C:\Program Files\Ligase Host");

        Assert.AreEqual(0, explicitResult.ExitCode, explicitResult.Error);
        Assert.AreEqual(explicitD, explicitResult.Output);
        Assert.AreEqual(0, upgradeResult.ExitCode, upgradeResult.Error);
        Assert.AreEqual(registeredD, upgradeResult.Output);
    }

    [TestMethod]
    public async Task InstallDirectoryResolverRejectsDuplicateRelativeRootAndNonCanonical()
    {
        var duplicate = await RunInstallDirectoryResolverAsync(
            "\"/InstallDirectory=D:\\Ligase Host\" \"/InstallDirectory=E:\\Ligase Host\"",
            string.Empty,
            @"C:\Program Files\Ligase Host");
        var relative = await RunInstallDirectoryResolverAsync(
            "/InstallDirectory=relative",
            string.Empty,
            @"C:\Program Files\Ligase Host");
        var root = await RunInstallDirectoryResolverAsync(
            "/InstallDirectory=D:\\",
            string.Empty,
            @"C:\Program Files\Ligase Host");
        var nonCanonical = await RunInstallDirectoryResolverAsync(
            "\"/InstallDirectory=D:\\Programs\\..\\Ligase Host\"",
            string.Empty,
            @"C:\Program Files\Ligase Host");

        Assert.AreNotEqual(0, duplicate.ExitCode);
        Assert.AreNotEqual(0, relative.ExitCode);
        Assert.AreNotEqual(0, root.ExitCode);
        Assert.AreNotEqual(0, nonCanonical.ExitCode);
        Assert.AreEqual("installDirectoryInvalid", duplicate.Error);
        Assert.AreEqual("installDirectoryInvalid", relative.Error);
        Assert.AreEqual("installDirectoryInvalid", root.Error);
        Assert.AreEqual("installDirectoryInvalid", nonCanonical.Error);
    }

    private static async Task<(int ExitCode, string Output, string Error)>
        RunInstallDirectoryResolverAsync(
            string rawParameters,
            string registered,
            string defaultLocation)
    {
        var repo = FindRepositoryRoot();
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.Environment["LIGASE_INSTALL_RAW_PARAMETERS"] = rawParameters;
        start.Environment["LIGASE_INSTALL_REGISTERED_LOCATION"] = registered;
        start.Environment["LIGASE_INSTALL_DEFAULT_LOCATION"] = defaultLocation;
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(
            repo,
            "packaging",
            "windows",
            "ligase",
            "Resolve-LigaseInstallDirectory.ps1"));
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("testProcessStartFailed");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output, await error);
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
            Directory.CreateDirectory(Path.Combine(Root, "Core"));
            WriteArtifact(
                Path.Combine("Desktop", "Ligase.Host.Desktop.exe"),
                "desktop");
            WriteArtifact(Path.Combine("Core", "sunshine.exe"), "core");
            WriteArtifact(
                Path.Combine("Tools", "GameWatcher", "Ligase.GameWatcher.exe"),
                "watcher");

            var artifacts = new[]
            {
                Artifact("desktop", "Desktop/Ligase.Host.Desktop.exe"),
                Artifact("managedCore", "Core/sunshine.exe"),
                Artifact("gameWatcher", "Tools/GameWatcher/Ligase.GameWatcher.exe")
            };
            var manifest = new
            {
                schemaVersion = 1,
                installLayout = "structured-v1",
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
                    installer = "Deployment/Drivers/sudovda/install.bat",
                    uninstaller = "Deployment/Drivers/sudovda/uninstall.bat",
                    certificateThumbprint = "",
                    selfSigned = true,
                    timestamped = false,
                    trust = "untrusted"
                },
                firewall = new
                {
                    required = false,
                    manifest = "Deployment/Firewall/ligase-firewall-v1.json",
                    script = "Deployment/Firewall/Manage-LigaseFirewall.ps1",
                    basePort = 48989
                },
                ownedEntries = Array.Empty<string>(),
                legacyFlatOwnedEntries = Array.Empty<string>()
            };
            File.WriteAllText(
                Path.Combine(Root, "ligase-install-manifest.json"),
                JsonSerializer.Serialize(manifest),
                new UTF8Encoding(false));
        }

        public string Root { get; }

        public async Task<(int ExitCode, string Output)> RunAsync(
            string action,
            string? dataRoot = null)
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
            if (dataRoot is not null)
            {
                start.ArgumentList.Add("-DataRoot");
                start.ArgumentList.Add(dataRoot);
            }
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("testProcessStartFailed");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var error = await stderr;
            Assert.AreEqual(string.Empty, error, error);
            return (process.ExitCode, (await stdout).Trim());
        }

        public void SetLegacyOwnedEntries(string[] entries)
        {
            var path = Path.Combine(Root, "ligase-install-manifest.json");
            var node = System.Text.Json.Nodes.JsonNode.Parse(
                File.ReadAllText(path))!.AsObject();
            node["legacyFlatOwnedEntries"] =
                JsonSerializer.SerializeToNode(entries);
            File.WriteAllText(
                path,
                node.ToJsonString(),
                new UTF8Encoding(false));
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
