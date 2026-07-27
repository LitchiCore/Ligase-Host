using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class FreshInstallPackagingTests
{
    [TestMethod]
    public void SecureStorePreflightSourceHasClosedUnsignedDevelopmentBoundary()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "Ligase.Installation.TransactionHelper",
            "Program.cs"));
        var project = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "Ligase.SecureStore.Preflight",
            "Ligase.SecureStore.Preflight.csproj"));
        var sourceGate = File.ReadAllText(Path.Combine(
            root,
            "packaging",
            "windows",
            "ligase",
            "Test-LigaseSecureStorePreflight.ps1"));

        StringAssert.Contains(program, "#if PREFLIGHT_ONLY");
        StringAssert.Contains(program, "if (args.Length != 0)");
        StringAssert.Contains(program, "localManualExactSha");
        StringAssert.Contains(program, "\"Ligase Host Admin\", \"Transactions\"");
        StringAssert.Contains(
            program,
            "bytes, PreflightEvidencePath, \".preflight-evidence-\"");
        StringAssert.Contains(
            program,
            "if (store is not null && !evidenceWriteInProgress)");
        StringAssert.Contains(program, "store.DeleteEvidence()");
        StringAssert.Contains(program, "Success = true");
        StringAssert.Contains(
            program,
            "ResultCode = \"secureStorePreflightReady\"");
        StringAssert.Contains(
            program,
            "ReadBounded(stream).SequenceEqual(bytes)");
        StringAssert.Contains(program, "terminalCommit: true");
        StringAssert.Contains(program, "if (terminalCommit)");
        StringAssert.Contains(program, "var committingStore = store");
        StringAssert.Contains(program, "store = null");
        StringAssert.Contains(program, "HasAlternateDataStream(verify)");
        Assert.IsFalse(program.Contains("Ligase Host Diagnostics"));
        StringAssert.Contains(
            program,
            "\"LIGASE_INSTALL_VALIDATION_HARNESS\", null");
        StringAssert.Contains(program, "#if !PREFLIGHT_ONLY");
        StringAssert.Contains(project, "PREFLIGHT_ONLY");
        StringAssert.Contains(project, "<PublishTrimmed>true</PublishTrimmed>");
        StringAssert.Contains(project, "<SelfContained>true</SelfContained>");
        StringAssert.Contains(project, "IL2026;IL3050");
        Assert.IsFalse(program.Contains("JsonSerializer"));
        StringAssert.Contains(program, "private static void AppendJsonString(");
        StringAssert.Contains(program, "secureStorePreflightEncodingFailed");
        StringAssert.Contains(sourceGate, "secureStorePreflightForbiddenSurface");
        StringAssert.Contains(sourceGate, "executableBuilt = $false");
    }

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
        RequireElevatedAclIntegration();
        using var fixture = new InstallFixture();
        var dataRoot = Path.Combine(fixture.Root, "existing-data");
        var bootstrap = Path.Combine(fixture.Root, "ligase-bootstrap.json");
        var initial = await fixture.RunAsync("Install", dataRoot);
        Assert.AreEqual(0, initial.ExitCode, initial.Output);
        var original = await File.ReadAllBytesAsync(bootstrap);
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
    public async Task MigrationRejectsUnknownExistingRootWithoutChangingBootstrapOrSource()
    {
        using var fixture = new InstallFixture();
        var source = Path.Combine(fixture.Root, "unknown-existing-data");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "library.json"),
            "{\"revision\":1}",
            new UTF8Encoding(false));
        var bootstrap = Path.Combine(fixture.Root, "ligase-bootstrap.json");
        var originalBootstrap = Encoding.UTF8.GetBytes(
            $"{{\"schemaVersion\":1,\"dataRoot\":{JsonSerializer.Serialize(source)}}}");
        await File.WriteAllBytesAsync(bootstrap, originalBootstrap);
        var originalLibrary = await File.ReadAllBytesAsync(
            Path.Combine(source, "library.json"));
        var target = Path.Combine(fixture.Root, "migration-target");

        var result = await fixture.RunAsync(
            "Install",
            target,
            migrateDataRoot: true);

        Assert.AreEqual(10, result.ExitCode, result.Output);
        using var document = JsonDocument.Parse(result.Output);
        Assert.AreEqual(
            "dataRootMigrationNotEligible",
            document.RootElement.GetProperty("code").GetString());
        CollectionAssert.AreEqual(
            originalBootstrap,
            await File.ReadAllBytesAsync(bootstrap));
        CollectionAssert.AreEqual(
            originalLibrary,
            await File.ReadAllBytesAsync(Path.Combine(source, "library.json")));
        Assert.IsFalse(Directory.Exists(target));
    }

    [TestMethod]
    public async Task UpgradeRejectsInheritedExistingAclInsteadOfSilentlyPreservingIt()
    {
        using var fixture = new InstallFixture();
        var source = Path.Combine(fixture.Root, "legacy-inherited-data");
        Directory.CreateDirectory(source);
        var bootstrap = Path.Combine(fixture.Root, "ligase-bootstrap.json");
        var original = Encoding.UTF8.GetBytes(
            $"{{\"schemaVersion\":1,\"dataRoot\":{JsonSerializer.Serialize(source)}}}");
        await File.WriteAllBytesAsync(bootstrap, original);

        var result = await fixture.RunAsync("Install");

        Assert.AreEqual(10, result.ExitCode, result.Output);
        using var document = JsonDocument.Parse(result.Output);
        Assert.AreEqual(
            "dataRootExistingUnsafe",
            document.RootElement.GetProperty("code").GetString());
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(bootstrap));
        Assert.IsTrue(Directory.Exists(source));
    }

    [TestMethod]
    public async Task ElevatedMigrationCopiesExactSetAppliesAclAndKeepsRollbackEvidence()
    {
        RequireElevatedAclIntegration();
        using var fixture = new InstallFixture();
        var id = Guid.NewGuid().ToString("D");
        var source = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ligase Host",
            "Instances",
            id);
        var target = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Ligase Host",
            "Instances",
            Guid.NewGuid().ToString("D"));
        try
        {
            Directory.CreateDirectory(Path.Combine(source, "empty"));
            await File.WriteAllTextAsync(
                Path.Combine(source, "library.json"),
                "{\"revision\":1}",
                new UTF8Encoding(false));
            var sourceHash = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(
                    Path.Combine(source, "library.json"))));
            await fixture.WriteBootstrapAsync(source);

            var result = await fixture.RunAsync(
                "Install",
                target,
                migrateDataRoot: true);

            Assert.AreEqual(0, result.ExitCode, result.Output);
            using var outcome = JsonDocument.Parse(result.Output);
            Assert.AreEqual(
                "migratedToStandardDataRoot",
                outcome.RootElement.GetProperty("dataRootAction").GetString());
            Assert.IsTrue(Directory.Exists(Path.Combine(source, "empty")));
            Assert.IsTrue(Directory.Exists(Path.Combine(target, "empty")));
            Assert.AreEqual(
                sourceHash,
                Convert.ToHexString(SHA256.HashData(
                    await File.ReadAllBytesAsync(Path.Combine(target, "library.json")))));
            using var bootstrap = JsonDocument.Parse(
                await File.ReadAllTextAsync(
                    Path.Combine(fixture.Root, "ligase-bootstrap.json")));
            Assert.AreEqual(
                target,
                bootstrap.RootElement.GetProperty("dataRoot").GetString());
            AssertExactDataRootAcl(target);
        }
        finally
        {
            CleanupMigrationDirectory(target);
            CleanupMigrationDirectory(source);
        }
    }

    [TestMethod]
    public async Task ElevatedMigrationFirewallFailureRestoresBootstrapAndAllowsRetry()
    {
        RequireElevatedAclIntegration();
        using var fixture = new InstallFixture();
        var source = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ligase Host",
            "Instances",
            Guid.NewGuid().ToString("D"));
        var target = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Ligase Host",
            "Instances",
            Guid.NewGuid().ToString("D"));
        try
        {
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(
                Path.Combine(source, "ligase-sync.json"),
                "{\"revision\":1}",
                new UTF8Encoding(false));
            await fixture.WriteBootstrapAsync(source);
            var bootstrapPath = Path.Combine(fixture.Root, "ligase-bootstrap.json");
            var original = await File.ReadAllBytesAsync(bootstrapPath);
            fixture.ConfigureFirewallScript(configuredAfterApply: false);

            var failed = await fixture.RunAsync(
                "Install",
                target,
                configureFirewall: true,
                migrateDataRoot: true);

            Assert.AreEqual(10, failed.ExitCode, failed.Output);
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(bootstrapPath));
            Assert.IsTrue(Directory.Exists(source));
            Assert.IsFalse(Directory.Exists(target));

            fixture.ConfigureFirewallScript(configuredAfterApply: true);
            var retried = await fixture.RunAsync(
                "Install",
                target,
                configureFirewall: true,
                migrateDataRoot: true);
            Assert.AreEqual(0, retried.ExitCode, retried.Output);
            Assert.IsTrue(Directory.Exists(source));
            Assert.IsTrue(Directory.Exists(target));
        }
        finally
        {
            CleanupMigrationDirectory(target);
            CleanupMigrationDirectory(source);
        }
    }

    [TestMethod]
    public async Task ElevatedOrphanRecoveryPreservesIdentityBytesAndCreatesBootstrapLast()
    {
        RequireElevatedAclIntegration();
        using var fixture = new InstallFixture();
        var source = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ligase Host", "Instances", Guid.NewGuid().ToString("D"));
        var target = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Ligase Host", "Instances", Guid.NewGuid().ToString("D"));
        try
        {
            Directory.CreateDirectory(Path.Combine(source, "empty"));
            foreach (var name in new[]
                     {
                         "ligase-authority.json", "library.json", "ligase-sync.json"
                     })
            {
                await File.WriteAllTextAsync(
                    Path.Combine(source, name),
                    "{\"revision\":1}",
                    new UTF8Encoding(false));
            }
            var authority = await File.ReadAllBytesAsync(
                Path.Combine(source, "ligase-authority.json"));

            var result = await fixture.RunAsync(
                "Install",
                target,
                recoverOrphanDataRoot: true,
                recoverySource: source);

            Assert.AreEqual(0, result.ExitCode, result.Output);
            using var outcome = JsonDocument.Parse(result.Output);
            Assert.AreEqual(
                "recoveredOrphanLegacyDataRoot",
                outcome.RootElement.GetProperty("dataRootAction").GetString());
            CollectionAssert.AreEqual(
                authority,
                await File.ReadAllBytesAsync(
                    Path.Combine(target, "ligase-authority.json")));
            Assert.IsTrue(Directory.Exists(Path.Combine(source, "empty")));
            AssertExactDataRootAcl(target);
        }
        finally
        {
            CleanupMigrationDirectory(target);
            CleanupMigrationDirectory(source);
        }
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
        RequireElevatedAclIntegration();
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

        var security = new DirectoryInfo(dataRoot).GetAccessControl();
        Assert.IsTrue(security.AreAccessRulesProtected);
        var owner = security.GetOwner(
            typeof(SecurityIdentifier)) as SecurityIdentifier;
        Assert.IsNotNull(owner);
        Assert.AreEqual(
            new SecurityIdentifier(
                WellKnownSidType.BuiltinAdministratorsSid,
                null).Value,
            owner.Value);
        var operatorSid = WindowsIdentity.GetCurrent().User!.Value;
        var systemSid = new SecurityIdentifier(
            WellKnownSidType.LocalSystemSid,
            null).Value;
        var administratorsSid = new SecurityIdentifier(
            WellKnownSidType.BuiltinAdministratorsSid,
            null).Value;
        var rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: false,
                targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        Assert.IsTrue(rules.All(rule =>
            rule.AccessControlType == AccessControlType.Allow));
        Assert.AreEqual(3, rules.Length);
        CollectionAssert.AreEquivalent(
            new[] { operatorSid, systemSid, administratorsSid },
            rules.Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value)
                .ToArray());
        Assert.IsFalse(rules.Any(rule =>
            ((SecurityIdentifier)rule.IdentityReference).IsWellKnown(
                WellKnownSidType.BuiltinUsersSid) ||
            ((SecurityIdentifier)rule.IdentityReference).IsWellKnown(
                WellKnownSidType.AuthenticatedUserSid)));

        var readback = await fixture.RunAsync("Readback");
        Assert.AreEqual(0, readback.ExitCode, readback.Output);
        using var readbackDocument = JsonDocument.Parse(readback.Output);
        Assert.AreEqual(
            "existing",
            readbackDocument.RootElement.GetProperty("dataRootState").GetString());
    }

    [TestMethod]
    public async Task FreshInstallRequiresAResolvedDataRootAndWritesNothing()
    {
        using var fixture = new InstallFixture();

        var result = await fixture.RunAsync("Install");

        Assert.AreEqual(10, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.AreEqual(
            "dataRootRequired",
            document.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(File.Exists(
            Path.Combine(fixture.Root, "ligase-bootstrap.json")));
    }

    [TestMethod]
    public async Task FirewallIntegrationRequiresConfiguredReadbackAndRollsBackFreshState()
    {
        RequireElevatedAclIntegration();
        using var fixture = new InstallFixture();
        fixture.ConfigureFirewallScript(configuredAfterApply: false);
        var dataRoot = Path.Combine(fixture.Root, "firewall-failure-data");

        var result = await fixture.RunAsync(
            "Install",
            dataRoot,
            configureFirewall: true);

        Assert.AreEqual(10, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.AreEqual(
            "firewallReadbackMismatch",
            document.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(File.Exists(
            Path.Combine(fixture.Root, "ligase-bootstrap.json")));
        Assert.IsFalse(Directory.Exists(dataRoot));
    }

    [TestMethod]
    public async Task FirewallIntegrationReturnsCurrentConfiguredReadback()
    {
        RequireElevatedAclIntegration();
        using var fixture = new InstallFixture();
        fixture.ConfigureFirewallScript(configuredAfterApply: true);
        var dataRoot = Path.Combine(fixture.Root, "firewall-success-data");

        var result = await fixture.RunAsync(
            "Install",
            dataRoot,
            configureFirewall: true);

        Assert.AreEqual(0, result.ExitCode, result.Output);
        using var document = JsonDocument.Parse(result.Output);
        Assert.AreEqual(
            "configured",
            document.RootElement.GetProperty("firewallState").GetString());
        Assert.AreEqual(
            "configured",
            document.RootElement.GetProperty("firewallMachineCode").GetString());
    }

    [TestMethod]
    public void NsIsOwnsOnlyLigaseIntegrationAndKeepsVirtualDisplayOptional()
    {
        var repo = FindRepositoryRoot();
        var nsis = File.ReadAllText(
            Path.Combine(repo, "packaging", "windows", "ligase", "LigaseHost.nsi"));
        var build = File.ReadAllText(
            Path.Combine(repo, "packaging", "windows", "ligase", "Build-LigaseInstaller.ps1"));
        var harness = File.ReadAllText(
            Path.Combine(
                repo,
                "packaging",
                "windows",
                "ligase",
                "LigaseInstallDirectoryHarness.nsi"));
        var resolver = File.ReadAllText(
            Path.Combine(
                repo,
                "packaging",
                "windows",
                "ligase",
                "Resolve-LigaseInstallDirectory.ps1"));
        var runtimeHarness = File.ReadAllText(
            Path.Combine(
                repo,
                "packaging",
                "windows",
                "ligase",
                "Test-LigaseInstallDirectoryRuntime.ps1"));
        var validationInclude = File.ReadAllText(
            Path.Combine(
                repo,
                "packaging",
                "windows",
                "ligase",
                "InstallDirectoryValidation.nsh"));
        var management = File.ReadAllText(
            Path.Combine(
                repo,
                "packaging",
                "windows",
                "ligase",
                "Manage-LigaseInstallation.ps1"));
        var transactionHelper = File.ReadAllText(
            Path.Combine(
                repo,
                "tools",
                "Ligase.Installation.TransactionHelper",
                "Program.cs"));

        StringAssert.Contains(nsis, "SectionIn RO");
        StringAssert.Contains(nsis, "Section /o \"Ligase 虚拟显示（可选）\"");
        StringAssert.Contains(
            nsis,
            "Section \"创建桌面快捷方式（可选）\" SEC_DESKTOP_SHORTCUT");
        StringAssert.Contains(nsis, "SetShellVarContext all");
        Assert.IsFalse(
            nsis.Contains("CreateShortcut \"$DESKTOP", StringComparison.Ordinal));
        Assert.IsFalse(
            nsis.Contains("Delete \"$DESKTOP", StringComparison.Ordinal));
        Assert.IsFalse(
            nsis.Contains("Delete \"$SMPROGRAMS", StringComparison.Ordinal));
        StringAssert.Contains(nsis, "Page custom DataRootPageCreate DataRootPageLeave");
        StringAssert.Contains(nsis, "Page custom InstallSummaryPageCreate");
        StringAssert.Contains(nsis, "Page custom InstallResultPageCreate");
        StringAssert.Contains(
            nsis,
            "Page custom InstallResultPageCreate InstallResultPageLeave");
        StringAssert.Contains(nsis, "MUI_CUSTOMFUNCTION_ABORT InstallerUserAbort");
        StringAssert.Contains(nsis, "Function .onInstFailed");
        StringAssert.Contains(nsis, "Function .onGUIEnd");
        StringAssert.Contains(nsis, "-Action RecordEvidence");
        StringAssert.Contains(nsis, "-Action FinalizeInstall");
        StringAssert.Contains(nsis, "Section -Finalize SEC_FINALIZE");
        StringAssert.Contains(nsis, "Call FinalizeInstallTerminal");
        StringAssert.Contains(nsis, "SetErrorLevel 10");
        StringAssert.Contains(nsis, "Abort");
        StringAssert.Contains(nsis, "installationFinalReadbackFailed");
        StringAssert.Contains(
            nsis,
            "安装最终读回失败。未将本次操作标记为成功");
        StringAssert.Contains(nsis, "高级：使用其他本机目录");
        StringAssert.Contains(nsis, "防火墙：Ligase 专属规则已精确验证");
        StringAssert.Contains(
            nsis,
            "SectionGetFlags ${LIGASE_SECTION_DESKTOP_SHORTCUT}");
        StringAssert.Contains(
            nsis,
            "SectionGetFlags ${LIGASE_SECTION_VIRTUAL_DISPLAY}");
        StringAssert.Contains(nsis, "preservedExistingBootstrap");
        StringAssert.Contains(nsis, "createdFreshBootstrap");
        StringAssert.Contains(nsis, "migratedToStandardDataRoot");
        StringAssert.Contains(nsis, "orphanLegacyRecovery");
        StringAssert.Contains(nsis, "recoverOrphanLegacyDataRoot");
        StringAssert.Contains(nsis, "recoveredOrphanLegacyDataRoot");
        StringAssert.Contains(nsis, "检测到未绑定的旧 Host 数据");
        StringAssert.Contains(nsis, "-RecoverOrphanDataRoot");
        StringAssert.Contains(nsis, "orphanLegacyActionRequired");
        StringAssert.Contains(
            nsis,
            "${ElseIf} $OrphanLegacyDecision == \"CreateFresh\"");
        StringAssert.Contains(
            resolver,
            "$silent -and $orphanAction -eq \"\"");
        StringAssert.Contains(resolver, "\"confirmedRecover\"");
        StringAssert.Contains(resolver, "\"confirmedCreateFresh\"");
        StringAssert.Contains(resolver, "\"proposal\"");
        StringAssert.Contains(nsis, "$6 == \"confirmedRecover\"");
        StringAssert.Contains(nsis, "$6 == \"confirmedCreateFresh\"");
        StringAssert.Contains(nsis, "Function ConsumeResolvedOrphanDecision");
        StringAssert.Contains(nsis, "Call ConsumeResolvedOrphanDecision");
        StringAssert.Contains(
            nsis,
            "$OrphanLegacyDecision != $OrphanLegacyIntent");
        Assert.IsTrue(
            nsis.LastIndexOf(
                "Call ConsumeResolvedOrphanDecision",
                StringComparison.Ordinal)
            < nsis.IndexOf("SetOutPath \"$INSTDIR\"", StringComparison.Ordinal),
            "The final resolver projection must be consumed before product writes.");
        Assert.IsFalse(
            harness.Contains(
                "${GetOptions} $0 \"/OrphanLegacyAction=\"",
                StringComparison.Ordinal),
            "The native harness must consume the resolver projection, not parse a parallel action.");
        StringAssert.Contains(
            runtimeHarness,
            "orphan-one-candidate-silent-without-action-fails-zero-write");
        StringAssert.Contains(
            runtimeHarness,
            "orphan-one-candidate-gui-proposal-has-no-confirmed-action");
        StringAssert.Contains(
            runtimeHarness,
            "orphan-one-candidate-gui-recover-consumes-confirmed-projection");
        Assert.IsFalse(
            nsis.Contains(
                "${NSD_Check} $OrphanRecoveryChoice",
                StringComparison.Ordinal)
            && !nsis.Contains(
                "${If} $OrphanLegacyDecision == \"Recover\"",
                StringComparison.Ordinal),
            "The orphan recovery radio must not receive an unconditional default.");
        StringAssert.Contains(nsis, "旧数据目录：$DataRootSource");
        StringAssert.Contains(nsis, "-MigrateDataRoot");
        StringAssert.Contains(management, "$security.SetOwner($administratorsSid)");
        StringAssert.Contains(management, "$explicitRules");
        StringAssert.Contains(
            management,
            "$_.AccessControlType -ne \"Allow\"");
        StringAssert.Contains(management, "Get-DataRootAccessState");
        StringAssert.Contains(management, "return \"wrongUser\"");
        StringAssert.Contains(management, "return \"aclDrift\"");
        StringAssert.Contains(management, "\"RecordEvidence\"");
        StringAssert.Contains(management, "\"FinalizeInstall\"");
        StringAssert.Contains(management, "function Write-InstallerEvidence");
        StringAssert.Contains(
            management,
            "Ligase Host\\Installer\\last-outcome.json");
        StringAssert.Contains(management, "candidateSourceHead");
        StringAssert.Contains(management, "timestampUtc");
        StringAssert.Contains(management, "function Get-SafePathProjection");
        StringAssert.Contains(management, "function Assert-FinalInstallReadback");
        StringAssert.Contains(management, "function Sync-OwnedShortcuts");
        StringAssert.Contains(management, "function Remove-ShortcutIfOwned");
        StringAssert.Contains(management, "Test-OwnedShortcut");
        StringAssert.Contains(management, "startMenuShortcutConflict");
        StringAssert.Contains(management, "desktopShortcutConflict");
        StringAssert.Contains(management, "function Get-ShortcutSnapshot");
        StringAssert.Contains(management, "function Restore-ShortcutTransaction");
        StringAssert.Contains(management, "function Save-InstallTransaction");
        StringAssert.Contains(management, "function Load-InstallTransaction");
        StringAssert.Contains(management, "function Invoke-InstallTransactionPreflight");
        StringAssert.Contains(management, "\"preflight\"");
        StringAssert.Contains(management, "\"PreflightInstallTransaction\"");
        StringAssert.Contains(
            management,
            "$expectedHelperHash.ToUpperInvariant()");
        StringAssert.Contains(management, "transactionHelperNativeExit");
        StringAssert.Contains(management, "transactionHelperStage");
        StringAssert.Contains(management, "transactionHelperNativeCategory");
        StringAssert.Contains(management, "transactionHelperNativeCode");
        StringAssert.Contains(management, "transactionRecoveryAction");
        StringAssert.Contains(management, "ReadAsync(");
        StringAssert.Contains(management, "StandardInput.WriteAsync($InputValue)");
        StringAssert.Contains(management, "StandardInput.FlushAsync()");
        StringAssert.Contains(management, "\"inputValidation\"");
        StringAssert.Contains(management, "[Diagnostics.Stopwatch]::StartNew()");
        StringAssert.Contains(management, "\"System32\\taskkill.exe\"");
        StringAssert.Contains(management, "\"/PID $($process.Id) /T /F\"");
        StringAssert.Contains(management, "\"processTimeout\"");
        Assert.IsTrue(
            management.IndexOf("ReadAsync(", StringComparison.Ordinal) <
            management.IndexOf(
                "$clock = [Diagnostics.Stopwatch]::StartNew()",
                StringComparison.Ordinal));
        Assert.IsTrue(
            management.IndexOf(
                "$clock = [Diagnostics.Stopwatch]::StartNew()",
                StringComparison.Ordinal) <
            management.IndexOf(
                "StandardInput.WriteAsync($InputValue)",
                StringComparison.Ordinal));
        Assert.IsFalse(management.Contains(
            "StandardOutput.ReadToEnd()",
            StringComparison.Ordinal));
        StringAssert.Contains(management, "installTransaction = \"pending\"");
        StringAssert.Contains(management, "\"installTransaction\"");
        StringAssert.Contains(management, "shortcutRollbackResult");
        StringAssert.Contains(management, "firewallRollbackResult");
        StringAssert.Contains(management, "transactionCleanupResult");
        StringAssert.Contains(
            management,
            "Deployment\\Ligase.Installation.TransactionHelper.exe");
        StringAssert.Contains(management, "Invoke-InstallTransactionHelper");
        Assert.IsFalse(
            management.Contains(
                "pending-install-transaction.json",
                StringComparison.Ordinal),
            "PowerShell must not own the transaction journal path.");
        StringAssert.Contains(management, "function Assert-TransactionRawShape");
        StringAssert.Contains(management, "function Get-ExpectedShortcutEntries");
        StringAssert.Contains(management, "manifestSha256");
        StringAssert.Contains(management, "installTransactionStale");
        StringAssert.Contains(management, "RandomNumberGenerator");
        StringAssert.Contains(build, "Ligase.Installation.TransactionHelper.csproj");
        StringAssert.Contains(
            build,
            "Deployment/Ligase.Installation.TransactionHelper.exe");
        StringAssert.Contains(transactionHelper, "FileFlagOpenReparsePoint");
        StringAssert.Contains(transactionHelper, "FileFlagBackupSemantics");
        StringAssert.Contains(transactionHelper, "GetFinalPathNameByHandleW");
        StringAssert.Contains(transactionHelper, "GetFileInformationByHandle");
        StringAssert.Contains(transactionHelper, "SetSecurityInfo");
        StringAssert.Contains(transactionHelper, "CreateDirectoryW");
        StringAssert.Contains(transactionHelper, "SecurityAttributes");
        StringAssert.Contains(
            transactionHelper,
            "new DiscretionaryAcl(directory, false, 2)");
        StringAssert.Contains(
            transactionHelper,
            "new(directory, false, ControlFlags.DiscretionaryAclProtected");
        StringAssert.Contains(transactionHelper, "OpenRecoveryDirectory");
        StringAssert.Contains(transactionHelper, "NtQueryDirectoryFile");
        StringAssert.Contains(
            transactionHelper,
            "GetFileInformationByHandleEx");
        StringAssert.Contains(transactionHelper, "FileStreamInfo");
        StringAssert.Contains(transactionHelper, "ErrorHandleEof");
        StringAssert.Contains(transactionHelper, "CreateFileW(path, access, 0");
        StringAssert.Contains(transactionHelper, "HasDirectoryEntries(handle)");
        StringAssert.Contains(transactionHelper, "HasAlternateDataStream(handle)");
        StringAssert.Contains(
            transactionHelper,
            "\"::$INDEX_ALLOCATION\"");
        StringAssert.Contains(
            transactionHelper,
            "\":$I30:$INDEX_ALLOCATION\"");
        StringAssert.Contains(
            transactionHelper,
            "inspectEmptyRootOwner");
        StringAssert.Contains(
            transactionHelper,
            "inspectEmptyRootChildren");
        StringAssert.Contains(
            transactionHelper,
            "inspectEmptyRootStreams");
        StringAssert.Contains(
            transactionHelper,
            "emptyRootInspectionReason");
        StringAssert.Contains(
            transactionHelper,
            "ownerNotAdministrators");
        StringAssert.Contains(transactionHelper, "childEntryPresent");
        StringAssert.Contains(transactionHelper, "namedDataStreamPresent");
        StringAssert.Contains(transactionHelper, "streamMetadataInvalid");
        StringAssert.Contains(
            transactionHelper,
            "next > NtQueryBufferBytes - offset");
        StringAssert.Contains(
            transactionHelper,
            "failEmptyRootStreamOffsetOverflow");
        StringAssert.Contains(
            transactionHelper,
            "failEmptyRootStreamRemainingShort");
        StringAssert.Contains(
            transactionHelper,
            "failEmptyRootStreamZeroProgress");
        StringAssert.Contains(transactionHelper, "InspectEmptyRoot(args)");
        StringAssert.Contains(transactionHelper, "inspectFixtureStreams");
        StringAssert.Contains(transactionHelper, "recoverEmptyAdminRoot");
        StringAssert.Contains(transactionHelper, "\"accessDenied\"");
        StringAssert.Contains(transactionHelper, "\"fileNotFound\"");
        StringAssert.Contains(transactionHelper, "\"pathNotFound\"");
        StringAssert.Contains(transactionHelper, "\"invalidHandle\"");
        StringAssert.Contains(transactionHelper, "\"busy\"");
        StringAssert.Contains(transactionHelper, "\"invalidParameter\"");
        StringAssert.Contains(transactionHelper, "\"privilegeNotHeld\"");
        StringAssert.Contains(transactionHelper, "\"invalidOwner\"");
        StringAssert.Contains(transactionHelper, "\"invalidAcl\"");
        StringAssert.Contains(transactionHelper, "\"identityChanged\"");
        StringAssert.Contains(transactionHelper, "\"bindingMismatch\"");
        StringAssert.Contains(transactionHelper, "\"volumeMismatch\"");
        StringAssert.Contains(transactionHelper, "\"segmentMismatch\"");
        StringAssert.Contains(transactionHelper, "\"fileIdentityMismatch\"");
        StringAssert.Contains(transactionHelper, "VolumeNameNt");
        StringAssert.Contains(
            transactionHelper,
            "trustedInfo.VolumeSerialNumber == info.VolumeSerialNumber");
        StringAssert.Contains(
            transactionHelper,
            "segments.Aggregate(");
        StringAssert.Contains(
            transactionHelper,
            "GetHandleFinalPath(trustedHandle)");
        Assert.IsFalse(transactionHelper.Contains(
            "Path.GetFullPath(final).TrimEnd",
            StringComparison.Ordinal));
        StringAssert.Contains(transactionHelper, "InspectSystemBinding");
        StringAssert.Contains(transactionHelper, "InspectSequentialBinding");
        StringAssert.Contains(
            transactionHelper,
            "installTransactionBindingValid");
        StringAssert.Contains(
            transactionHelper,
            "_bindingPrefixMatched = false;");
        StringAssert.Contains(
            transactionHelper,
            "failSecondBindingWrongVolume");
        StringAssert.Contains(
            transactionHelper,
            "failSecondBindingSegmentMismatch");
        StringAssert.Contains(
            transactionHelper,
            "failSecondBindingIdentitySwap");
        StringAssert.Contains(
            transactionHelper,
            "nativeCode is > 0 and <= ushort.MaxValue");
        StringAssert.Contains(transactionHelper, "aclMutationOccurred");
        StringAssert.Contains(transactionHelper, "aclRollback");
        StringAssert.Contains(
            transactionHelper,
            "FileListDirectory | FileReadAttributes | ReadControl |");
        StringAssert.Contains(
            transactionHelper,
            ": GenericRead | ReadControl) |");
        StringAssert.Contains(
            transactionHelper,
            "\"failOpenHandleAccessDenied\"");
        StringAssert.Contains(
            transactionHelper,
            "\"failVerifyIdentityInvalidHandle\"");
        StringAssert.Contains(
            transactionHelper,
            "\"failResolveFinalPathInvalidParameter\"");
        Assert.IsFalse(transactionHelper.Contains(
            "SetStage(\"openSegment\")", StringComparison.Ordinal));
        StringAssert.Contains(management, "transactionAclMutationOccurred");
        StringAssert.Contains(management, "transactionAclRollback");
        StringAssert.Contains(management, "bindingReason");
        StringAssert.Contains(management, "bindingRootKind");
        StringAssert.Contains(management, "bindingSegmentCount");
        StringAssert.Contains(management, "bindingPrefixMatched");
        StringAssert.Contains(management, "bindingVolumeMatched");
        StringAssert.Contains(management, "bindingFileIdentityMatched");
        StringAssert.Contains(management, "aclInspectionReason");
        StringAssert.Contains(management, "managedFailure");
        StringAssert.Contains(management, "$managedAclTuples");
        StringAssert.Contains(management, "$isExactManagedAclTuple");
        StringAssert.Contains(management, "$hasManagedAclField");
        StringAssert.Contains(management, "$emptyRootTuples");
        StringAssert.Contains(management, "$isExactEmptyRootTuple");
        StringAssert.Contains(management, "$hasEmptyRootField");
        StringAssert.Contains(
            transactionHelper,
            "emitDuplicateEmptyRootInspectionReason");
        StringAssert.Contains(
            transactionHelper,
            "emitEmptyRootTupleCrossSplice");
        StringAssert.Contains(management, "LigaseStrictJson");
        StringAssert.Contains(
            management,
            "HasUniqueProperties($stderr)");
        StringAssert.Contains(
            management,
            "new HashSet<string>(StringComparer.Ordinal)");
        Assert.IsTrue(
            management.IndexOf(
                "HasUniqueProperties($stderr)",
                StringComparison.Ordinal) <
            management.IndexOf(
                "$failure = $stderr | ConvertFrom-Json",
                StringComparison.Ordinal));
        StringAssert.Contains(
            management,
            "$nativeCode -ge 1 -and $nativeCode -le 65535");
        StringAssert.Contains(transactionHelper, "GetSecurityInfo");
        StringAssert.Contains(transactionHelper, "MoveFileExW");
        StringAssert.Contains(transactionHelper, "Ligase Host Admin");
        StringAssert.Contains(transactionHelper, "Transactions");
        StringAssert.Contains(transactionHelper, "RejectReparseChain");
        StringAssert.Contains(transactionHelper, "ValidateRoot");
        StringAssert.Contains(transactionHelper, "\"preflight\" => Preflight(store)");
        StringAssert.Contains(transactionHelper, "LIGASE_TRANSACTION_FAILURE_STAGE");
        StringAssert.Contains(transactionHelper, "LIGASE_TRANSACTION_TEST_BEHAVIOR");
        StringAssert.Contains(transactionHelper, "case \"hang\":");
        StringAssert.Contains(transactionHelper, "case \"hangBeforeStdinRead\":");
        StringAssert.Contains(transactionHelper, "case \"delayedStdinRead\":");
        StringAssert.Contains(transactionHelper, "case \"delayedPipe\":");
        StringAssert.Contains(transactionHelper, "case \"oversizeStdout\":");
        StringAssert.Contains(transactionHelper, "case \"oversizeStderr\":");
        StringAssert.Contains(transactionHelper, "case \"killTree\":");
        StringAssert.Contains(transactionHelper, "\"resolveProgramData\"");
        StringAssert.Contains(transactionHelper, "\"atomicReplace\"");
        StringAssert.Contains(transactionHelper, "\"finalReadback\"");
        foreach (var stage in new[]
                 {
                     "resolveProgramData", "rejectReparse", "createSegment",
                     "openHandle", "verifyIdentity", "resolveFinalPath",
                     "canonicalRoot", "inspectAcl", "readSecurityDescriptor",
                     "descriptorLength", "descriptorCopy", "descriptorParse",
                     "buildSecurityDescriptor", "compareSecurityDescriptor",
                     "applyAcl", "assertAcl", "createTemp",
                     "atomicReplace", "finalReadback", "read", "delete"
                 })
        {
            StringAssert.Contains(transactionHelper, $"SetStage(\"{stage}\")");
            StringAssert.Contains(management, $"\"{stage}\"");
        }
        StringAssert.Contains(
            transactionHelper,
            "installTransactionPreflightReady");
        StringAssert.Contains(
            runtimeHarness,
            "installTransactionEmptyAdminRootRecoveryFailed");
        StringAssert.Contains(
            runtimeHarness,
            "installTransactionEmptyAdminRootRecoveryReadbackFailed");
        StringAssert.Contains(
            runtimeHarness,
            "installTransactionNativeSubstageDiagnosticInvalid");
        StringAssert.Contains(
            runtimeHarness,
            "installTransactionAclInspectionDiagnosticInvalid");
        StringAssert.Contains(
            runtimeHarness,
            "installTransactionBindingDiagnosticInvalid");
        StringAssert.Contains(
            runtimeHarness,
            "\"transaction-binding-\"");
        StringAssert.Contains(
            runtimeHarness,
            "installTransactionSystemBindingReadOnlyInvalid");
        StringAssert.Contains(
            runtimeHarness,
            "installTransactionSystemAclReadOnlyInvalid");
        StringAssert.Contains(
            transactionHelper,
            "inspectSystemAcl");
        StringAssert.Contains(
            runtimeHarness,
            "installTransactionSequentialBindingIsolationInvalid");
        StringAssert.Contains(
            runtimeHarness,
            "installTransactionDuplicatePropertyAccepted");
        StringAssert.Contains(runtimeHarness, "emitDuplicateCode");
        StringAssert.Contains(runtimeHarness, "emitDuplicateNativeCode");
        StringAssert.Contains(
            runtimeHarness,
            "emitDuplicateBindingFileIdentityMatched");
        StringAssert.Contains(
            runtimeHarness,
            "emitDuplicateAclInspectionReason");
        StringAssert.Contains(
            runtimeHarness,
            "installTransactionManagedAclTupleAccepted");
        StringAssert.Contains(runtimeHarness, "emitAclTupleWrongStage");
        StringAssert.Contains(runtimeHarness, "emitAclTupleWrongCode");
        StringAssert.Contains(runtimeHarness, "emitAclTupleWrongReason");
        StringAssert.Contains(runtimeHarness, "emitAclTupleCrossSplice");
        StringAssert.Contains(
            runtimeHarness,
            "installTransactionNonemptyAdminRootAccepted");
        StringAssert.Contains(
            nsis,
            "如检测到先前安装留下的精确空目录");
        StringAssert.Contains(runtimeHarness, "\"hang\", \"delayedPipe\"");
        StringAssert.Contains(runtimeHarness, "\"oversizeStdout\"");
        StringAssert.Contains(runtimeHarness, "\"oversizeStderr\"");
        StringAssert.Contains(runtimeHarness, "\"killTree\"");
        StringAssert.Contains(
            runtimeHarness,
            "installTransactionBoundedInvocationMutatedJournal");
        StringAssert.Contains(
            runtimeHarness,
            "transactionHelperStage -cne \"processTimeout\"");
        StringAssert.Contains(management, "transactionBytes.Count -ne 32");
        StringAssert.Contains(management, "$actualEntries.Count -ne 4");
        StringAssert.Contains(nsis, "-Action FinalizeInstall");
        StringAssert.Contains(nsis, "-ConfigureFirewall $3 $4");
        StringAssert.Contains(management, "shortcutRollbackFailed");
        StringAssert.Contains(management, "firewallAppliedByTransaction");
        StringAssert.Contains(management, "failedField");
        StringAssert.Contains(management, "components = $script:finalComponents");
        StringAssert.Contains(
            management,
            "Fail-FinalInstallReadback \"startMenu\"");
        Assert.IsTrue(
            management.IndexOf(
                "    Invoke-InstallTransactionPreflight",
                StringComparison.Ordinal) <
            management.LastIndexOf(
                "Sync-OwnedShortcuts ([bool]$DesktopShortcutSelected)",
                StringComparison.Ordinal));
        Assert.IsTrue(
            management.IndexOf(
                "Sync-OwnedShortcuts ([bool]$DesktopShortcutSelected)",
                StringComparison.Ordinal) <
            management.IndexOf(
                "Invoke-FirewallAction -FirewallAction Apply",
                StringComparison.Ordinal));
        StringAssert.Contains(
            management,
            "Fail-FinalInstallReadback \"desktop\"");
        StringAssert.Contains(
            management,
            "Fail-FinalInstallReadback \"firewall\"");
        Assert.IsFalse(
            management.Contains(
                "$EvidenceFirewall = \"failed\"",
                StringComparison.Ordinal),
            "A failure before firewall readback must not be relabeled as firewall failure.");
        StringAssert.Contains(management, "$script:rollbackResult = \"failed\"");
        StringAssert.Contains(management, "installationFinalReadbackFailed");
        StringAssert.Contains(
            management,
            "function Invoke-DataRootMigration");
        StringAssert.Contains(management, "$RecoverOrphanDataRoot");
        StringAssert.Contains(management, "orphanLegacyBootstrapExists");
        StringAssert.Contains(management, "RequireAuthorityDocuments");
        Assert.IsFalse(
            management.Contains(
                "exceptionText =",
                StringComparison.OrdinalIgnoreCase),
            "Persistent installer evidence must not serialize exception text.");
        StringAssert.Contains(harness, "displayedSuccess");
        StringAssert.Contains(harness, "displayedFailure");
        StringAssert.Contains(harness, "rollbackFailure");
        StringAssert.Contains(harness, "silentProvisional");
        StringAssert.Contains(
            management,
            "-FirewallAction Remove");
        StringAssert.Contains(
            management,
            "Remove-Item -LiteralPath $bootstrapPath");
        Assert.IsFalse(
            management.Contains(
                "[Security.Principal.WindowsIdentity]::GetCurrent().User",
                StringComparison.Ordinal));
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
        StringAssert.Contains(build, "build-server shutdown");
        StringAssert.Contains(build, "desktopBuildServerShutdownFailed");
        StringAssert.Contains(build, "-p:UseSharedCompilation=false");
        StringAssert.Contains(build, "-p:UseArtifactsOutput=true");
        StringAssert.Contains(build, "-p:ArtifactsPath=$dotnetArtifacts");
        StringAssert.Contains(build, "-nodeReuse:false");
        StringAssert.Contains(build, "desktopBuildWorkspaceNotClean");
        Assert.IsFalse(build.Contains("desktopCleanFailed", StringComparison.Ordinal));
        StringAssert.Contains(build, "Test-LigaseDesktopPayload.ps1");
        StringAssert.Contains(build, "Test-LigaseDesktopStartup.ps1");
        StringAssert.Contains(build, "-Filter \"Ligase.GameWatcher.*\"");
        StringAssert.Contains(build, "tools/Ligase.GameWatcher/Ligase.GameWatcher.csproj");
        StringAssert.Contains(build, "tools/Ligase.Host.Launcher/Ligase.Host.Launcher.csproj");
        StringAssert.Contains(build, "Core/sunshine.exe");
        StringAssert.Contains(
            nsis,
            "$INSTDIR\\Ligase Host.exe");
        Assert.IsFalse(
            nsis.Contains(
                "CreateShortcut \"$SMPROGRAMS\\Ligase Host\\Ligase Host.lnk\" \"$INSTDIR\\Desktop",
                StringComparison.Ordinal));
        StringAssert.Contains(nsis, "InstallDirRegKey HKLM");
        StringAssert.Contains(nsis, "GetCommandLineW() w .r0");
        StringAssert.Contains(nsis, "/InstallDirectory=");
        StringAssert.Contains(nsis, "/DataRoot=");
        Assert.IsFalse(
            nsis.Contains(
                "${GetOptions} $0 \"/DataRoot=\"",
                StringComparison.Ordinal));
        StringAssert.Contains(nsis, "\"InstallLocation\" \"$INSTDIR\"");
        StringAssert.Contains(build, "Resolve-LigaseInstallDirectory.ps1");
        StringAssert.Contains(build, "Invoke-LigaseInstaller.ps1");
        StringAssert.Contains(build, "Test-LigaseInstallDirectoryRuntime.ps1");
        StringAssert.Contains(build, "installerArgumentRuntimeValidationFailed");
        StringAssert.Contains(build, "\"/INPUTCHARSET\"");
        StringAssert.Contains(build, "\"UTF8\"");
        StringAssert.Contains(build, "-DotNet $DotNet");
        var invoke = File.ReadAllText(
            Path.Combine(
                repo,
                "packaging",
                "windows",
                "ligase",
                "Invoke-LigaseInstaller.ps1"));
        StringAssert.Contains(invoke, "ConvertTo-WindowsCommandLineArgument");
        StringAssert.Contains(invoke, "Start-Process @startParameters");
        StringAssert.Contains(invoke, "exit $process.ExitCode");
        Assert.IsFalse(
            invoke.Contains(
                "ArgumentList = @(",
                StringComparison.Ordinal),
            "The supported invocation seam must pass one tested serialized argv string.");
        Assert.IsFalse(
            nsis.Contains("Function .onVerifyInstDir", StringComparison.Ordinal));
        var requiredSection = nsis.IndexOf(
            "Section \"Ligase Host（必需）\"",
            StringComparison.Ordinal);
        var originalValidation = nsis.IndexOf(
            "Call ResolveInstallerArguments",
            requiredSection,
            StringComparison.Ordinal);
        var finalValidation = nsis.IndexOf(
            "Call ResolveInstallerArguments",
            originalValidation + 1,
            StringComparison.Ordinal);
        var firstWrite = nsis.IndexOf(
            "SetOutPath \"$INSTDIR\"",
            requiredSection,
            StringComparison.Ordinal);
        Assert.IsTrue(requiredSection >= 0);
        Assert.IsTrue(originalValidation > requiredSection);
        Assert.IsTrue(finalValidation > originalValidation);
        Assert.IsTrue(firstWrite > finalValidation);
        StringAssert.Contains(
            harness,
            "!include \"InstallDirectoryValidation.nsh\"");
        StringAssert.Contains(validationInclude, "SetErrorLevel $0");
        StringAssert.Contains(
            validationInclude,
            "LIGASE_INSTALL_PROGRAM_DATA");
        StringAssert.Contains(
            validationInclude,
            "SetEnvironmentVariableW(w \"LIGASE_INSTALL_RAW_PARAMETERS\", w r9)");
        Assert.IsFalse(
            harness.Contains("SetOutPath \"$INSTDIR\"", StringComparison.Ordinal));
        var helper = File.ReadAllText(Path.Combine(
            repo,
            "packaging",
            "windows",
            "ligase",
            "Manage-LigaseInstallation.ps1"));
        StringAssert.Contains(helper, "throw \"dataRootRequired\"");
        StringAssert.Contains(helper, "ProcessIdToSessionId");
        StringAssert.Contains(helper, "WTSQuerySessionInformation");
        StringAssert.Contains(helper, "SetAccessRuleProtection($true, $false)");
        StringAssert.Contains(helper, "Invoke-FirewallAction");
        StringAssert.Contains(helper, "firewallReadbackMismatch");
        StringAssert.Contains(helper, "Get-OperatorDataRootSnapshot");
        StringAssert.Contains(helper, "LigaseFileIdentity]::GetLinkCount");
        StringAssert.Contains(helper, "dataRootMigrationHardLink");
        StringAssert.Contains(helper, "dataRootMigrationSourceChanged");
        StringAssert.Contains(helper, "dataRootMigrationOwnershipMismatch");
        StringAssert.Contains(helper, "Remove-OwnedMigrationDirectory");
        StringAssert.Contains(helper, "Restore-Migration");
        StringAssert.Contains(helper, "migratedToStandardDataRoot");
        StringAssert.Contains(validationInclude, "FileReadUTF16LE $3 $DataRootSource");
    }

    private static void RequireElevatedAclIntegration()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            Assert.Inconclusive(
                "The exact Administrators-owned ACL integration gate requires an elevated test token.");
        }
    }

    private static void AssertExactDataRootAcl(string root)
    {
        var security = new DirectoryInfo(root).GetAccessControl();
        Assert.IsTrue(security.AreAccessRulesProtected);
        var owner = security.GetOwner(
            typeof(SecurityIdentifier)) as SecurityIdentifier;
        Assert.IsNotNull(owner);
        Assert.AreEqual(
            new SecurityIdentifier(
                WellKnownSidType.BuiltinAdministratorsSid,
                null).Value,
            owner.Value);
        var rules = security.GetAccessRules(
                includeExplicit: true,
                includeInherited: false,
                targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        Assert.AreEqual(3, rules.Length);
        Assert.IsTrue(rules.All(rule =>
            rule.AccessControlType == AccessControlType.Allow));
    }

    private static void CleanupMigrationDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    [TestMethod]
    public async Task InstallDirectoryResolverSupportsExplicitDAndFreshProgramDataDefault()
    {
        const string explicitD = @"D:\Program Files\Ligase Host Unit";
        const string explicitDataD = @"D:\Development\Ligase Data\Host";
        const string registeredD = @"D:\Applications\Ligase Host";

        var explicitResult = await RunInstallDirectoryResolverAsync(
            $"\"/InstallDirectory={explicitD}\" \"/DataRoot={explicitDataD}\"",
            registeredD,
            @"C:\Program Files\Ligase Host");
        var upgradeResult = await RunInstallDirectoryResolverAsync(
            string.Empty,
            registeredD,
            @"C:\Program Files\Ligase Host");

        Assert.AreEqual(0, explicitResult.ExitCode, explicitResult.Error);
        Assert.AreEqual($"{explicitD}|{explicitDataD}", explicitResult.Output);
        Assert.AreEqual(0, upgradeResult.ExitCode, upgradeResult.Error);
        StringAssert.StartsWith(
            upgradeResult.Output,
            registeredD + @"|D:\ProgramDataFixture\Ligase Host\Instances\");
        StringAssert.Matches(
            upgradeResult.Output,
            new System.Text.RegularExpressions.Regex(
                @"\|D:\\ProgramDataFixture\\Ligase Host\\Instances\\" +
                @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"));
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
        var splitByCaller = await RunInstallDirectoryResolverAsync(
            "/InstallDirectory=D:\\Program Files\\Ligase Host /DataRoot=D:\\Ligase Data",
            string.Empty,
            @"C:\Program Files\Ligase Host");

        Assert.AreNotEqual(0, duplicate.ExitCode);
        Assert.AreNotEqual(0, relative.ExitCode);
        Assert.AreNotEqual(0, root.ExitCode);
        Assert.AreNotEqual(0, nonCanonical.ExitCode);
        Assert.AreNotEqual(0, splitByCaller.ExitCode);
        Assert.AreEqual("installerArgumentsInvalid", duplicate.Error);
        Assert.AreEqual("installerArgumentsInvalid", relative.Error);
        Assert.AreEqual("installerArgumentsInvalid", root.Error);
        Assert.AreEqual("installerArgumentsInvalid", nonCanonical.Error);
        Assert.AreEqual("installerArgumentsInvalid", splitByCaller.Error);
    }

    [TestMethod]
    public async Task DesktopPayloadValidatorFailsClosedWithoutGeneratedWinUiResources()
    {
        var repo = FindRepositoryRoot();
        var root = Path.Combine(
            Path.GetTempPath(),
            "ligase-desktop-payload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Ligase.Host.Desktop.exe"),
            "fixture");
        try
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
            start.ArgumentList.Add(Path.Combine(
                repo,
                "packaging",
                "windows",
                "ligase",
                "Test-LigaseDesktopPayload.ps1"));
            start.ArgumentList.Add("-DesktopDirectory");
            start.ArgumentList.Add(root);
            start.ArgumentList.Add("-SourceRoot");
            start.ArgumentList.Add(repo);
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("testProcessStartFailed");
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.AreEqual(10, process.ExitCode);
            using var document = JsonDocument.Parse(output);
            Assert.AreEqual(
                "desktopRuntimeAssetMissing",
                document.RootElement.GetProperty("code").GetString());
            Assert.IsFalse(document.RootElement.GetProperty("success").GetBoolean());
            Assert.IsFalse(output.Contains(root, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)>
        RunInstallDirectoryResolverAsync(
            string rawParameters,
            string registered,
            string defaultLocation)
    {
        var repo = FindRepositoryRoot();
        var resultPath = Path.Combine(
            Path.GetTempPath(),
            $"ligase-installer-arguments-{Guid.NewGuid():N}.txt");
        var operatorLocal = Path.Combine(
            Environment.GetEnvironmentVariable("LIGASE_BUILD_ROOT")
                ?? Path.GetTempPath(),
            "test-output",
            $"installer-operator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(operatorLocal);
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.Environment["LIGASE_INSTALL_RAW_PARAMETERS"] =
            $"\"C:\\Fixture\\Ligase Installer.exe\" {rawParameters}".TrimEnd();
        start.Environment["LIGASE_INSTALL_REGISTERED_LOCATION"] = registered;
        start.Environment["LIGASE_INSTALL_DEFAULT_LOCATION"] = defaultLocation;
        start.Environment["LIGASE_INSTALL_ARGUMENT_RESULT"] = resultPath;
        start.Environment["LIGASE_INSTALL_BOOTSTRAP_PATH"] =
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "bootstrap.json");
        start.Environment["LIGASE_INSTALL_PROGRAM_DATA"] = @"D:\ProgramDataFixture";
        start.Environment["LIGASE_INSTALL_VALIDATION_HARNESS"] = "1";
        start.Environment["LIGASE_INSTALL_TEST_OPERATOR_LOCAL_APP_DATA"] =
            operatorLocal;
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
        _ = await output;
        var resolved = File.Exists(resultPath)
            ? string.Join("|", File.ReadAllLines(resultPath).Take(2))
            : string.Empty;
        if (File.Exists(resultPath))
        {
            File.Delete(resultPath);
        }
        Directory.Delete(operatorLocal, recursive: true);
        return (process.ExitCode, resolved, await error);
    }

    private static string FindRepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("LIGASE_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var full = Path.GetFullPath(configured);
            if (File.Exists(Path.Combine(full, "CMakeLists.txt")) &&
                Directory.Exists(Path.Combine(full, ".git")))
            {
                return full;
            }
            throw new DirectoryNotFoundException("configuredRepositoryRootInvalid");
        }
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
            string? dataRoot = null,
            bool configureFirewall = false,
            bool migrateDataRoot = false,
            bool recoverOrphanDataRoot = false,
            string? recoverySource = null)
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
            if (configureFirewall)
                start.ArgumentList.Add("-ConfigureFirewall");
            if (migrateDataRoot)
                start.ArgumentList.Add("-MigrateDataRoot");
            if (recoverOrphanDataRoot)
                start.ArgumentList.Add("-RecoverOrphanDataRoot");
            if (recoverySource is not null)
            {
                start.ArgumentList.Add("-RecoveryDataRootSource");
                start.ArgumentList.Add(recoverySource);
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

        public void ConfigureFirewallScript(bool configuredAfterApply)
        {
            var directory = Path.Combine(Root, "Deployment", "Firewall");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "ligase-firewall-v1.json"),
                "{}",
                new UTF8Encoding(false));
            var configured = configuredAfterApply ? "$true" : "$false";
            File.WriteAllText(
                Path.Combine(directory, "Manage-LigaseFirewall.ps1"),
                $$"""
                param(
                  [string]$Action,
                  [string]$Manifest,
                  [string]$Program,
                  [int]$BasePort)
                $marker = Join-Path $PSScriptRoot "configured.marker"
                if ($Action -eq "Apply") {
                  if ({{configured}}) {
                    [IO.File]::WriteAllText($marker, "configured")
                  }
                  @{ code = $(if ({{configured}}) { "configured" } else { "notConfigured" });
                     configured = {{configured}} } | ConvertTo-Json -Compress
                  exit 0
                }
                if ($Action -eq "Readback") {
                  $isConfigured = Test-Path -LiteralPath $marker
                  @{ code = $(if ($isConfigured) { "configured" } else { "notConfigured" });
                     configured = $isConfigured } | ConvertTo-Json -Compress
                  exit 0
                }
                if ($Action -eq "Remove") {
                  Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
                  @{ code = "removed"; configured = $false } | ConvertTo-Json -Compress
                  exit 0
                }
                exit 1
                """,
                new UTF8Encoding(false));
        }

        public Task WriteBootstrapAsync(string dataRoot) =>
            File.WriteAllTextAsync(
                Path.Combine(Root, "ligase-bootstrap.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    dataRoot = Path.GetFullPath(dataRoot)
                }),
                new UTF8Encoding(false));

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
