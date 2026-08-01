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
        var launcher = File.ReadAllText(Path.Combine(
            root, "tools", "Ligase.SecureStore.Preflight.Launcher",
            "Program.cs"));

        StringAssert.Contains(program, "#if PREFLIGHT_ONLY");
        StringAssert.Contains(
            program,
            "private static string _stage = \"inputValidation\"");
        StringAssert.Contains(program, "_stage = \"securityInitialization\"");
        StringAssert.Contains(
            program, "_stage = \"diagnosticChannelValidation\"");
        StringAssert.Contains(program, "EnableChildProcessMitigation()");
        StringAssert.Contains(program, "ProcessChildProcessPolicy = 13");
        StringAssert.Contains(program, "NoChildProcessCreation = 1");
        StringAssert.Contains(program, "SetProcessMitigationPolicy(");
        StringAssert.Contains(program, "GetProcessMitigationPolicy(");
        StringAssert.Contains(
            program,
            "if (readback != NoChildProcessCreation)");
        StringAssert.Contains(program, "_nativeCode = 20013");
        StringAssert.Contains(program, "#if PREFLIGHT_VALIDATION");
        StringAssert.Contains(program, "ValidationArgvToken");
        StringAssert.Contains(program, "ValidationChildPolicy");
        StringAssert.Contains(program, "ValidationHangPipes");
        StringAssert.Contains(program, "ValidationPolicySetFault");
        StringAssert.Contains(program, "ValidationPolicyReadbackMismatch");
        StringAssert.Contains(program, "CreateSuspended");
        StringAssert.Contains(program, "ErrorChildProcessBlocked = 367");
        StringAssert.Contains(
            program,
            "nativeCode != ErrorChildProcessBlocked");
        StringAssert.Contains(program, "RunPreflightValidation()");
        StringAssert.Contains(program, "CreateProcessW(");
        StringAssert.Contains(program, "\\\"childCreationBlocked\\\":true");
        StringAssert.Contains(program, "\\\"processHandlesZero\\\":true");
        StringAssert.Contains(program, "TerminateProcess(");
        StringAssert.Contains(program, "WaitForSingleObject(");
        Assert.IsFalse(program.Contains("ResumeThread"));
        StringAssert.Contains(program, "childCleanupFailed");
        StringAssert.Contains(program, "validationChildPolicyV1");
        StringAssert.Contains(program, "validationChildFailureV1");
        StringAssert.Contains(program, "validationChildCleanupV1");
        StringAssert.Contains(program, "SerializeValidationChildFailure()");
        StringAssert.Contains(
            program,
            "\\\"resultCode\\\":\\\"secureStorePreflightFailed\\\"");
        StringAssert.Contains(program, "_preflightValidationChildPid");
        StringAssert.Contains(program, "_preflightValidationChildCleanup");
        StringAssert.Contains(program, "_stage = \"inputValidation\"");
        StringAssert.Contains(program, "SetStage(\"inputValidation\")");
        StringAssert.Contains(program, "TryOpenDiagnosticPipe(args)");
        StringAssert.Contains(program, "GetNamedPipeServerProcessId");
        StringAssert.Contains(program, "diagnosticPeerInvalid");
        StringAssert.Contains(program, "TrySendDiagnostic(failureBytes)");
        StringAssert.Contains(program, "KnownResidueMismatchCode = 20014");
        StringAssert.Contains(program, "AssertKnownPartialAdminRoot");
        StringAssert.Contains(program, "KnownResidueAce");
        StringAssert.Contains(
            program, "ControlFlags.DiscretionaryAclAutoInherited");
        StringAssert.Contains(
            program, "WellKnownSidType.CreatorOwnerSid");
        StringAssert.Contains(program, "AceType.AccessAllowed");
        StringAssert.Contains(program, "0x001200A9");
        StringAssert.Contains(program, "0x00000116");
        StringAssert.Contains(
            program, "StreamQueryWithoutNativeCode = 20015");
        StringAssert.Contains(
            program, "_stage = \"queryEmptyRootStreams\"");
        StringAssert.Contains(
            program, "SetStage(\"queryEmptyRootStreams\")");
        StringAssert.Contains(
            program, "\"failEmptyRootStreamQueryManaged\"");
        StringAssert.Contains(
            program, "querySucceeded = GetFileInformationByHandleEx(");
        StringAssert.Contains(
            program, "var queryError = Marshal.GetLastPInvokeError()");
        StringAssert.Contains(program, "queryError == ErrorHandleEof");
        StringAssert.Contains(
            program, "_emptyRootInspectionReason = \"streamQueryFailed\"");
        Assert.IsFalse(program.Contains(
            "KnownPartialAdminRootSddlSha256"));
        StringAssert.Contains(
            program, "_aclRollback = completed");
        StringAssert.Contains(program, "AdminRootRecoveryLease");
        StringAssert.Contains(program, "FailureDiagnosticSnapshot");
        StringAssert.Contains(program, "CaptureFailureDiagnostic()");
        StringAssert.Contains(
            program, "RestoreFailureDiagnostic(failureDiagnostic)");
        Assert.AreEqual(
            2,
            System.Text.RegularExpressions.Regex.Matches(
                program,
                @"RollbackPreservingFailure\(recoveryLease\)").Count);
        StringAssert.Contains(
            program, "HasExactAclSemantics(actual, expected)");
        StringAssert.Contains(
            program, "actualFlags != (requiredFlags |");
        StringAssert.Contains(
            program, "ControlFlags.DiscretionaryAclUntrusted");
        StringAssert.Contains(
            program, "actualAcl.Count != expectedAcl.Count");
        StringAssert.Contains(
            program, "actualAce.AceType != expectedAce.AceType");
        StringAssert.Contains(
            program, "actualAce.AccessMask != expectedAce.AccessMask");
        StringAssert.Contains(
            program, "actualAce.AceFlags != expectedAce.AceFlags");
        Assert.IsFalse(program.Contains("actual.GetSddlForm(sections)"));
        Assert.IsFalse(program.Contains("expected.GetSddlForm(sections)"));
        StringAssert.Contains(program, "RecoveryLeaseState.Unarmed");
        StringAssert.Contains(program, "RecoveryLeaseState.Frozen");
        StringAssert.Contains(program, "RecoveryLeaseState.Mutated");
        StringAssert.Contains(program, "RecoveryLeaseState.Committed");
        StringAssert.Contains(program, "failPreArmIdentity");
        StringAssert.Contains(program, "failPreArmReadSecurity");
        StringAssert.Contains(program, "failPreArmKnownResidue");
        StringAssert.Contains(program, "failEmptyRootChildPresent");
        StringAssert.Contains(program, "failEmptyRootNamedAds");
        StringAssert.Contains(program, "recoveryLease?.Commit()");
        StringAssert.Contains(program, "recoveryLease?.Rollback()");
        StringAssert.Contains(program, "failTransactionsCreate");
        StringAssert.Contains(program, "failTransactionsVerify");
        StringAssert.Contains(program, "failOpenVerified");
        Assert.IsTrue(
            program.IndexOf(
                "var store = OpenVerified(root, recoveredEmptyAdminRoot)",
                StringComparison.Ordinal) <
            program.IndexOf(
                "recoveryLease?.Commit()",
                StringComparison.Ordinal));
        Assert.IsTrue(
            program.IndexOf(
                "recoveryLease.RecordCreatedTransaction(current, identity)",
                StringComparison.Ordinal) <
            program.IndexOf(
                "InjectPostAclRecoveryFailure(\"failTransactionsVerify\")",
                StringComparison.Ordinal));
        StringAssert.Contains(
            program, "\"invalidArguments\" => \"invalidArguments\"");
        StringAssert.Contains(program, "\"diagnosticChannelRequired\" =>");
        Assert.IsTrue(
            program.IndexOf(
                "TryOpenDiagnosticPipe(args)",
                StringComparison.Ordinal) <
            program.IndexOf(
                "\"LIGASE_INSTALL_VALIDATION_HARNESS\", null",
                StringComparison.Ordinal));
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
        StringAssert.Contains(launcher, "NamedPipeServerStreamAcl.Create");
        StringAssert.Contains(launcher, "RandomNumberGenerator.GetBytes(32)");
        StringAssert.Contains(launcher, "GetNamedPipeClientProcessId");
        StringAssert.Contains(launcher, "GetProcessUserSid(child.Handle)");
        StringAssert.Contains(launcher, "Verb = \"runas\"");
        StringAssert.Contains(launcher, "#if LAUNCHER_VALIDATION");
        StringAssert.Contains(launcher, "ValidationChildFileName");
        StringAssert.Contains(launcher, "--validate-ipc-success");
        StringAssert.Contains(launcher, "--validate-ipc-early-failure");
        StringAssert.Contains(launcher, "--validate-ipc-nonce-mismatch");
        StringAssert.Contains(
            launcher, "--validate-ipc-client-pid-mismatch");
        StringAssert.Contains(
            launcher, "--validate-ipc-client-session-mismatch");
        StringAssert.Contains(
            launcher, "--validate-ipc-client-sid-mismatch");
        StringAssert.Contains(
            launcher, "--validate-ipc-server-pid-mismatch");
        StringAssert.Contains(
            launcher, "--validate-ipc-server-session-mismatch");
        StringAssert.Contains(
            launcher, "--validate-ipc-server-sid-mismatch");
        StringAssert.Contains(launcher, "--validate-ipc-frame-oversize");
        StringAssert.Contains(launcher, "--validate-ipc-disconnect");
        StringAssert.Contains(launcher, "--validate-ipc-timeout");
        StringAssert.Contains(launcher, "--validate-ipc-timeout-tree");
        StringAssert.Contains(
            launcher, "--validate-ipc-cleanup-kill-fault");
        StringAssert.Contains(
            launcher, "--validate-ipc-cleanup-snapshot-fault");
        StringAssert.Contains(
            launcher, "--validate-ipc-cleanup-wait-timeout");
        StringAssert.Contains(
            launcher, "--validate-ipc-cleanup-has-exited-fault");
        StringAssert.Contains(
            launcher, "--validate-ipc-cleanup-pid-check-fault");
        StringAssert.Contains(
            launcher, "child.Kill(entireProcessTree: true)");
        StringAssert.Contains(
            launcher, "VerifyProcessTreeAbsent(treePids)");
        StringAssert.Contains(launcher, "CreateToolhelp32Snapshot");
        StringAssert.Contains(launcher, "\\\"killAttempted\\\"");
        StringAssert.Contains(launcher, "\\\"rootFallbackAttempted\\\"");
        StringAssert.Contains(launcher, "\\\"rootPidZero\\\"");
        StringAssert.Contains(launcher, "\\\"withinDeadline\\\"");
        StringAssert.Contains(launcher, "UseShellExecute = false");
        StringAssert.Contains(launcher, "CreateNoWindow = true");
        StringAssert.Contains(launcher, "Arguments = \"--diagnostic-pipe \"");
        StringAssert.Contains(
            launcher, "secure-store-preflight-review-evidence.json");
        Assert.IsFalse(launcher.Contains("payloadRoot"));
        Assert.IsFalse(launcher.Contains("Manage-LigaseInstallation"));
        StringAssert.Contains(
            program,
            "\"LIGASE_INSTALL_VALIDATION_HARNESS\", null");
        StringAssert.Contains(program, "#if !PREFLIGHT_ONLY");
        StringAssert.Contains(project, "PREFLIGHT_ONLY");
        StringAssert.Contains(project, "<PublishTrimmed>true</PublishTrimmed>");
        StringAssert.Contains(project, "<SelfContained>true</SelfContained>");
        StringAssert.Contains(project, "IL2026;IL3050");
        Assert.IsFalse(project.Contains("PREFLIGHT_VALIDATION"));
        var launcherProject = File.ReadAllText(Path.Combine(
            root, "tools", "Ligase.SecureStore.Preflight.Launcher",
            "Ligase.SecureStore.Preflight.Launcher.csproj"));
        Assert.IsFalse(launcherProject.Contains("LAUNCHER_VALIDATION"));
        Assert.IsFalse(program.Contains("JsonSerializer"));
        StringAssert.Contains(program, "private static void AppendJsonString(");
        StringAssert.Contains(program, "secureStorePreflightEncodingFailed");
        StringAssert.Contains(sourceGate, "secureStorePreflightForbiddenSurface");
        StringAssert.Contains(sourceGate, "transactionHelperInitialStageDrifted");
        StringAssert.Contains(
            sourceGate,
            "$ErrorActionPreference = 'Stop'");
        Assert.IsFalse(sourceGate.Contains(".ArgumentList"));
        StringAssert.Contains(
            sourceGate,
            "$start.Arguments = $Token");
        StringAssert.Contains(
            sourceGate,
            "secureStorePreflightUnsupportedArgumentApi");
        StringAssert.Contains(
            sourceGate,
            "secureStorePreflightUnsupportedApiStartedProcess");
        StringAssert.Contains(
            sourceGate,
            "secureStorePreflightValidationSeamInProductionArtifact");
        StringAssert.Contains(sourceGate, "productionDiagnosticRequiredV1");
        StringAssert.Contains(
            sourceGate,
            "gateEvidenceDiagnosticRequiredReadbackDrift");
        StringAssert.Contains(
            sourceGate,
            "gateEvidenceDiagnosticRequiredSchemaAccepted");
        StringAssert.Contains(
            sourceGate,
            "'launcher', 'launcherValidation'");
        StringAssert.Contains(
            sourceGate,
            "gateEvidenceLauncherProjectionRejected");
        StringAssert.Contains(
            sourceGate,
            "gateEvidenceLauncherReadbackDrift");
        StringAssert.Contains(
            sourceGate,
            "gateEvidenceLauncherInvalidReadbackDrift");
        StringAssert.Contains(
            sourceGate,
            "gateEvidenceLauncherKindSchemaAccepted");
        StringAssert.Contains(
            sourceGate,
            "invalid-production-launcher");
        StringAssert.Contains(
            sourceGate,
            "invalid-launcher-production");
        StringAssert.Contains(
            sourceGate,
            "diagnostic-required-wrong-tuple");
        StringAssert.Contains(sourceGate, "launcherFailureV1");
        StringAssert.Contains(sourceGate, "launcherIpcValidationV1");
        StringAssert.Contains(sourceGate, "$LauncherArtifact");
        StringAssert.Contains(sourceGate, "$LauncherValidationArtifact");
        StringAssert.Contains(
            sourceGate, "production-diagnostic-channel-required");
        StringAssert.Contains(sourceGate, "launcher-production-extra-argv");
        StringAssert.Contains(sourceGate, "--validate-ipc-timeout-tree");
        StringAssert.Contains(
            sourceGate,
            "secureStorePreflightLauncherCleanupNotClosed");
        StringAssert.Contains(
            sourceGate, "launcher-production-validation-seam-scan");
        StringAssert.Contains(
            sourceGate,
            "Get-SafeAdminRootFingerprint");
        StringAssert.Contains(
            sourceGate,
            "$entry -is [IO.FileInfo]");
        StringAssert.Contains(
            sourceGate,
            "secureStorePreflightNestedFingerprintInvalid");
        StringAssert.Contains(
            sourceGate,
            "ReadToEndAsync()");
        StringAssert.Contains(
            sourceGate,
            "Diagnostics.Stopwatch");
        StringAssert.Contains(
            sourceGate,
            "secureStorePreflightArtifactHangNotBounded");
        StringAssert.Contains(
            sourceGate,
            "secureStorePreflightCleanupFailed");
        StringAssert.Contains(
            sourceGate,
            "secureStorePreflightTaskkillWaitFailed");
        StringAssert.Contains(
            sourceGate,
            "secureStorePreflightTaskkillResidue");
        StringAssert.Contains(
            sourceGate,
            "secureStorePreflightCleanupFaultNotBounded");
        StringAssert.Contains(sourceGate, "taskkillHang");
        StringAssert.Contains(sourceGate, "taskkillExitNonzero");
        StringAssert.Contains(sourceGate, "targetKillFailure");
        StringAssert.Contains(sourceGate, "targetWaitTimeout");
        Assert.IsFalse(sourceGate.Contains("ping -n"));
        Assert.IsFalse(sourceGate.Contains("System32\\cmd.exe"));
        StringAssert.Contains(
            sourceGate,
            "secureStorePreflightValidationChildResumePresent");
        StringAssert.Contains(
            sourceGate,
            "$childResult.processHandlesZero");
        StringAssert.Contains(
            sourceGate,
            "function New-ArtifactGateObservation");
        StringAssert.Contains(
            sourceGate,
            "function Write-ArtifactGateObservation");
        StringAssert.Contains(
            sourceGate,
            "function Write-ArtifactGateCleanupObservation");
        StringAssert.Contains(
            sourceGate,
            "function Test-ClosedArtifactGateObservation");
        StringAssert.Contains(sourceGate, "function Test-FixedTimeSha256");
        StringAssert.Contains(sourceGate, "$ExpectedFirstSha256");
        StringAssert.Contains(sourceGate, "firstEvidenceShaMismatch");
        StringAssert.Contains(sourceGate, "firstEvidenceJsonInvalid");
        StringAssert.Contains(sourceGate, "firstEvidenceShapeInvalid");
        StringAssert.Contains(
            sourceGate,
            "function Complete-ArtifactGate");
        StringAssert.Contains(
            sourceGate,
            "function ConvertTo-ClosedArtifactProjection");
        StringAssert.Contains(
            sourceGate,
            "public static class LigaseArtifactStrictJson");
        StringAssert.Contains(
            sourceGate,
            "IsUniqueAndComplete");
        StringAssert.Contains(sourceGate, "StringComparer.Ordinal");
        StringAssert.Contains(sourceGate, "productionInvalidArgumentsV1");
        StringAssert.Contains(sourceGate, "productionPolicySetFailureV1");
        StringAssert.Contains(
            sourceGate,
            "productionPolicyReadbackFailureV1");
        StringAssert.Contains(sourceGate, "validationPolicySetFailureV1");
        StringAssert.Contains(
            sourceGate,
            "validationPolicyReadbackFailureV1");
        StringAssert.Contains(
            sourceGate,
            "gateEvidenceValidationPolicyProjectionRejected");
        StringAssert.Contains(
            sourceGate,
            "gateEvidenceValidationPolicyReadbackDrift");
        StringAssert.Contains(
            sourceGate,
            "gateEvidenceValidationPolicyKindSchemaAccepted");
        StringAssert.Contains(
            sourceGate,
            "validation-policy-set-wrong-stage");
        StringAssert.Contains(
            sourceGate,
            "validation-policy-readback-wrong-code");
        StringAssert.Contains(sourceGate, "validationArgvV1");
        StringAssert.Contains(sourceGate, "validationChildPolicyV1");
        StringAssert.Contains(sourceGate, "validationChildFailureV1");
        StringAssert.Contains(sourceGate, "validationChildCleanupV1");
        StringAssert.Contains(
            sourceGate,
            "function Get-ClosedChildArtifactDiscriminator");
        StringAssert.Contains(
            sourceGate,
            "function ConvertTo-DiscriminatedChildProjection");
        StringAssert.Contains(sourceGate, "discriminatorSyntaxOrDuplicate");
        StringAssert.Contains(sourceGate, "discriminatorValue");
        StringAssert.Contains(sourceGate, "observedPropertyCount");
        StringAssert.Contains(sourceGate, "propertyNameSetHash");
        StringAssert.Contains(sourceGate, "declaredSchemaId");
        StringAssert.Contains(sourceGate, "observedResultCode");
        StringAssert.Contains(sourceGate, "observedStage");
        StringAssert.Contains(sourceGate, "observedNativeCode");
        StringAssert.Contains(sourceGate, "$value.nativeCode -eq 367");
        StringAssert.Contains(sourceGate, "$childResult.nativeCode -eq 367");
        StringAssert.Contains(sourceGate, "child-code-five");
        StringAssert.Contains(sourceGate, "child-code-other");
        StringAssert.Contains(sourceGate, "self-test-failure-flow");
        StringAssert.Contains(sourceGate, "gateEvidenceFailureFlowContinued");
        Assert.IsFalse(sourceGate.Contains("exit$x"));
        Assert.IsFalse(sourceGate.Contains("throwmodeInvalid"));
        StringAssert.Contains(
            sourceGate,
            "secureStorePreflightChildPolicyMachineFailure");
        StringAssert.Contains(
            sourceGate,
            "self-test-child-discriminator-");
        StringAssert.Contains(sourceGate, "duplicate-conflict");
        Assert.IsFalse(sourceGate.Contains(
            "if ($child.exitCode -eq 0) {" +
            Environment.NewLine +
            "        $childSchemaId = 'validationChildPolicyV1'"));
        StringAssert.Contains(sourceGate, "syntaxOrDuplicate");
        StringAssert.Contains(sourceGate, "propertySet");
        StringAssert.Contains(sourceGate, "propertyType");
        StringAssert.Contains(sourceGate, "schemaId");
        StringAssert.Contains(sourceGate, "parseReason");
        StringAssert.Contains(sourceGate, "stdoutLength");
        StringAssert.Contains(sourceGate, "stderrLength");
        StringAssert.Contains(sourceGate, "gateEvidenceProvenanceInvalid");
        StringAssert.Contains(sourceGate, "cleanupProvenanceMismatch");
        StringAssert.Contains(sourceGate, "self-test-no-parse-inference");
        StringAssert.Contains(sourceGate, "self-test-wrong-schema");
        StringAssert.Contains(sourceGate, "self-test-cleanup-hash-drift");
        StringAssert.Contains(sourceGate, "self-test-first-tamper-");
        StringAssert.Contains(sourceGate, "gateEvidenceTamperCleanupCreated");
        StringAssert.Contains(sourceGate, "production-duplicate-same");
        StringAssert.Contains(sourceGate, "production-duplicate-conflict");
        StringAssert.Contains(sourceGate, "production-unknown");
        StringAssert.Contains(sourceGate, "production-missing");
        StringAssert.Contains(sourceGate, "production-type");
        StringAssert.Contains(sourceGate, "production-negative");
        StringAssert.Contains(sourceGate, "production-overflow");
        StringAssert.Contains(sourceGate, "production-trailing");
        StringAssert.Contains(sourceGate, "argv-policy-duplicate");
        StringAssert.Contains(sourceGate, "child-handles-type");
        StringAssert.Contains(sourceGate, "cleanup-pid-duplicate");
        StringAssert.Contains(sourceGate, "production-ready-code");
        StringAssert.Contains(sourceGate, "production-success-true");
        StringAssert.Contains(sourceGate, "production-stage-drift");
        StringAssert.Contains(sourceGate, "production-acl-drift");
        StringAssert.Contains(sourceGate, "production-recovery-drift");
        StringAssert.Contains(sourceGate, "production-probe-drift");
        StringAssert.Contains(sourceGate, "production-cleanup-drift");
        StringAssert.Contains(sourceGate, "production-rollback-drift");
        StringAssert.Contains(sourceGate, "policy-set-cross-code");
        StringAssert.Contains(sourceGate, "policy-readback-cross-category");
        StringAssert.Contains(sourceGate, "semanticTupleMismatch");
        StringAssert.Contains(sourceGate, "function Test-ArtifactNativeTuple");
        StringAssert.Contains(
            sourceGate,
            "gateEvidenceCanonicalCleanupRejected");
        StringAssert.Contains(sourceGate, "failed-exit-zero");
        StringAssert.Contains(sourceGate, "success-exit-eighteen");
        StringAssert.Contains(sourceGate, "cleanup-exit-zero");
        StringAssert.Contains(sourceGate, "child-policy-exit-eighteen");
        StringAssert.Contains(sourceGate, "cleanup-stderr-empty");
        StringAssert.Contains(sourceGate, "cleanup-wrong-schema");
        Assert.IsTrue(
            sourceGate.IndexOf(
                "$firstFailureSha = Write-ArtifactGateObservation",
                StringComparison.Ordinal) <
            sourceGate.IndexOf(
                "$childDeadline = [Diagnostics.Stopwatch]::StartNew()",
                StringComparison.Ordinal));
        Assert.IsTrue(
            sourceGate.IndexOf(
                "$childCleanupObservation $firstFailureSha",
                StringComparison.Ordinal) >
            sourceGate.IndexOf(
                "$childDeadline = [Diagnostics.Stopwatch]::StartNew()",
                StringComparison.Ordinal));
        StringAssert.Contains(sourceGate, "gateEvidenceUnavailable");
        StringAssert.Contains(sourceGate, "gateEvidenceDuplicateProperty");
        StringAssert.Contains(sourceGate, "gateEvidenceReadbackInvalid");
        StringAssert.Contains(sourceGate, "evidenceSha256=");
        StringAssert.Contains(sourceGate, ".first.json");
        StringAssert.Contains(sourceGate, "{ '.cleanup' } else { '.first' }");
        StringAssert.Contains(sourceGate, "[IO.File]::Move($temp, $destination)");
        StringAssert.Contains(
            sourceGate,
            "self-test-child-field-failure");
        StringAssert.Contains(
            sourceGate,
            "$id = 'self-test-child-' + [string]$case.id");
        StringAssert.Contains(sourceGate, "@{ id = 'policy'");
        StringAssert.Contains(sourceGate, "@{ id = 'handles'");
        StringAssert.Contains(sourceGate, "@{ id = 'pid'");
        StringAssert.Contains(sourceGate, "@{ id = 'sentinel'");
        StringAssert.Contains(sourceGate, "@{ id = 'cleanup'");
        StringAssert.Contains(sourceGate, "stdoutSha256");
        StringAssert.Contains(sourceGate, "stderrSha256");
        StringAssert.Contains(sourceGate, "parseState");
        StringAssert.Contains(sourceGate, "elapsedMilliseconds");
        Assert.IsFalse(sourceGate.Contains("ProgramData\\Ligase Host"));
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
        StringAssert.Contains(
            management,
            "Write-Outcome \"installationFinalized\" $true ([ordered]@{");
        Assert.IsFalse(
            management.Contains(
                "Write-Outcome \"installationFinalized\" $true @{",
                StringComparison.Ordinal));
        StringAssert.Contains(harness, "nsExec::ExecToStack");
        StringAssert.Contains(harness, "Pop $0");
        StringAssert.Contains(harness, "Pop $1");
        StringAssert.Contains(
            harness,
            "${AndIf} $1 == '{\"code\":\"installationFinalized\",\"success\":true,\"dataRootState\":\"existing\",\"firewallState\":\"configured\"}'");
        StringAssert.Contains(
            runtimeHarness,
            "finalizationStackCases = $finalizationStackResults");
        StringAssert.Contains(
            runtimeHarness,
            "-Mode \"success\" -ExpectedVerdict \"passed\"");
        StringAssert.Contains(
            runtimeHarness,
            "-Mode \"extra\" -ExpectedVerdict \"failed\"");
        StringAssert.Contains(
            runtimeHarness,
            "-Mode \"malformed\" -ExpectedVerdict \"failed\"");
        StringAssert.Contains(
            runtimeHarness,
            "-Mode \"nonzero\" -ExpectedVerdict \"failed\"");
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
        StringAssert.Contains(
            management,
            "queryEmptyRootStreams = @(20015, \"streamQueryFailed\")");
        StringAssert.Contains(
            management,
            "\"queryEmptyRootStreams\", \"inspectEmptyRootStreams\"");
        StringAssert.Contains(
            management,
            "20008, 20009, 20010, 20011, 20015");
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
            "catch [UnauthorizedAccessException]");
        StringAssert.Contains(
            runtimeHarness,
            "passed = $systemBindingReadable");
        StringAssert.Contains(
            runtimeHarness,
            "inconclusive = -not $systemBindingReadable");
        StringAssert.Contains(
            runtimeHarness,
            "passed = $systemAclInspectionReadable");
        StringAssert.Contains(
            runtimeHarness,
            "inconclusive = -not $systemAclInspectionReadable");
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
        Assert.AreEqual(
            3,
            System.Text.RegularExpressions.Regex.Matches(
                nsis,
                @"-Action Install -InstallDirectory .*?-ConfigureFirewall \$3 \$4").Count,
            "Every Install mode must freeze desktop and virtual-display selection in the transaction.");
        Assert.AreEqual(
            2,
            System.Text.RegularExpressions.Regex.Matches(
                nsis,
                @"SectionGetFlags \$\{LIGASE_SECTION_VIRTUAL_DISPLAY\} \$2").Count,
            "Install and Finalize must independently read the same section selection.");
        StringAssert.Contains(management, "readbackStage = $script:transactionReadbackStage");
        StringAssert.Contains(management, "readbackReason = $script:transactionReadbackReason");
        foreach (var token in new[]
                 {
                     "\"rawRead\"", "\"rawShape\"", "\"jsonParse\"",
                     "\"schemaValidation\"", "\"freshnessValidation\"",
                     "\"identityValidation\"", "\"shortcutValidation\"",
                     "\"cleanup\"", "\"missing\"", "\"duplicateProperty\"",
                     "\"missingProperty\"", "\"malformedJson\"",
                     "\"unknownProperty\"", "\"wrongType\"", "\"stale\"",
                     "\"helperFailure\"", "\"identityMismatch\"",
                     "\"shortcutSnapshotInvalid\"", "\"cleanupFailed\""
                 })
        {
            StringAssert.Contains(management, token);
        }
        var virtualReadbackIndex = management.IndexOf(
            "$displayReadback = Get-VirtualDisplay",
            StringComparison.Ordinal);
        var virtualMarkerIndex = management.IndexOf(
            "Write-VirtualDisplayOwnershipMarkerAtomic $ownershipPath",
            virtualReadbackIndex,
            StringComparison.Ordinal);
        Assert.IsTrue(virtualReadbackIndex >= 0);
        Assert.IsTrue(
            virtualMarkerIndex > virtualReadbackIndex,
            "Virtual-display device and driver binding must be verified before ownership marker commit.");
        StringAssert.Contains(management, "\"virtualDisplayReadbackFailed\"");
        StringAssert.Contains(management, "\"virtualDisplayMarkerCommitFailed\"");
        StringAssert.Contains(management, "\"virtualDisplayRollbackFailed\"");
        StringAssert.Contains(management, "\"virtualDisplayDeviceCountInvalid\"");
        StringAssert.Contains(management, "\"virtualDisplayDeviceRemoveFailed\"");
        StringAssert.Contains(management, "\"virtualDisplayDeviceRemoveReadbackFailed\"");
        StringAssert.Contains(management, "\"virtualDisplayDeviceRemoveSettleFailed\"");
        StringAssert.Contains(management, "\"virtualDisplayDeviceZeroProofFailed\"");
        StringAssert.Contains(management, "\"virtualDisplayDeviceRemoveFallbackFailed\"");
        StringAssert.Contains(management, "Invoke-VirtualDisplayRemovalReconciliation");
        StringAssert.Contains(management,
            "$beforeCount = [int]$before.deviceCount");
        StringAssert.Contains(management,
            "if ($beforeCount -eq 0)");
        StringAssert.Contains(management,
            "$zeroProofSamples -lt 3");
        StringAssert.Contains(management,
            "$zeroReadbackCount -ne 0");
        StringAssert.Contains(management,
            "if ($totalClock.ElapsedMilliseconds -ge $TotalMilliseconds)");
        StringAssert.Contains(management,
            "[string]$zeroReadback.uniqueDeviceIdsSha256 -cne $zeroEpoch");
        StringAssert.Contains(management,
            "if ($afterCount -lt $beforeCount)");
        StringAssert.Contains(management,
            "$env:LIGASE_VDISPLAY_ACTION = \"removeOne\"");
        StringAssert.Contains(management,
            "$env:LIGASE_VDISPLAY_ACTION = \"removeInstance\"");
        StringAssert.Contains(management,
            "$env:LIGASE_VDISPLAY_INSTANCE_ID = $InstanceId");
        StringAssert.Contains(management,
            "[LigaseFileIdentity]::GetTrustedPnPUtilPath()");
        StringAssert.Contains(management, "GetSystemDirectoryW");
        StringAssert.Contains(management, "GetFinalPathNameByHandleW");
        StringAssert.Contains(management,
            "$env:LIGASE_VDISPLAY_PNPUTIL = $trustedPnPUtil");
        StringAssert.Contains(management,
            "$env:LIGASE_VDISPLAY_ACTION = \"install\"");
        StringAssert.Contains(management, "uniqueDeviceIdsSha256");
        StringAssert.Contains(management, "Write-VirtualDisplayDiagnostic");
        StringAssert.Contains(management, "Read-VirtualDisplayDiagnostic");
        StringAssert.Contains(management, "ConvertTo-VirtualDisplayDiagnosticToken");
        StringAssert.Contains(management, "ConvertFrom-VirtualDisplayDiagnosticToken");
        StringAssert.Contains(management,
            "[A-Za-z0-9_-]{1,4096}");
        StringAssert.Contains(management, "\"vd1.$payload.");
        StringAssert.Contains(nsis,
            "-VirtualDisplayDiagnosticToken \"$VirtualDisplayDiagnosticToken\"");
        StringAssert.Contains(management,
            "LIGASE_VIRTUAL_DISPLAY_DIAGNOSTIC_WRITE_FAULT");
        StringAssert.Contains(management, "candidateSourceHead = Get-EvidenceSourceHead");
        StringAssert.Contains(management, "virtualDisplayDiagnosticInvalid");
        StringAssert.Contains(management, "\"ValidateVirtualDisplayReadback\"");
        StringAssert.Contains(runtimeHarness, "virtualDisplayReadbackCases");
        StringAssert.Contains(runtimeHarness, "Resolve-TransactionFixturePhysicalPath");
        StringAssert.Contains(runtimeHarness, "\"transaction-fixtures\"");
        StringAssert.Contains(runtimeHarness, "$transactionFixtureRoot");
        StringAssert.Contains(management, "[StringComparer]::OrdinalIgnoreCase.Equals(");
        Assert.IsFalse(
            management.Contains(
                "[string]$_ -ceq \"root\\sudomaker\\sudovda\"",
                StringComparison.Ordinal),
            "Windows PnP hardware identity must use case-insensitive exact comparison.");
        StringAssert.Contains(runtimeHarness, "\"exactOneUppercase\"");
        StringAssert.Contains(runtimeHarness, "\"ROOT\\SUDOMAKER\\SUDOVDA\"");
        StringAssert.Contains(runtimeHarness, "\"exactOneMixedCase\"");
        StringAssert.Contains(runtimeHarness, "\"Root\\SudoMaker\\SudoVDA\"");
        StringAssert.Contains(runtimeHarness, "\"duplicate\"");
        StringAssert.Contains(runtimeHarness, "\"bindingMissing\"");
        StringAssert.Contains(runtimeHarness,
            "\"virtual-display-diagnostic-projection\"");
        StringAssert.Contains(runtimeHarness,
            "\"virtualDisplayDiagnosticProjectionValidated\"");
        StringAssert.Contains(runtimeHarness,
            "\"virtualDisplayDeviceCountInvalid\"");
        StringAssert.Contains(runtimeHarness,
            "\"virtualDisplayDeviceRemoveFailed\"");
        StringAssert.Contains(runtimeHarness, "\"deviceRemove\"");
        StringAssert.Contains(runtimeHarness, "virtualDisplayRemovalCases");
        StringAssert.Contains(runtimeHarness, "\"exit6Fallback\"");
        StringAssert.Contains(runtimeHarness, "\"exit6FallbackFailed\"");
        StringAssert.Contains(runtimeHarness, "\"fallbackTimeoutPreTuple\"");
        StringAssert.Contains(runtimeHarness, "\"fallbackOutputPreTuple\"");
        StringAssert.Contains(runtimeHarness, "\"fallbackOverflowPreTuple\"");
        StringAssert.Contains(runtimeHarness, "\"fallbackUnavailablePreTuple\"");
        StringAssert.Contains(runtimeHarness, "\"fallbackCleanupPreTuple\"");
        StringAssert.Contains(runtimeHarness, "\"fallbackTrustedToolPreTuple\"");
        StringAssert.Contains(runtimeHarness, "\"postRemoveReadbackFailure\"");
        StringAssert.Contains(runtimeHarness, "\"stableZero\"");
        StringAssert.Contains(runtimeHarness, "\"transientZeroToTwo\"");
        StringAssert.Contains(runtimeHarness, "\"zeroIdentityEpochDrift\"");
        StringAssert.Contains(runtimeHarness,
            "\"lastZeroSampleCrossesDeadline\"");
        StringAssert.Contains(runtimeHarness,
            "\"virtualDisplayTrustedToolValidated\"");
        StringAssert.Contains(runtimeHarness, "\"settleProgress\"");
        StringAssert.Contains(runtimeHarness, "\"settleTimeout\"");
        StringAssert.Contains(runtimeHarness, "removeCount -ne 16");
        StringAssert.Contains(nsis, "Var VirtualDisplayDiagnosticToken");
        StringAssert.Contains(nsis,
            "-VirtualDisplayDiagnosticToken \"$VirtualDisplayDiagnosticToken\"");
        StringAssert.Contains(nsis,
            "{\"code\":\"virtualDisplayInstalled\",\"success\":true}");
        StringAssert.Contains(management, "driverBindingVerified");
        StringAssert.Contains(management, "deviceRecovery");
        StringAssert.Contains(management, "residualDeviceState");
        StringAssert.Contains(management, "compensationState");
        StringAssert.Contains(management, "fallbackStage");
        StringAssert.Contains(management, "fallbackReason");
        StringAssert.Contains(management, "compensationFailureReason");
        StringAssert.Contains(management, "terminalReadbackState");
        StringAssert.Contains(management, "terminalReadbackReason");
        StringAssert.Contains(management, "createInvocationCount");
        StringAssert.Contains(management, "createInvocationIdSha256");
        StringAssert.Contains(management, "preCreateIdentitySha256");
        StringAssert.Contains(management, "postCreateIdentitySha256");
        StringAssert.Contains(management, "postCreateIdentityState");
        StringAssert.Contains(management, "postCreateIdentityReason");
        StringAssert.Contains(management, "presentDeviceCount");
        StringAssert.Contains(management, "inventoryFailureStage");
        StringAssert.Contains(management, "inventoryOutputReason");
        StringAssert.Contains(management, "inventoryNativeExitCode");
        StringAssert.Contains(management, "inventoryChildFailureStage");
        StringAssert.Contains(management, "inventoryCoverageStage");
        StringAssert.Contains(management, "inventoryCoverageReason");
        StringAssert.Contains(management, "inventoryRequestedCount");
        StringAssert.Contains(management, "inventoryReturnedCount");
        StringAssert.Contains(management, "inventoryCurrentBatchIndex");
        StringAssert.Contains(management, "inventoryTotalBatchCount");
        StringAssert.Contains(management,
            "inventoryHardwareBatchesCompleted");
        StringAssert.Contains(management,
            "inventoryDriverBatchesCompleted");
        StringAssert.Contains(management, "inventoryRunBudgetMilliseconds");
        StringAssert.Contains(management, "inventoryHardCapMilliseconds");
        StringAssert.Contains(management, "inventoryCleanupState");
        StringAssert.Contains(management, "inventoryRootPidZero");
        StringAssert.Contains(management, "inventoryJobActiveProcesses");
        StringAssert.Contains(management, "Get-PnpDevice -ErrorAction Stop");
        StringAssert.Contains(management, "\"DEVPKEY_Device_HardwareIds\"");
        StringAssert.Contains(management, "\"DEVPKEY_Device_DriverInfPath\"");
        StringAssert.Contains(management,
            "$virtualDisplayPropertyBatchSize = 32");
        StringAssert.Contains(management,
            "$virtualDisplayPropertyInventoryDeadlineMilliseconds = 10000");
        StringAssert.Contains(management,
            "$virtualDisplayPropertyCleanupReserveMilliseconds = 1000");
        StringAssert.Contains(management,
            "function Invoke-VirtualDisplayPropertyBatchProcess(");
        StringAssert.Contains(management,
            "[LigaseJobProcess]::StartExact(");
        StringAssert.Contains(management,
            "$startCleanupBudget");
        StringAssert.Contains(management,
            "[LigaseJobProcess]::AuthorityRetained");
        StringAssert.Contains(management,
            "[LigaseJobProcess]::SecondaryContainment(");
        StringAssert.Contains(management,
            "[LigaseFileIdentity]::GetTrustedWindowsPowerShellPath()");
        StringAssert.Contains(management,
            "$job.Terminate()");
        StringAssert.Contains(management,
            "$job.HasNoActiveProcesses()");
        StringAssert.Contains(management,
            "Get-PnpDeviceProperty -InstanceId ([string[]]$p.instanceIds)");
        StringAssert.Contains(management,
            "PropertyState=\"absent\";Data=$null");
        StringAssert.Contains(management,
            "PropertyState=$value.State;Data=$value.Data");
        StringAssert.Contains(management,
            "function Get-VirtualDisplayPropertyRowsChunked(");
        StringAssert.Contains(management,
            "[StringComparer]::Ordinal)");
        StringAssert.Contains(management,
            "$rows.Count -ne $batch.Count");
        StringAssert.Contains(management,
            "-not $batchRequested.Contains($returnedId)");
        Assert.IsFalse(management.Contains(
            "$allRows.Count -ne $allRequested.Count",
            StringComparison.Ordinal));
        StringAssert.Contains(management,
            "-ValidationForcePostLoopDeadline");
        Assert.IsFalse(management.Contains(
            "Get-PnpDeviceProperty -InstanceId $instanceIds",
            StringComparison.Ordinal));
        StringAssert.Contains(management,
            "return $env:LIGASE_VIRTUAL_DISPLAY_DEPENDENT_DEVICE -ceq \"1\"");
        Assert.IsFalse(management.Contains(
            "Get-PnpDevice -PresentOnly -ErrorAction Stop",
            StringComparison.Ordinal));
        Assert.IsFalse(management.Contains(
            "$_.FriendlyName -match \"SudoVDA|Virtual Display\"",
            StringComparison.Ordinal));
        StringAssert.Contains(management,
            "$script:virtualDisplayCreateInvocationCount = 1");
        StringAssert.Contains(management,
            "$script:virtualDisplayPreCreateIdentitySha256 =");
        StringAssert.Contains(management,
            "$script:virtualDisplayPostCreateIdentitySha256 =");
        StringAssert.Contains(management,
            "Set-VirtualDisplayTerminalResidualAuthority");
        StringAssert.Contains(management,
            "Assert-VirtualDisplayDiagnosticCorrelation");
        StringAssert.Contains(management, "\"fallbackContradiction\"");
        StringAssert.Contains(management, "\"compensationContradiction\"");
        StringAssert.Contains(management, "\"terminalContradiction\"");
        StringAssert.Contains(management,
            "\"terminalZeroHashContradiction\"");
        StringAssert.Contains(management,
            "\"terminalPositiveHashContradiction\"");
        StringAssert.Contains(management,
            "\"createInvocationAbsentContradiction\"");
        StringAssert.Contains(management,
            "\"createInvocationPresentContradiction\"");
        StringAssert.Contains(management,
            "\"createInvocationPostIdentityContradiction\"");
        StringAssert.Contains(management,
            "\"createInvocationPostFailureContradiction\"");
        StringAssert.Contains(management, "\"presentCountContradiction\"");
        StringAssert.Contains(management, "\"boundNonPresentContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryNotAttemptedContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryDeadlineContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryCoverageContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryCoverageCountContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryCoverageIdentityContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryCoverageAllDevicesContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryCoverageFirstBatchCountContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryCoverageLastBatchCountContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryCleanupContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryCompletedCleanupContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryOutputReasonMissingContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryOutputReasonCrossSpliceContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryOutputCleanupContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryOutputDriverBeforeHardwareContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryOutputHardwareProgressContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryNativeExitStageContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryResponseIdentityStageContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryRequestIdentityStageContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryOutputNativeCodeContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryRawNegativeExitContradiction\"");
        StringAssert.Contains(management,
            "\"inventoryRawHighExitContradiction\"");
        StringAssert.Contains(management, "$phaseBatchAuthority = (");
        StringAssert.Contains(management, "$nativeExitAuthority = switch");
        StringAssert.Contains(management, "catch{exit 97}");
        StringAssert.Contains(management, "default { 98 }");
        StringAssert.Contains(management,
            "98 { $childFailureStage -ceq \"hostFailure\" }");
        StringAssert.Contains(runtimeHarness, "nativeExitCode = 94");
        StringAssert.Contains(runtimeHarness,
            "childFailureStage = \"validationQuota\"");
        StringAssert.Contains(runtimeHarness, "outputReason = \"nativeExit\"");
        StringAssert.Contains(runtimeHarness, "outputReason = \"stderr\"");
        StringAssert.Contains(runtimeHarness,
            "outputReason = \"invokeFailure\"");
        StringAssert.Contains(runtimeHarness,
            "inventoryOutputReasonsPersisted -ne 10");
        StringAssert.Contains(management,
            "[StringComparer]::OrdinalIgnoreCase");
        StringAssert.Contains(management, "responseIdentityConflict");
        StringAssert.Contains(management,
            "Set-VirtualDisplayChunkDuplicateStats");
        StringAssert.Contains(management,
            "[StringComparer]::OrdinalIgnoreCase");
        StringAssert.Contains(management, "duplicateGroupCount");
        StringAssert.Contains(management, "caseOnlyDuplicateCount");
        StringAssert.Contains(management, "responseEquivalentMultiValue");
        StringAssert.Contains(management, "responseDuplicateElementEquivalent");
        StringAssert.Contains(management, "responseDelimiterConflict");
        StringAssert.Contains(management, "responseOrderConflict");
        StringAssert.Contains(management, "responseLengthConflict");
        StringAssert.Contains(management,
            "ConvertTo-Json -InputObject $normalized -Compress");
        Assert.IsFalse(management.Contains(
            "$normalized-join\"\\u001f\"", StringComparison.Ordinal));
        Assert.IsFalse(management.Contains(
            "-split\"\\u001f\"", StringComparison.Ordinal));
        StringAssert.Contains(management, "requestIdentityDuplicate");
        StringAssert.Contains(runtimeHarness, "failureMode = \"caseCanonical\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"mixedAbsent\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"allAbsentHardware\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"allAbsentDriver\"");
        Assert.AreEqual(1,
            System.Text.RegularExpressions.Regex.Matches(runtimeHarness,
                "failureMode = \"allAbsentDriver\";\\s*" +
                "failureBatchIndex = 0;\\s*" +
                "deadline = 10000; result = \"passed\"").Count);
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"requestCaseDuplicate\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseExactDuplicate\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseEquivalentMultiValue\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseDuplicateElementEquivalent\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseConflict\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseStateConflict\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseDelimiterConflict\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseOrderConflict\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseLengthConflict\"");
        StringAssert.Contains(runtimeHarness, "multiValueOutputCount");
        StringAssert.Contains(runtimeHarness, "failureMode = \"responsePrefix\"");
        StringAssert.Contains(runtimeHarness, "failureMode = \"responseSuffix\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseEscaping\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseMissingIdentity\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseWrongTypeIdentity\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseRowShape\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseCanonicalInvalid\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responsePropertyStateInvalid\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseAbsentDataInvalid\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseHardwareIdsDataInvalid\"");
        StringAssert.Contains(runtimeHarness,
            "failureMode = \"responseDriverInfDataInvalid\"");
        StringAssert.Contains(management, "identityInvalidReason");
        StringAssert.Contains(management, "identityInvalidCount");
        StringAssert.Contains(management,
            "inventoryInvalidReasonMissingContradiction");
        StringAssert.Contains(management,
            "inventoryConflictCarriesInvalidReasonContradiction");
        StringAssert.Contains(runtimeHarness, "failureMode = \"hostNegative\"");
        StringAssert.Contains(runtimeHarness, "failureMode = \"hostHigh\"");
        StringAssert.Contains(management,
            "Assert-VirtualDisplayInventoryDiagnosticCorrelation");
        StringAssert.Contains(management, "$createProvenanceValid =");
        StringAssert.Contains(management,
            "Set-VirtualDisplayPostCreateIdentityAuthority");
        StringAssert.Contains(runtimeHarness, "prePostIdentityState");
        StringAssert.Contains(runtimeHarness, "prePostIdentityReason");
        StringAssert.Contains(runtimeHarness, "prePostZeroIdentityState");
        StringAssert.Contains(runtimeHarness,
            "prePostZeroObservedDeviceCount");
        StringAssert.Contains(management, "$markerAlreadyExact =");
        StringAssert.Contains(management, "if (-not $markerAlreadyExact)");
        StringAssert.Contains(runtimeHarness, "\"marker-restore-exact-noop\"");
        StringAssert.Contains(runtimeHarness,
            "LIGASE_VIRTUAL_DISPLAY_FORCE_MARKER_CHANGED");
        StringAssert.Contains(runtimeHarness, "-ForceMarkerChanged $true");
        StringAssert.Contains(management,
            "LIGASE_VIRTUAL_DISPLAY_FORCE_MARKER_CHANGED");
        StringAssert.Contains(runtimeHarness, "crossSpliceRejected");
        StringAssert.Contains(runtimeHarness,
            "virtualDisplayTerminalReadbackCases");
        StringAssert.Contains(runtimeHarness, "\"oneUnbound\"");
        StringAssert.Contains(runtimeHarness, "\"unavailable\"");
        StringAssert.Contains(runtimeHarness, "\"nonPresentExact\"");
        StringAssert.Contains(runtimeHarness,
            "\"unboundExactUnexpectedNames\"");
        StringAssert.Contains(runtimeHarness, "\"mixedPresentAndPhantom\"");
        StringAssert.Contains(runtimeHarness, "\"inventoryUnavailable\"");
        StringAssert.Contains(runtimeHarness,
            "virtualDisplayChunkedInventoryCases");
        StringAssert.Contains(runtimeHarness, "\"large369\"");
        StringAssert.Contains(runtimeHarness, "\"batchBoundary33\"");
        StringAssert.Contains(runtimeHarness, "\"quota\"");
        StringAssert.Contains(runtimeHarness, "\"inputDuplicate\"");
        StringAssert.Contains(runtimeHarness, "\"missing\"");
        StringAssert.Contains(runtimeHarness, "\"extra\"");
        StringAssert.Contains(runtimeHarness, "\"duplicate\"");
        StringAssert.Contains(runtimeHarness, "\"identityMismatch\"");
        StringAssert.Contains(runtimeHarness, "coverageStage");
        StringAssert.Contains(runtimeHarness, "coverageReason");
        StringAssert.Contains(runtimeHarness, "requestedCount");
        StringAssert.Contains(runtimeHarness, "returnedCount");
        StringAssert.Contains(runtimeHarness, "\"crossBatch\"");
        StringAssert.Contains(runtimeHarness, "\"timeout\"");
        StringAssert.Contains(runtimeHarness, "\"postLoopDeadline\"");
        StringAssert.Contains(runtimeHarness, "\"startAssign\"");
        StringAssert.Contains(runtimeHarness, "\"startResume\"");
        StringAssert.Contains(runtimeHarness, "\"startRetain\"");
        StringAssert.Contains(runtimeHarness, "hardCapMilliseconds");
        StringAssert.Contains(runtimeHarness, "cleanupState");
        StringAssert.Contains(runtimeHarness, "rootPidZero");
        StringAssert.Contains(runtimeHarness, "jobActiveProcesses");
        StringAssert.Contains(management,
            "\"schemaVersion\", \"inventoryState\", \"devices\"");
        StringAssert.Contains(management, "Invoke-VirtualDisplayInstaller");
        StringAssert.Contains(management, "ValidateVirtualDisplayInstallerProcess");
        StringAssert.Contains(management, "jobProcess.StandardOutput.ReadAsync");
        StringAssert.Contains(management, "jobProcess.StandardError.ReadAsync");
        StringAssert.Contains(management, "[byte[]]::new(512)");
        StringAssert.Contains(management, "CREATE_SUSPENDED");
        StringAssert.Contains(management, "STARTUPINFOEX");
        StringAssert.Contains(management, "PROC_THREAD_ATTRIBUTE_HANDLE_LIST");
        StringAssert.Contains(management, "InitializeProcThreadAttributeList");
        StringAssert.Contains(management, "UpdateProcThreadAttribute");
        StringAssert.Contains(management, "DeleteProcThreadAttributeList");
        StringAssert.Contains(management, "AssignProcessToJobObject");
        StringAssert.Contains(management, "ResumeThread");
        StringAssert.Contains(management, "JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE");
        StringAssert.Contains(management, "TerminateJobObject");
        StringAssert.Contains(management, "HasNoActiveProcesses");
        StringAssert.Contains(
            management,
            "[Text.UTF8Encoding]::new($false, $true)");
        var boundedInstallerStart = management.IndexOf(
            "function Invoke-VirtualDisplayInstaller(",
            StringComparison.Ordinal);
        var boundedInstallerEnd = management.IndexOf(
            "function Assert-VirtualDisplayInstallerTuple",
            boundedInstallerStart,
            StringComparison.Ordinal);
        Assert.IsTrue(boundedInstallerStart >= 0 && boundedInstallerEnd > boundedInstallerStart);
        var boundedInstaller = management[
            boundedInstallerStart..boundedInstallerEnd];
        Assert.IsFalse(boundedInstaller.Contains(
            "StandardOutput.ReadToEndAsync",
            StringComparison.Ordinal));
        Assert.IsFalse(boundedInstaller.Contains(
            "StandardError.ReadToEndAsync",
            StringComparison.Ordinal));
        StringAssert.Contains(management, "\"virtualDisplayInstallerToolUnavailable\"");
        StringAssert.Contains(management, "\"virtualDisplayCertificateRootFailed\"");
        StringAssert.Contains(management, "\"virtualDisplayCertificatePublisherFailed\"");
        StringAssert.Contains(management, "\"virtualDisplayDeviceRemoveFailed\"");
        StringAssert.Contains(management, "\"virtualDisplayDeviceCreateFailed\"");
        StringAssert.Contains(management, "\"virtualDisplayDriverPackageInstallFailed\"");
        StringAssert.Contains(management, "\"virtualDisplayInstallerTimeout\"");
        StringAssert.Contains(management, "\"virtualDisplayInstallerOutputOverflow\"");
        StringAssert.Contains(
            management,
            "installStage = [string]$script:virtualDisplayInstallStage");
        foreach (var token in new[]
                 {
                     "virtualDisplayInstallerProcessCases",
                     "\"stdoutOverflow\"", "\"stderrOverflow\"", "\"hang\"",
                     "\"stdoutOverflowTree\"", "\"stderrOverflowTree\"",
                     "\"assignFault\"", "\"resumeFault\"", "\"readFault\"",
                     "\"unassignedTerminateFault\"",
                     "\"unassignedWaitFault\"",
                     "\"assignedJobTerminateFault\"",
                     "\"assignedJobAccountingFault\"",
                     "\"secondaryContainment\"",
                     "\"secondaryTerminateFailure\"",
                     "\"secondaryWaitFailure\"",
                      "\"secondaryAccountingFailure\"",
                      "Assert-InstallerProcessCase",
                      "\"directV1\"", "\"faultV1\"", "\"retainedV1\"",
                      "virtualDisplayInstallerCaseSchemaInvalid",
                      "virtualDisplayInstallerCaseSchemaCases",
                      "$expected = @(switch -CaseSensitive ($schema)",
                      "switch -CaseSensitive ($schema)",
                      "Test-SecondaryContainmentCaseEvidence",
                      "Write-SecondaryContainmentCaseEvidence",
                      "secondaryContainmentCaseEvidenceV1",
                      "InstallerProcessCaseFilter",
                      "StopAfterInstallerProcessCases",
                      "virtualDisplayInstallerProcessFocusedPassed",
                      "externalCleanupAttempted",
                      "outerHardCapMilliseconds",
                      "environmentRestored",
                      "[StringComparer]::Ordinal",
                      "\"missing\"", "\"unknown\"", "\"type\"",
                      "\"duplicateSame\"", "\"duplicateConflict\"",
                      "\"schemaUpper\"", "\"schemaMixed\"", "\"nameCase\"",
                      "\"codeCase\"", "\"externalCleanupCase\"",
                     "\"unsafeSchema\"",
                     "installTransactionJunctionInvocationUnbounded",
                     "installTransactionJunctionOutputOverflow",
                     "installTransactionJunctionProjectionInvalid",
                     "\"--bounded-capture\"",
                     "class NativeJobProcess",
                     "CreateProcessW",
                     "CreateSuspended",
                     "ExtendedStartupInfoPresent",
                     "HandleListAttribute",
                     "InitializeProcThreadAttributeList",
                     "UpdateProcThreadAttribute",
                     "AssignProcessToJobObject(job, information.Process)",
                     "ResumeThread(information.Thread)",
                     "\"treeInheritedPipe\"",
                     "boundedHostEarlyChildFixtureFailed",
                     "\"assignFault\"",
                     "\"resumeFault\"",
                     "boundedHostStartFaultFixtureFailed",
                     "LastCleanupProven",
                     "LastJobActiveProcesses",
                     "sealed class NativeStartException",
                     "startStage = failure.Stage",
                     "startCode = failure.Code",
                     "\"executableResolveFault\"",
                     "GetSystemDirectoryW",
                     "ResolveTrustedExecutable",
                     "IsClosedExecutableFile",
                     "StringComparison.Ordinal",
                     "Environment.ProcessPath",
                     "FileAttributes.ReparsePoint",
                     "CreateProcessW(\n                resolvedExecutable",
                     "\"pipeFault\"",
                     "\"jobFault\"",
                     "\"attributeFault\"",
                     "\"createFault\"",
                     "\"managedHandoffFault\"",
                     "\"processWrapperFault\"",
                     "\"stdoutSafeHandleFault\"",
                     "\"stdoutStreamFault\"",
                     "\"stderrSafeHandleFault\"",
                     "\"stderrStreamFault\"",
                     "\"stdoutWriteCloseFault\"",
                     "\"stderrWriteCloseFault\"",
                     "\"threadCloseFault\"",
                     "CloseRawAfterFailure",
                     "\"powershellCaseDrift\"",
                     "\"powershellAbsolute\"",
                     "\"unknownName\"",
                     "boundedHostExecutableIdentityFixtureFailed",
                     "AssignProcessToJobObject",
                     "TerminateJobObject",
                     "ActiveProcessCount",
                     "pipeFault",
                     "jobEmpty",
                     "cleanupCompleted",
                     "boundedHostHangFixtureFailed",
                     "boundedHostOverflowFixtureFailed",
                     "boundedHostUtf8BoundaryFixtureFailed",
                     "boundedHostInvalidOverflowFixtureFailed",
                     "boundedHostInvalidUtf8FixtureFailed",
                     "boundedHostIncompleteUtf8FixtureFailed",
                     "boundedHostDualOverflowFixtureFailed",
                     "stdoutDecoderState",
                     "stderrDecoderState",
                     "stdoutPendingTailLength",
                     "stderrPendingTailLength",
                     "stdoutRawSha",
                     "stderrRawSha",
                     "runTimeoutMs + cleanupReserveMs",
                     "boundedHostCaseEvidenceV1",
                     "\"--emit\"",
                     "\"asciiOverflowStdout\"",
                     "\"utf8BoundaryStdout\"",
                     "\"internalInvalidStdout\"",
                     "\"incompleteUtf8Stdout\"",
                     "\"dualOverflow\"",
                     "byteEmitterArgumentValidationFailed",
                     "argumentListRunnerSourceSha",
                     "argumentListRunnerBinarySha",
                     "<TreatWarningsAsErrors>true</TreatWarningsAsErrors>",
                     "Get-CompatibleSha256",
                     "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
                     "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
                     "compatibilityProcessStartCount",
                     "evidenceCompatibilitySelfTestFailed",
                     "Write-BoundedHostCaseEvidence",
                     "Test-BoundedHostCaseEvidence",
                     "gateEvidenceUnavailable",
                     "evidenceSha=",
                     "stdoutRawLength",
                     "stderrRawLength",
                     "hardCapMilliseconds",
                     "environmentRestored",
                     "boundedHostExtraJsonFixtureFailed",
                     "boundedHostMalformedJsonFixtureFailed",
                     "boundedHostCleanupFaultFixtureFailed",
                     "boundedHostNoRecordFixtureFailed",
                     "installTransactionBoundedWriteEnvelopeInvalid",
                     "installTransactionBoundedWriteOutputInvalid",
                     "$writeEnvelope.jobActiveProcesses",
                     "$writeEnvelope.hardCapMilliseconds -ne 25000",
                     "$DotNet $argumentListRunner --bounded-capture",
                     "installTransactionJunctionStateRestoreFailed",
                     "validationEnvironmentRestored",
                     "$junctionEnvelopeOutput.Count -ne 1",
                     "transactionHelperStage -ceq \"rejectReparse\"",
                     "transactionHelperNativeExit -eq 18",
                     "helperNativeExit = $junctionNativeExit",
                     "helperStage = $junctionStage",
                     "elapsedMilliseconds = $junctionElapsedMilliseconds",
                     "boundedJunctionEvidenceV1",
                     "transaction-junction.first.json",
                     "jobActiveProcesses -ne 0",
                     "hardCapMilliseconds -ne 20000",
                     "junctionEnvelope.stdoutOverflow",
                     "junctionEnvelope.stderrOverflow",
                     "evidenceSha = $junctionEvidenceSha",
                     "$caseUsesExternalCleanup",
                      "firstCleanupProven", "authorityRetained",
                     "secondaryContainmentAttempted",
                     "secondaryContainmentCompleted",
                     "\"terminateFault\"", "\"waitFault\"", "\"pipeFault\"",
                     "\"tree\"", "\"cross\"", "\"malformed\"", "\"extra\"",
                     "\"invalidUtf8\""
                 })
        {
            StringAssert.Contains(runtimeHarness, token);
        }
        var secondaryEvidenceStart = runtimeHarness.IndexOf(
            "$secondaryEvidence = [ordered]@{", StringComparison.Ordinal);
        var secondaryEvidenceEnd = runtimeHarness.IndexOf(
            "$secondaryEvidenceSha =", secondaryEvidenceStart,
            StringComparison.Ordinal);
        Assert.IsTrue(
            secondaryEvidenceStart >= 0 &&
            secondaryEvidenceEnd > secondaryEvidenceStart);
        var secondaryEvidence = runtimeHarness[
            secondaryEvidenceStart..secondaryEvidenceEnd];
        var secondaryEvidenceNames =
            System.Text.RegularExpressions.Regex.Matches(
                secondaryEvidence,
                @"(?m)^\s{6}(?<name>[A-Za-z][A-Za-z0-9]*)\s*=")
            .Select(match => match.Groups["name"].Value)
            .ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "schema", "caseId", "declaredSchema", "behavior", "fault",
                "runnerExit", "startStage", "startCode", "childExitCode",
                "parseState", "outputRecordCount", "stdoutRawLength",
                "stdoutRawSha", "stdoutOverflow", "stdoutDecoderState",
                "stdoutPendingTailLength", "stderrRawLength",
                "stderrRawSha", "stderrOverflow", "stderrDecoderState",
                "stderrPendingTailLength", "timedOut",
                "runnerCleanupState", "rootPidZero", "descendantPidZero",
                "jobActiveProcesses", "runnerElapsedMilliseconds",
                "runBudgetMilliseconds", "cleanupReserveMilliseconds",
                "outerElapsedMilliseconds", "outerHardCapMilliseconds",
                "code", "success",
                "firstCleanupProven", "authorityRetained", "retainedPid",
                "secondaryContainmentAttempted",
                "secondaryContainmentCompleted", "cleanupState",
                "externalCleanupAttempted", "externalCleanupCompleted",
                "finalPidZero", "sentinelExists", "residueCount",
                "environmentRestored"
            },
            secondaryEvidenceNames);
        var secondaryInvocationStart = runtimeHarness.IndexOf(
            "if ($caseIsSecondary) {", StringComparison.Ordinal);
        var secondaryInvocationEnd = runtimeHarness.IndexOf(
            "} else {", secondaryInvocationStart, StringComparison.Ordinal);
        Assert.IsTrue(
            secondaryInvocationStart >= 0 &&
            secondaryInvocationEnd > secondaryInvocationStart);
        var secondaryInvocation = runtimeHarness[
            secondaryInvocationStart..secondaryInvocationEnd];
        StringAssert.Contains(
            secondaryInvocation,
            "$DotNet $argumentListRunner --bounded-capture");
        StringAssert.Contains(secondaryInvocation, "7000 3500 8192");
        Assert.IsFalse(
            secondaryInvocation.Contains(
                "& powershell.exe", StringComparison.Ordinal));
        var runnerRawScanIndex = runtimeHarness.IndexOf(
            "Get-SecondaryRunnerRawParseState $runnerRaw",
            secondaryInvocationEnd, StringComparison.Ordinal);
        var runnerConvertIndex = runtimeHarness.IndexOf(
            "$runnerEnvelope = $runnerRaw | ConvertFrom-Json",
            runnerRawScanIndex, StringComparison.Ordinal);
        Assert.IsTrue(
            runnerRawScanIndex > secondaryInvocationEnd &&
            runnerConvertIndex > runnerRawScanIndex);
        foreach (var rawParserToken in new[]
                 {
                     "return \"duplicate\"",
                     "return \"trailing\"",
                     "return \"missing\"",
                     "return \"unknownProperty\"",
                     "$parseState = \"typeInvalid\"",
                     "$trimmed.EndsWith(",
                     "$ExpectedNames -cnotcontains $_",
                     "$rawNames -cnotcontains $_",
                     "$secondaryRunnerParserDuplicateSame",
                     "$secondaryRunnerParserDuplicateConflict",
                     "secondaryRunnerRawParserSelfTestFailed"
                 })
        {
            StringAssert.Contains(runtimeHarness, rawParserToken);
        }
        var secondaryWriteIndex = runtimeHarness.IndexOf(
            "Write-SecondaryContainmentCaseEvidence $secondaryEvidence",
            secondaryEvidenceEnd, StringComparison.Ordinal);
        var secondaryAssertionsStart = runtimeHarness.IndexOf(
            "# SECONDARY_ASSERTIONS_BEGIN", secondaryWriteIndex,
            StringComparison.Ordinal);
        var secondaryAssertionsEnd = runtimeHarness.IndexOf(
            "# SECONDARY_ASSERTIONS_END", secondaryAssertionsStart,
            StringComparison.Ordinal);
        Assert.IsTrue(
            secondaryWriteIndex > secondaryEvidenceEnd &&
            secondaryAssertionsStart > secondaryWriteIndex &&
            secondaryAssertionsEnd > secondaryAssertionsStart);
        var secondaryAssertions = runtimeHarness[
            secondaryAssertionsStart..secondaryAssertionsEnd];
        Assert.AreEqual(
            1,
            System.Text.RegularExpressions.Regex.Matches(
                secondaryAssertions, @"(?m)^\s*throw\s").Count);
        StringAssert.Contains(
            secondaryAssertions,
            "virtualDisplayInstallerSecondaryCaseFailed caseId=");
        StringAssert.Contains(secondaryAssertions, "evidenceSha=");
        foreach (var secondaryAuthorityToken in new[]
                 {
                     "\"runnerStart\"", "\"runnerOutput\"", "\"runBudget\"",
                     "\"runnerCleanup\"", "\"hardCap\"", "\"environment\"",
                     "\"semanticTuple\"", "\"primaryContainmentAuthority\"",
                     "\"externalCleanup\"", "\"residue\"",
                     "[int]$projection.cleanupPid -ne 0",
                     "[bool]$projection.firstCleanupProven"
                 })
        {
            StringAssert.Contains(secondaryAssertions, secondaryAuthorityToken);
        }
        var genericAssertions = runtimeHarness[
            secondaryAssertionsEnd..runtimeHarness.IndexOf(
                "$evidence = [ordered]@{", secondaryAssertionsEnd,
                StringComparison.Ordinal)];
        Assert.AreEqual(
            4,
            System.Text.RegularExpressions.Regex.Matches(
                genericAssertions, @"if \(-not \$caseIsSecondary").Count);
        var boundedStart = runtimeHarness.IndexOf(
            "if (args[0] == \"--bounded-capture\")",
            StringComparison.Ordinal);
        var ordinaryRunnerStart = runtimeHarness.IndexOf(
            "var start = new System.Diagnostics.ProcessStartInfo(args[0])",
            boundedStart, StringComparison.Ordinal);
        Assert.IsTrue(boundedStart >= 0 && ordinaryRunnerStart > boundedStart);
        var boundedRunner = runtimeHarness[
            boundedStart..ordinaryRunnerStart];
        foreach (var obsoleteManagedToken in new[]
                 {
                     "Process.Start(startBounded)",
                     "processBounded.Kill(true)",
                     "RedirectStandardOutput",
                     "RedirectStandardError",
                     "processBounded.StandardOutput",
                     "processBounded.StandardError",
                     "StandardOutput.BaseStream",
                     "StandardError.BaseStream",
                     "using var job = new LigaseJob"
                 })
        {
            Assert.IsFalse(boundedRunner.Contains(
                obsoleteManagedToken, StringComparison.Ordinal));
        }
        var nativeStart = runtimeHarness.IndexOf(
            "sealed class NativeJobProcess", ordinaryRunnerStart,
            StringComparison.Ordinal);
        var nativeEnd = runtimeHarness.IndexOf(
            "sealed class LigaseJob", nativeStart,
            StringComparison.Ordinal);
        Assert.IsTrue(nativeStart > ordinaryRunnerStart && nativeEnd > nativeStart);
        var nativeRunner = runtimeHarness[nativeStart..nativeEnd];
        Assert.AreEqual(3, System.Text.RegularExpressions.Regex.Matches(
            nativeRunner, @"\bCreatePipe\(").Count);
        Assert.AreEqual(3, System.Text.RegularExpressions.Regex.Matches(
            nativeRunner, @"\bSetHandleInformation\(").Count);
        StringAssert.Contains(
            nativeRunner,
            "Marshal.WriteIntPtr(handleList, 0, stdoutWrite);");
        StringAssert.Contains(
            nativeRunner,
            "Marshal.WriteIntPtr(handleList, IntPtr.Size, stderrWrite);");
        StringAssert.Contains(
            nativeRunner,
            "CreateSuspended | CreateNoWindow | ExtendedStartupInfoPresent");
        StringAssert.Contains(
            nativeRunner,
            "true,\n                CreateSuspended");
        var createIndex = nativeRunner.IndexOf(
            "if (!CreateProcessW(", StringComparison.Ordinal);
        var assignIndex = nativeRunner.IndexOf(
            "if (!AssignProcessToJobObject(job, information.Process))",
            StringComparison.Ordinal);
        var resumeIndex = nativeRunner.IndexOf(
            "if (ResumeThread(information.Thread)", StringComparison.Ordinal);
        Assert.IsTrue(
            createIndex >= 0 && assignIndex > createIndex &&
            resumeIndex > assignIndex);
        StringAssert.Contains(nativeRunner, "LastCleanupProven = signaled && empty");
        StringAssert.Contains(nativeRunner, "WaitForSingleObject");
        StringAssert.Contains(nativeRunner, "ActiveCount(job)");
        foreach (var nativeStartStage in new[]
                 {
                     "executableResolve", "pipe", "job", "attribute", "create", "assign",
                     "resume", "managedHandoff"
                 })
        {
            StringAssert.Contains(
                nativeRunner, $"stage = \"{nativeStartStage}\";");
        }
        StringAssert.Contains(
            nativeRunner, "throw new NativeStartException(");
        Assert.IsFalse(nativeRunner.Contains(
            "var value = new NativeJobProcess {",
            StringComparison.Ordinal));
        var processWrapperIndex = nativeRunner.IndexOf(
            "managedProcess = System.Diagnostics.Process.GetProcessById(",
            StringComparison.Ordinal);
        var stdoutTransferIndex = nativeRunner.IndexOf(
            "managedStdoutHandle = new SafeFileHandle(stdoutRead, true);",
            StringComparison.Ordinal);
        var stderrTransferIndex = nativeRunner.IndexOf(
            "managedStderrHandle = new SafeFileHandle(stderrRead, true);",
            StringComparison.Ordinal);
        var stdoutRawClearIndex = nativeRunner.IndexOf(
            "stdoutRead = IntPtr.Zero;", stdoutTransferIndex,
            StringComparison.Ordinal);
        var stderrRawClearIndex = nativeRunner.IndexOf(
            "stderrRead = IntPtr.Zero;", stderrTransferIndex,
            StringComparison.Ordinal);
        Assert.IsTrue(
            processWrapperIndex > assignIndex &&
            stdoutTransferIndex > processWrapperIndex &&
            stdoutRawClearIndex > stdoutTransferIndex &&
            stderrTransferIndex > stdoutRawClearIndex &&
            stderrRawClearIndex > stderrTransferIndex &&
            resumeIndex > stderrRawClearIndex);
        StringAssert.Contains(
            nativeRunner, "managedStdoutStream?.Dispose();");
        StringAssert.Contains(
            nativeRunner, "managedStderrStream?.Dispose();");
        StringAssert.Contains(
            nativeRunner, "managedStdoutHandle?.Dispose();");
        StringAssert.Contains(
            nativeRunner, "managedStderrHandle?.Dispose();");
        StringAssert.Contains(
            nativeRunner, "managedProcess?.Dispose();");
        var driverInstaller = File.ReadAllText(Path.Combine(
            repo, "src_assets", "windows", "drivers", "sudovda",
            "install.bat"));
        var driverInf = File.ReadAllText(Path.Combine(
            repo, "src_assets", "windows", "drivers", "sudovda",
            "SudoVDA.inf"));
        Assert.IsFalse(File.Exists(Path.Combine(
            repo, "src_assets", "windows", "drivers", "sudovda",
            "SudoVDA.dll")),
            "The signed driver binary must remain an external pinned build input.");
        foreach (var token in new[]
                 {
                     "[SourceDisksFiles]", "SudoVDA.dll=1",
                     "CopyFiles=UMDriverCopy",
                     "ServiceBinary=%12%\\UMDF\\SudoVDA.dll",
                     "DriverVer = 07/14/2025,1.10.9.289"
                 })
        {
            StringAssert.Contains(driverInf, token);
        }
        var sourceDisksFiles = System.Text.RegularExpressions.Regex.Match(
            driverInf,
            @"(?ms)^\[SourceDisksFiles\]\s*(?<body>.*?)(?=^\[)");
        var umDriverCopy = System.Text.RegularExpressions.Regex.Match(
            driverInf,
            @"(?ms)^\[UMDriverCopy\]\s*(?<body>.*?)(?=^\[)");
        Assert.IsTrue(sourceDisksFiles.Success);
        Assert.IsTrue(umDriverCopy.Success);
        CollectionAssert.AreEqual(
            new[] { "SudoVDA.dll=1" },
            sourceDisksFiles.Groups["body"].Value
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith(';'))
                .ToArray());
        CollectionAssert.AreEqual(
            new[] { "SudoVDA.dll" },
            umDriverCopy.Groups["body"].Value
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith(';'))
                .ToArray());
        foreach (var token in new[]
                 {
                      "if not exist \"%NEFCON%\"", "stage=certificateRoot",
                      "if \"%ACTION%\"==\"removeOne\" goto remove_one_device",
                      "if \"%ACTION%\"==\"removeInstance\" goto remove_instance",
                      "if not \"%ACTION%\"==\"install\"",
                      "stage=deviceRemove", "removeExit=%REMOVE_EXIT%",
                      "\"%LIGASE_VDISPLAY_PNPUTIL%\" /remove-device \"%LIGASE_VDISPLAY_INSTANCE_ID%\"",
                      "stage=deviceRemoveFallback", "exit /b 26",
                     "stage=certificatePublisher", "stage=deviceCreate",
                     "stage=driverPackageInstall", "stage=completed",
                     "exit /b 20", "exit /b 21", "exit /b 22", "exit /b 23",
                     "exit /b 24"
                 })
        {
            StringAssert.Contains(driverInstaller, token);
        }
        Assert.IsFalse(driverInstaller.Contains(
            "if not \"%REMOVE_EXIT%\"==\"0\" goto removed_all_devices",
            StringComparison.Ordinal));
        Assert.IsFalse(driverInstaller.Contains(
            "%SystemRoot%\\System32\\pnputil.exe",
            StringComparison.Ordinal));
        Assert.IsTrue(
            driverInstaller.LastIndexOf("exit /b 0", StringComparison.Ordinal) >
            driverInstaller.LastIndexOf("popd", StringComparison.Ordinal));
        var installerBuild = File.ReadAllText(Path.Combine(
            repo, "packaging", "windows", "ligase",
            "Build-LigaseInstaller.ps1"));
        foreach (var token in new[]
                 {
                     "[string]$NefconExecutable", "586152",
                      "19A113297EAFEFD796AA91C1A64D199628D9C58DC53928899D2E5D6A68074EFE",
                      "1F431092EC96A80B41AB5317F53AC02EA6F9B89B",
                      "[string]$SudoVdaDriverBinary",
                      "[string]$BoostArchive",
                      "[switch]$ValidateExternalInputsOnly",
                      "102078704",
                      "67ACEC02D0D118B5DE9EB441F5FB707B3A1CDD884BE00CA24B9A73C995511F74",
                      "boostArchiveUnavailable",
                      "boostArchiveSizeMismatch",
                      "boostArchiveHashMismatch",
                      "boostArchiveSha256",
                      "boostArchiveSize",
                      "83216",
                      "47EE263CB5DE9382C6630A2D7F3DAFEC4A49419F953BEEC869CA5DD0C460FF63",
                      "3C918FC73525AD8B1521B6DB26B71F694277CC49",
                      "0x8664",
                      "virtualDisplayDriverBinaryUnavailable",
                      "virtualDisplayDriverBinarySizeMismatch",
                      "virtualDisplayDriverBinaryArchitectureMismatch",
                      "virtualDisplayDriverBinaryHashMismatch",
                      "virtualDisplayDriverBinarySignatureInvalid",
                      "virtualDisplayDriverVersionMismatch",
                      "virtualDisplayDriverInfClosureInvalid",
                      "[ValidateRange(1, 64)]",
                     "[int]$CoreBuildParallelism = 1",
                     "--parallel $CoreBuildParallelism",
                      "installerToolSha256",
                      "driverBinarySha256",
                      "driverBinaryArchitecture",
                      "driverVersion",
                      "Deployment/Drivers/sudovda/nefconc.exe",
                      "Deployment/Drivers/sudovda/SudoVDA.dll"
                 })
        {
            StringAssert.Contains(installerBuild, token);
        }
        Assert.IsFalse(
            System.Text.RegularExpressions.Regex.IsMatch(
                installerBuild,
                @"--parallel\s*(?:\r?\n|$)"),
            "The core build must never use an unbounded bare --parallel switch.");
        Assert.IsTrue(
            System.Text.RegularExpressions.Regex.IsMatch(
                installerBuild,
                @"\[Parameter\(Mandatory\)\]\s*\r?\n\s*\[string\]\$SudoVdaDriverBinary"),
            "The SudoVDA driver binary must be an explicit mandatory input.");
        Assert.IsTrue(
            System.Text.RegularExpressions.Regex.IsMatch(
                installerBuild,
                @"\[Parameter\(Mandatory\)\]\s*\r?\n\s*\[string\]\$BoostArchive"),
            "The Boost archive must be an explicit mandatory input.");
        var boostValidationIndex = installerBuild.IndexOf(
            "$boostArchivePath = [IO.Path]::GetFullPath($BoostArchive)",
            StringComparison.Ordinal);
        var driverValidationIndex = installerBuild.IndexOf(
            "$sudoVdaPath = [IO.Path]::GetFullPath($SudoVdaDriverBinary)",
            StringComparison.Ordinal);
        var buildWorkspaceIndex = installerBuild.IndexOf(
            "$work = Join-Path $output", StringComparison.Ordinal);
        var driverCopyIndex = installerBuild.IndexOf(
            "Join-Path $temporaryStage \"Deployment/Drivers/sudovda/SudoVDA.dll\"",
            StringComparison.Ordinal);
        var manifestDriverIndex = installerBuild.IndexOf(
            "driverBinarySha256 = $sudoVdaHash.ToLowerInvariant()",
            StringComparison.Ordinal);
        Assert.IsTrue(
            boostValidationIndex >= 0 &&
            driverValidationIndex > boostValidationIndex &&
            buildWorkspaceIndex > driverValidationIndex &&
            driverCopyIndex > buildWorkspaceIndex &&
            manifestDriverIndex > driverCopyIndex,
            "Pinned external inputs must validate before build and the DLL must be copied before manifest projection.");
        var boostCmake = File.ReadAllText(Path.Combine(
            repo, "cmake", "dependencies", "Boost_Sunshine.cmake"));
        foreach (var token in new[]
                 {
                     "LIGASE_BOOST_ARCHIVE is required for offline Windows builds",
                     "get_filename_component(LIGASE_BOOST_ARCHIVE_ABSOLUTE",
                     "LIGASE_BOOST_ARCHIVE_ABSOLUTE MATCHES \"^[Dd]:[/\\\\\\\\]\"",
                     "LIGASE_BOOST_ARCHIVE must be on the D drive",
                     "file(SIZE \"${LIGASE_BOOST_ARCHIVE_ABSOLUTE}\"",
                     "file(SHA256 \"${LIGASE_BOOST_ARCHIVE_ABSOLUTE}\"",
                     "LIGASE_BOOST_ARCHIVE_SIZE EQUAL 102078704",
                     "67acec02d0d118b5de9eb441f5fb707b3a1cdd884be00ca24b9a73c995511f74",
                     "set(BOOST_URL \"${LIGASE_BOOST_ARCHIVE_ABSOLUTE}\")"
                 })
        {
            StringAssert.Contains(boostCmake, token);
        }
        Assert.IsTrue(
            boostCmake.IndexOf(
                "LIGASE_BOOST_ARCHIVE is required for offline Windows builds",
                StringComparison.Ordinal) <
            boostCmake.IndexOf(
                "FetchContent_Declare(",
                StringComparison.Ordinal),
            "The local archive must be validated before FetchContent declaration.");
        Assert.IsTrue(
            boostCmake.IndexOf("set(Boost_FOUND FALSE)", StringComparison.Ordinal) <
            boostCmake.IndexOf("if(NOT Boost_FOUND)", StringComparison.Ordinal),
            "Windows builds must not bypass the pinned archive through a system Boost package.");
        foreach (var token in new[]
                 {
                     "[IO.File]::Replace($temp, $Path, $null, $true)",
                     "[IO.File]::Move($temp, $Path)",
                     "$stream.Flush($true)",
                     "Test-ExactBytes $Bytes ([IO.File]::ReadAllBytes($Path))",
                     "$ownershipPath $ownershipBytes $false",
                     "\".ligase-driver-ownership-*.tmp\"",
                     "\"createTemp\"", "\"writeTemp\"", "\"tempReadback\"",
                     "\"atomicReplace\"", "\"finalReadback\"",
                     "\"ValidateVirtualDisplayMarkerTransaction\"",
                     "LIGASE_VIRTUAL_DISPLAY_VALIDATION_ROOT",
                     "LIGASE_VIRTUAL_DISPLAY_COMPENSATION_FAILURE"
                 })
        {
            StringAssert.Contains(management, token);
        }
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
