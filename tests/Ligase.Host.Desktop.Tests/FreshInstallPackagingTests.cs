using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class FreshInstallPackagingTests
{
    [TestMethod]
    public void DesktopStartupGateUsesStrictIsolatedAppInstanceKey()
    {
        var repo = FindRepositoryRoot();
        var singleInstance = File.ReadAllText(Path.Combine(
            repo, "src", "Ligase.Desktop", "Services",
            "SingleInstanceService.cs"));
        var desktopStartupGate = File.ReadAllText(Path.Combine(
            repo, "packaging", "windows", "ligase",
            "Test-LigaseDesktopStartup.ps1"));

        StringAssert.Contains(singleInstance,
            "LIGASE_STARTUP_VALIDATION_INSTANCE_KEY");
        StringAssert.Contains(singleInstance, "Guid.TryParseExact(");
        StringAssert.Contains(singleInstance,
            "StartupValidationInstancePrefix.Length..");
        StringAssert.Contains(desktopStartupGate,
            "Ligase.Host.Desktop.StartupValidation.");
        StringAssert.Contains(desktopStartupGate,
            "Remove-Item Env:LIGASE_STARTUP_VALIDATION_INSTANCE_KEY");
    }

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
    public void LegacyDriverTrustCleanupAuthorityIsRetired()
    {
        var management = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "packaging", "windows", "ligase",
            "Manage-LigaseInstallation.ps1"));
        Assert.IsFalse(management.Contains(
            "CleanupLegacyDriverTrust", StringComparison.Ordinal));
        Assert.IsFalse(management.Contains(
            "Get-DriverCertificateLocations", StringComparison.Ordinal));
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
        RequireProductProcessesStoppedForMigrationFixture();
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

    private static void RequireProductProcessesStoppedForMigrationFixture()
    {
        var names = new[]
        {
            "Ligase Host",
            "Ligase.Host.Desktop",
            "sunshine",
            "Ligase.GameWatcher"
        };
        if (names.Any(name => Process.GetProcessesByName(name).Length != 0))
            Assert.Inconclusive(
                "Migration fixture requires the installed product to remain stopped; " +
                "the current physical acceptance window is intentionally preserved.");
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
    public void VirtualDisplayResetUsesOnePinnedNativeOwnerAndThinCallers()
    {
        var repo = FindRepositoryRoot();
        var nsis = File.ReadAllText(Path.Combine(
            repo, "packaging", "windows", "ligase", "LigaseHost.nsi"));
        var management = File.ReadAllText(Path.Combine(
            repo, "packaging", "windows", "ligase",
            "Manage-LigaseInstallation.ps1"));
        var build = File.ReadAllText(Path.Combine(
            repo, "packaging", "windows", "ligase",
            "Build-LigaseInstaller.ps1"));
        var setup = File.ReadAllText(Path.Combine(
            repo, "tools", "Ligase.VirtualDisplay.Setup", "Program.cs"));

        StringAssert.Contains(build, "Ligase.VirtualDisplay.Setup.csproj");
        StringAssert.Contains(build, "Deployment/Ligase.VirtualDisplay.Setup.exe");
        StringAssert.Contains(management, "function Invoke-VirtualDisplaySetup");
        StringAssert.Contains(management, "StartExactWithHandles");
        StringAssert.Contains(management, "inheritedHandles");
        StringAssert.Contains(management, "requestWriteHandlesClosed = $true");
        StringAssert.Contains(management, "requestWriteSharing = $false");
        StringAssert.Contains(management,
            "Assert-VirtualDisplaySetupResultContract $result $Manifest");
        StringAssert.Contains(management, "function Test-JsonSchemaValue");
        StringAssert.Contains(management, "resultSchemaSha256");
        StringAssert.Contains(management, "$target.Length + $count -gt 4096");
        StringAssert.Contains(management, "$job.HasNoActiveProcesses()");
        StringAssert.Contains(management, "$stdoutClosed -and $stderrClosed");
        StringAssert.Contains(management,
            "[LigaseJobProcess]::SecondaryContainment");
        StringAssert.Contains(management,
            "Write-VirtualDisplaySetupEvidence $setupResult");
        StringAssert.Contains(management,
            "Write-VirtualDisplaySetupLastResortEvidence $setupResult");
        StringAssert.Contains(management,
            "resultFileIdentitySha256 = [string]$script:virtualDisplayResultIdentitySha256");
        StringAssert.Contains(management,
            "resultFileSha256 = [string]$script:virtualDisplayResultSha256");
        StringAssert.Contains(management,
            "$null = Write-InstallerEvidenceDocument $document");
        StringAssert.Contains(management,
            "[string]$EvidenceUninstallDisposition = \"none\"");
        StringAssert.Contains(management,
            "[string]$EvidenceUninstallState = \"notAttempted\"");
        StringAssert.Contains(management,
            "$EvidenceResultCode -ceq \"uninstallStarted\"");
        StringAssert.Contains(management,
            "$EvidenceResultCode -ceq \"uninstalled\"");
        StringAssert.Contains(management, "uninstall = if (");
        StringAssert.Contains(management,
            "Write-Outcome \"uninstalled\" $true ([ordered]@{");
        Assert.IsFalse(management.Contains(
            "\n  Write-InstallerEvidenceDocument $document\n  return $document",
            StringComparison.Ordinal));
        StringAssert.Contains(build, "resultSchemaSha256");
        StringAssert.Contains(build,
            "\"SudoVDA.dll\", \"SudoVDA.inf\", \"sudovda.cat\", \"sudovda.cer\"");
        Assert.IsFalse(Regex.IsMatch(build,
            "\\\"sudovda\\.cer\\\"\\)\\s*\\|\\s*Sort-Object\\s+-CaseSensitive"));
        StringAssert.Contains(nsis, "-Action InstallVirtualDisplay");
        StringAssert.Contains(nsis, "-Action UninstallVirtualDisplay");
        StringAssert.Contains(nsis, "Section \"Uninstall\"");
        Assert.IsFalse(nsis.Contains("Section \"卸载\"", StringComparison.Ordinal));
        StringAssert.Contains(nsis, "${UnStrTrimNewLines}");
        var uninstallBlock = Regex.Match(nsis,
            "(?ms)^Section\\s+\"Uninstall\"\\s*\\r?\\n(?<body>.*?)^SectionEnd\\s*$");
        Assert.IsTrue(uninstallBlock.Success);
        Assert.AreEqual(3, Regex.Matches(uninstallBlock.Groups["body"].Value,
            "\\$\\{UnStrTrimNewLines\\}").Count);
        Assert.AreEqual(0, Regex.Matches(uninstallBlock.Groups["body"].Value,
            "\\$\\{StrTrimNewLines\\}").Count);
        var installerOnly = nsis.Remove(uninstallBlock.Index, uninstallBlock.Length);
        Assert.AreEqual(1, Regex.Matches(installerOnly,
            "\\$\\{UnStrTrimNewLines\\}").Count);
        StringAssert.Contains(nsis, "-EvidencePhase uninstalling");
        StringAssert.Contains(nsis, "-EvidencePhase uninstalled");
        StringAssert.Contains(nsis, "-EvidenceUninstallDisposition $3");
        StringAssert.Contains(nsis, "-EvidenceUninstallState $4");
        Assert.IsTrue(nsis.IndexOf("-EvidencePhase uninstalling",
            StringComparison.Ordinal) < nsis.IndexOf(
            "-Action UninstallVirtualDisplay", StringComparison.Ordinal));
        Assert.IsTrue(nsis.IndexOf("-EvidencePhase uninstalled",
            StringComparison.Ordinal) < nsis.IndexOf("DeleteRegKey HKLM",
            StringComparison.Ordinal));
        Assert.IsTrue(nsis.IndexOf("DeleteRegKey HKLM",
            StringComparison.Ordinal) < nsis.IndexOf("RMDir /r \"$INSTDIR\"",
            StringComparison.Ordinal));
        StringAssert.Contains(nsis,
            "虚拟显示未安装；物理桌面串流仍可用。");
        StringAssert.Contains(nsis,
            "Host 仍保留，虚拟显示未确认移除。可重试卸载。");

        StringAssert.Contains(setup, "DiUninstallDevice");
        Assert.IsFalse(setup.Contains(
            "SetupDiRemoveDevice", StringComparison.Ordinal));
        StringAssert.Contains(setup, "DigcfAllClasses | DigcfPresent");
        StringAssert.Contains(setup, "strictDecrease");
        StringAssert.Contains(setup, "samples < 3");
        StringAssert.Contains(setup, "CertificateStoreAuthority");
        StringAssert.Contains(setup, "LocalMachine\\\\Root");
        StringAssert.Contains(setup, "LocalMachine\\\\TrustedPublisher");
        StringAssert.Contains(setup, "retainedNotOwned");
        StringAssert.Contains(setup, "absentNotOwned");
        StringAssert.Contains(setup, "CleanupState = \"retained\"");
        StringAssert.Contains(setup, "legacyStores.Length > 2");
        StringAssert.Contains(setup, "ObserveUnownedCertificateStore");
        StringAssert.Contains(setup, "FailureDiagnostic(failure, \"trust\")");
        StringAssert.Contains(setup, "FailureDiagnostic(failure, \"package\")");
        StringAssert.Contains(setup, "FailureDiagnostic(commands.Failure!, \"create\")");
        StringAssert.Contains(setup, "setupapi.dev.log");
        StringAssert.Contains(setup, "nativeCodeHex");
        StringAssert.Contains(setup, "reasonCode");
        Assert.IsFalse(Regex.IsMatch(setup,
            "\\\"sudovda\\.cer\\\"\\s*\\}\\s*\\.OrderBy\\("));
        foreach (var reason in new[]
        {
            "installerToolMissing", "installerToolReparse",
            "installerToolHashMismatch", "packageFileMissing",
            "packageFileReparse", "packageHashMismatch"
        }) StringAssert.Contains(setup, reason);
        StringAssert.Contains(setup, "logPathState");
        StringAssert.Contains(setup,
            "present ? \"present\" : \"absent\"");
        StringAssert.Contains(setup, "ValidatePackage(driverRoot, request.PackageSha256");
        StringAssert.Contains(setup, "request.InstallerToolSha256");
        StringAssert.Contains(setup, "--create-device-node");
        StringAssert.Contains(setup, "--install-driver");
        StringAssert.Contains(setup, "--inf-path");
        StringAssert.Contains(setup, "RunPinnedTool(toolPath, arguments");
        StringAssert.Contains(setup, "request.InstallerToolSha256, driverRoot");
        StringAssert.Contains(setup, "RollbackProvisionMutation");
        StringAssert.Contains(setup, "RollbackTrustMutation");
        Assert.IsFalse(setup.Contains("SetupCopyOEMInf", StringComparison.Ordinal));
        Assert.IsTrue(setup.IndexOf("var trustResult = EnsureTrust(driverRoot",
                StringComparison.Ordinal) <
            setup.IndexOf("var commands = ExecuteDriverCommandSequence(",
                StringComparison.Ordinal));
        Assert.IsTrue(setup.IndexOf(
                "ValidatePackage(driverRoot, request.PackageSha256",
                StringComparison.Ordinal) < setup.IndexOf(
                "var trustResult = EnsureTrust(driverRoot",
                StringComparison.Ordinal));
        Assert.IsTrue(setup.IndexOf("CreateDriverArguments", StringComparison.Ordinal) <
            setup.IndexOf("InstallDriverArguments", StringComparison.Ordinal));
        StringAssert.Contains(management,
            "installerToolSha256 = [string]$Manifest.virtualDisplay.installerToolSha256");
        Assert.IsFalse(setup.Contains(
            "legacyStores.Length is < 1 or > 2", StringComparison.Ordinal));

        foreach (var retired in new[]
                 {
                     "VirtualDisplayDiagnostic",
                     "VirtualDisplayInventoryHelper",
                     "Invoke-VirtualDisplayNativeRemoval",
                     "Invoke-VirtualDisplayRemovalReconciliation",
                     "Get-PnpDeviceProperty",
                     "EncodedCommand",
                     "virtual-display-outcome",
                     "finalize-handoff",
                     "CleanupLegacyDriverTrust"
                 })
        {
            Assert.IsFalse(management.Contains(retired, StringComparison.Ordinal),
                $"Retired authority remains: {retired}");
        }
        Assert.IsFalse(File.Exists(Path.Combine(
            repo, "src_assets", "windows", "drivers", "sudovda",
            "install.bat")));
        Assert.IsFalse(File.Exists(Path.Combine(
            repo, "src_assets", "windows", "drivers", "sudovda",
            "uninstall.bat")));
        Assert.IsFalse(File.Exists(Path.Combine(
            repo, "tools", "Ligase.VirtualDisplay.InventoryHelper",
            "Program.cs")));
        Assert.IsFalse(File.Exists(Path.Combine(
            repo, "tools", "Ligase.VirtualDisplay.InventoryHelper",
            "Ligase.VirtualDisplay.InventoryHelper.csproj")));
    }

    [TestMethod]
    public void VirtualDisplayComponentDefaultsSelectedButRemainsOptionalAndRiskGated()
    {
        var repo = FindRepositoryRoot();
        var nsis = File.ReadAllText(Path.Combine(
            repo, "packaging", "windows", "ligase", "LigaseHost.nsi"));
        const string header =
            "Section \"Ligase 虚拟显示（默认勾选，可取消）\" SEC_VDISPLAY";
        StringAssert.Contains(nsis, header);
        Assert.IsFalse(nsis.Contains(
            "Section /o \"Ligase 虚拟显示", StringComparison.Ordinal));
        Assert.IsFalse(nsis.Contains(
            "SectionSetFlags ${LIGASE_SECTION_VIRTUAL_DISPLAY}",
            StringComparison.Ordinal));

        var section = Regex.Match(nsis,
            "(?ms)^" + Regex.Escape(header) +
            "\\r?\\n(?<body>.*?)^SectionEnd\\s*$");
        Assert.IsTrue(section.Success);
        var body = section.Groups["body"].Value;

        // No SectionIn RO: the default applies equally to fresh and upgrade,
        // but the user can still clear the checkbox on the Components page.
        Assert.IsFalse(body.Contains("SectionIn RO", StringComparison.Ordinal));
        StringAssert.Contains(body, "自签名发布者证书");
        StringAssert.Contains(body, "本机信任存储");
        StringAssert.Contains(body, "Windows 安装内核驱动");
        StringAssert.Contains(body, "串流物理桌面不需要此组件");
        StringAssert.Contains(body, "MB_YESNO");
        StringAssert.Contains(body, "IDNO skipVirtualDisplay");

        var warning = body.IndexOf("MessageBox MB_YESNO", StringComparison.Ordinal);
        var privilegedCall = body.IndexOf(
            "-Action InstallVirtualDisplay", StringComparison.Ordinal);
        Assert.IsTrue(warning >= 0 && privilegedCall > warning);
        Assert.AreEqual(1, Regex.Matches(body,
            Regex.Escape("-Action InstallVirtualDisplay")).Count);

        // NSIS does not execute the body of an unselected section. Keeping
        // every privileged virtual-display action inside this one optional
        // section is the mutation-zero contract for an explicit opt-out.
        var installerOnlyAction = Regex.Match(nsis,
            "(?m)^\\s*nsExec::ExecToStack .*?-Action InstallVirtualDisplay.*$");
        Assert.IsTrue(installerOnlyAction.Success);
        Assert.IsTrue(installerOnlyAction.Index >= section.Index &&
            installerOnlyAction.Index < section.Index + section.Length);
    }

    [TestMethod]
    public void UpgradeGatePromptsBeforeWritesAndRestrictsLegacyForceToExactReceipts()
    {
        var repo = FindRepositoryRoot();
        var nsis = File.ReadAllText(Path.Combine(
            repo, "packaging", "windows", "ligase", "LigaseHost.nsi"));
        var management = File.ReadAllText(Path.Combine(
            repo, "packaging", "windows", "ligase",
            "Manage-LigaseInstallation.ps1"));
        var tray = File.ReadAllText(Path.Combine(
            repo, "src", "Ligase.Desktop", "Services",
            "WindowsTrayIconService.cs"));
        var shutdownService = File.ReadAllText(Path.Combine(
            repo, "src", "Ligase.Desktop", "Services",
            "InstallerShutdownService.cs"));
        var app = File.ReadAllText(Path.Combine(
            repo, "src", "Ligase.Desktop", "App.xaml.cs"));
        var coreManager = File.ReadAllText(Path.Combine(
            repo, "src", "Ligase.Core", "Services",
            "ApolloInstanceManager.cs"));
        var nativeCore = File.ReadAllText(Path.Combine(repo, "src", "main.cpp"));
        var singleInstance = File.ReadAllText(Path.Combine(
            repo, "src", "Ligase.Desktop", "Services", "SingleInstanceService.cs"));
        var releaseGate = File.ReadAllText(Path.Combine(
            repo, "packaging", "windows", "ligase",
            "Test-LigaseReleaseShutdown.ps1"));

        StringAssert.Contains(nsis, "Function EnsureRunningProductClosed");
        StringAssert.Contains(nsis, "-Action QueryRunningProduct");
        StringAssert.Contains(nsis, "-Action CloseRunningProduct");
        StringAssert.Contains(nsis, "关闭并继续");
        StringAssert.Contains(nsis, "优雅退出");
        Assert.IsTrue(nsis.IndexOf("Call EnsureRunningProductClosed",
            StringComparison.Ordinal) < nsis.IndexOf(
            "Call RecordInstallerEvidenceConfirmed", StringComparison.Ordinal));
        Assert.IsTrue(nsis.IndexOf("Call EnsureRunningProductClosed",
            StringComparison.Ordinal) < nsis.IndexOf(
            "SetOutPath \"$INSTDIR\"", StringComparison.Ordinal));
        Assert.IsFalse(nsis.Contains("MB_ABORTRETRYIGNORE",
            StringComparison.Ordinal));

        StringAssert.Contains(management, "function Get-RunningLigaseProductProcesses");
        StringAssert.Contains(management, "function Request-RunningLigaseProductExit");
        StringAssert.Contains(management, "Ligase Host.exe");
        StringAssert.Contains(management, "Desktop\\Ligase.Host.Desktop.exe");
        StringAssert.Contains(management, "Core\\sunshine.exe");
        StringAssert.Contains(management, "LigaseProductShutdownClient");
        StringAssert.Contains(management, "NamedPipeClientStream");
        StringAssert.Contains(management, "GetNamedPipeServerProcessId");
        StringAssert.Contains(management, "shutdownAcknowledged");
        StringAssert.Contains(management, "7000");
        StringAssert.Contains(management, "Write-ShutdownTerminal");
        StringAssert.Contains(management,
            "Write-RunningLigaseProductQueryTerminal");
        StringAssert.Contains(management, "operation = \"query\"");
        StringAssert.Contains(management, "operation = \"close\"");
        StringAssert.Contains(management, "shutdownResidualProcesses");
        StringAssert.Contains(management, "desktopTerminalReceived");
        StringAssert.Contains(management, "LigaseRestartManager");
        StringAssert.Contains(management, "RmRegisterResources");
        StringAssert.Contains(management, "RmGetList");
        StringAssert.Contains(management, "Test-ExactProcessLockerSet");
        StringAssert.Contains(management, "Open-LegacyForceAuthority");
        StringAssert.Contains(management, "Test-LegacyForceAuthorityCurrent");
        StringAssert.Contains(management, "ShutdownLockingProcesses");
        StringAssert.Contains(management, "RmShutdown(handle, force ? 1u : 0u");
        StringAssert.Contains(management, "legacyForceIneligible");
        StringAssert.Contains(management, "legacyAuthorityFinalDrift");
        StringAssert.Contains(management, "programWriteCalls = 0");
        StringAssert.Contains(management, "UserConfirmedClose");
        StringAssert.Contains(management, "EvaluateLegacyForceEligibility");
        StringAssert.Contains(management, "Invoke-LegacyForceEligibilityDryRun");
        StringAssert.Contains(management, "wouldBeForcedEligible");
        StringAssert.Contains(management, "forcedAttempted = $false");
        StringAssert.Contains(management, "rmShutdownCalls = 0");
        StringAssert.Contains(management, "RM_UNIQUE_PROCESS");
        StringAssert.Contains(management, "StartFileTimeUtc");
        StringAssert.Contains(management, "legacyAuthorityProcessDrift");
        StringAssert.Contains(management, "legacyAuthorityRestartManagerDrift");
        StringAssert.Contains(management, "legacyAuthorityPathMismatch");
        StringAssert.Contains(management, "legacyAuthorityArtifactMismatch");
        StringAssert.Contains(management, "legacyAuthoritySignerMismatch");
        StringAssert.Contains(management, "legacyAuthorityLockerSetMismatch");
        StringAssert.Contains(management, "simulateGraceful351ForcePermission");
        StringAssert.Contains(management, "simulateGraceful351ForceCompleted");
        StringAssert.Contains(nsis, "-UserConfirmedClose");
        StringAssert.Contains(management, "parentPid");
        StringAssert.Contains(management, "startedUtc");
        StringAssert.Contains(management, "restartManagerResidualLocks");
        StringAssert.Contains(management, "TryReadTerminal");
        StringAssert.Contains(management, "shutdownTerminalInvalid");
        var closeBody = Regex.Match(management,
            "(?ms)^function Request-RunningLigaseProductExit\\s*\\{(?<body>.*?)^\\}");
        Assert.IsTrue(closeBody.Success);
        Assert.IsFalse(closeBody.Groups["body"].Value.Contains(
            ".Kill", StringComparison.Ordinal));
        Assert.IsFalse(closeBody.Groups["body"].Value.Contains(
            "TerminateProcess", StringComparison.Ordinal));
        Assert.IsFalse(closeBody.Groups["body"].Value.Contains(
            "taskkill", StringComparison.OrdinalIgnoreCase));

        Assert.IsFalse(tray.Contains("InstallerExitMessage", StringComparison.Ordinal));
        StringAssert.Contains(shutdownService, "PipeOptions.CurrentUserOnly");
        StringAssert.Contains(shutdownService, "state\\\":\\\"accepted");
        StringAssert.Contains(shutdownService, "shutdownProtocolVersion\\\":3");
        StringAssert.Contains(shutdownService, "exitCommitFailed");
        StringAssert.Contains(app, "PrepareForInstallerShutdownAsync");
        StringAssert.Contains(app, "StopForInstallerAsync");
        StringAssert.Contains(app, "BeginExit");
        StringAssert.Contains(app, "exitCommitted");
        Assert.IsFalse(app.Contains(
            "ResumeAfterInstallerShutdownFailure", StringComparison.Ordinal));
        var window = File.ReadAllText(Path.Combine(
            repo, "src", "Ligase.Desktop", "MainWindow.xaml.cs"));
        StringAssert.Contains(window, "_installerShutdownFrozen");
        StringAssert.Contains(window,
            "if (_isExiting || _installerShutdownFrozen) return;");
        StringAssert.Contains(singleInstance, "public void BeginExit()");
        StringAssert.Contains(singleInstance,
            "if (Volatile.Read(ref _isExiting) != 0) return;");
        StringAssert.Contains(releaseGate, "[ValidateRange(5, 10)]");
        StringAssert.Contains(releaseGate, "releaseProductShutdownGatePassed");
        StringAssert.Contains(releaseGate, "restartObserved=$false");
        StringAssert.Contains(releaseGate, "finalResidual.restartManager.processes");
        StringAssert.Contains(coreManager, "RequestGracefulExit()");
        var installerStop = Regex.Match(coreManager,
            "(?ms)public async Task<ApolloStopOutcome> StopForInstallerAsync.*?^    \\}");
        Assert.IsTrue(installerStop.Success);
        Assert.IsFalse(installerStop.Value.Contains(".Kill(", StringComparison.Ordinal));
        StringAssert.Contains(nativeCore, "WM_LIGASE_MANAGED_SHUTDOWN");
    }

    [TestMethod]
    public async Task UpgradeShutdownProtocolClosesOwnedProcessChainAndFailsClosed()
    {
        var repo = FindRepositoryRoot();
        var dotnet = @"D:\Development\Ligase\Dependencies\dotnet-sdk-8.0.100\dotnet.exe";
        var harness = Path.Combine(
            repo, "packaging", "windows", "ligase",
            "Test-LigaseProductShutdown.ps1");
        Assert.IsTrue(File.Exists(dotnet));
        Assert.IsTrue(File.Exists(harness));

        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive",
            "-File", harness,
            "-DotNetPath", dotnet,
            "-SourceRoot", repo
        }) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("shutdownFixtureStartFailed");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(180));
        Assert.AreEqual(0, process.ExitCode, await stderr);
        var lines = (await stdout).Split(
            new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        using var terminal = JsonDocument.Parse(lines[^1]);
        var root = terminal.RootElement;
        Assert.AreEqual("shutdownProtocolFixturePassed",
            root.GetProperty("code").GetString());
        Assert.AreEqual(17, root.GetProperty("cases").GetInt32());
        Assert.IsTrue(root.GetProperty("acknowledged").GetBoolean());
        Assert.IsTrue(root.GetProperty("legacyReorderedAccepted").GetBoolean());
        Assert.IsTrue(root.GetProperty("legacyCleanupFaultRecovered").GetBoolean());
        Assert.AreEqual(1, root.GetProperty("launcherStarts").GetInt32());
        Assert.IsTrue(root.GetProperty(
            "ackUnavailableRecoveredByRestartManager").GetBoolean());
        Assert.IsTrue(root.GetProperty(
            "terminalFailureRecoveredByRestartManager").GetBoolean());
        Assert.IsTrue(root.GetProperty(
            "terminalEofRecoveredByRestartManager").GetBoolean());
        Assert.IsTrue(root.GetProperty(
            "residualRecoveredByRestartManager").GetBoolean());
        Assert.IsTrue(root.GetProperty("forceUsed").GetBoolean());
        Assert.AreEqual(8, root.GetProperty("forceNegatives").GetInt32());
        Assert.IsTrue(root.GetProperty("staleIgnored").GetBoolean());
        Assert.AreEqual(0, root.GetProperty("processResidue").GetInt32());
        Assert.AreEqual(0, root.GetProperty("fileResidue").GetInt32());
    }

    [TestMethod]
    public async Task InstallerPackageHashUsesWindowsPowerShell51CompatibleDisposedHasher()
    {
        var repo = FindRepositoryRoot();
        var build = File.ReadAllText(Path.Combine(
            repo, "packaging", "windows", "ligase",
            "Build-LigaseInstaller.ps1"));

        Assert.IsFalse(build.Contains("[Security.Cryptography.SHA256]::HashData(",
            StringComparison.Ordinal));
        Assert.IsFalse(build.Contains("[Convert]::ToHexString(",
            StringComparison.Ordinal));
        StringAssert.Contains(build,
            "$virtualDisplayPackageHasher = [Security.Cryptography.SHA256]::Create()");
        StringAssert.Contains(build,
            "$virtualDisplayPackageHasher.ComputeHash(");
        StringAssert.Contains(build,
            "$virtualDisplayPackageHasher.Dispose()");
        StringAssert.Contains(build,
            "[BitConverter]::ToString(");
        StringAssert.Contains(build,
            ".Replace('-', '').ToLowerInvariant()");
        var management = File.ReadAllText(Path.Combine(
            repo, "packaging", "windows", "ligase",
            "Manage-LigaseInstallation.ps1"));
        StringAssert.Contains(management, "function Get-FileSha256(");
        Assert.IsFalse(management.Contains("Get-FileHash",
            StringComparison.Ordinal));
        StringAssert.Contains(management,
            "function Get-AuthenticodeSignatureCompat(");
        Assert.IsFalse(Regex.IsMatch(management,
            @"(?m)^\s*\$signature\s*=\s*Get-AuthenticodeSignature\s"));

        var root = Path.Combine(
            @"D:\Development\Ligase\Build",
            "ps51-package-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-NoLogo");
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("& { " +
                "$ErrorActionPreference='Stop'; " +
                "$chunks=@([byte[]](0,1,2,255),[Text.Encoding]::UTF8.GetBytes('Ligase')); " +
                "$stream=[IO.MemoryStream]::new(); try { foreach($chunk in $chunks){$stream.Write($chunk,0,$chunk.Length)}; " +
                "$bytes=$stream.ToArray() } finally { $stream.Dispose() }; " +
                "$path=Join-Path (Get-Location) 'canonical.bin'; [IO.File]::WriteAllBytes($path,$bytes); " +
                "$hasher=[Security.Cryptography.SHA256]::Create(); try { $hash=$hasher.ComputeHash($bytes); " +
                "$hex=[BitConverter]::ToString($hash).Replace('-','').ToLowerInvariant() } finally { $hasher.Dispose() }; " +
                "$disposedRejected=$false; try { [void]$hasher.ComputeHash($bytes) } catch [ObjectDisposedException] { $disposedRejected=$true }; " +
                "$file=[IO.File]::OpenRead($path); try { $independentHasher=[Security.Cryptography.SHA256]::Create(); try { " +
                "$independent=[BitConverter]::ToString($independentHasher.ComputeHash($file)).Replace('-','').ToLowerInvariant() " +
                "} finally { $independentHasher.Dispose() } } finally { $file.Dispose() }; " +
                "$fileHashCommand=[bool](Get-Command Get-FileHash -ErrorAction SilentlyContinue); " +
                "[ordered]@{version=$PSVersionTable.PSVersion.ToString(); hash=$hex; independent=$independent; fileHashCommand=$fileHashCommand; " +
                "length=$hex.Length; lowercase=($hex -cmatch '^[0-9a-f]{64}$'); disposedRejected=$disposedRejected} | ConvertTo-Json -Compress }");
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("powershellStartFailed");
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.AreEqual(0, process.ExitCode, error);
            using var result = JsonDocument.Parse(output);
            var value = result.RootElement;
            StringAssert.StartsWith(value.GetProperty("version").GetString(), "5.1");
            Assert.AreEqual(
                value.GetProperty("independent").GetString(),
                value.GetProperty("hash").GetString());
            Assert.AreEqual(64, value.GetProperty("length").GetInt32());
            Assert.IsTrue(value.GetProperty("lowercase").GetBoolean());
            Assert.IsTrue(value.GetProperty("disposedRejected").GetBoolean());
            Assert.IsFalse(value.GetProperty("fileHashCommand").GetBoolean(),
                "The controlled inherited module path must prove that the cmdlet is unavailable.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void InstallerBuildCapturesNsisStdoutAndStderrAsAtomicEvidence()
    {
        var repo = FindRepositoryRoot();
        var build = File.ReadAllText(Path.Combine(
            repo, "packaging", "windows", "ligase", "Build-LigaseInstaller.ps1"));
        var capture = File.ReadAllText(Path.Combine(
            repo, "packaging", "windows", "ligase", "Invoke-NsisCompiler.ps1"));

        StringAssert.Contains(build, "Invoke-NsisCompiler.ps1");
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(build,
            @"(?m)^\s*&\s*\$MakeNsis\s+@nsisArguments\s*$"));
        StringAssert.Contains(capture, "RedirectStandardOutput = $true");
        StringAssert.Contains(capture, "RedirectStandardError = $true");
        StringAssert.Contains(capture, "nsisCompilerEvidenceCaptured");
        StringAssert.Contains(capture, "makensis.stdout.log");
        StringAssert.Contains(capture, "makensis.stderr.log");
        StringAssert.Contains(capture, "[IO.FileOptions]::WriteThrough");
        StringAssert.Contains(capture, "nsisEvidenceFinalReadbackFailed");
        StringAssert.Contains(capture, "nsisOutputOverflow");
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(capture,
            @"(?m)^\s*exit(?:\s|$)"));
        StringAssert.Contains(build, "$nsisResult = &");
        StringAssert.Contains(build, "nsisBuildFailed:$([int]$nsisResult.exitCode)");
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
                WorkingDirectory = repo,
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
            WorkingDirectory = repo,
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
                GitMarkerExists(full))
            {
                return full;
            }
            throw new DirectoryNotFoundException("configuredRepositoryRootInvalid");
        }
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CMakeLists.txt")) &&
                GitMarkerExists(directory.FullName))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("repositoryRootUnavailable");
    }

    private static bool GitMarkerExists(string root)
    {
        var marker = Path.Combine(root, ".git");
        return Directory.Exists(marker) || File.Exists(marker);
    }

    [TestMethod]
    public void DesktopWindowsSdkReferenceIsPinnedToReviewedOfflineAuthority()
    {
        var repo = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(
            repo, "src", "Ligase.Desktop", "Ligase.Host.Desktop.csproj"));
        var build = File.ReadAllText(Path.Combine(
            repo, "packaging", "windows", "ligase", "Build-LigaseInstaller.ps1"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repo, "packaging", "windows", "ligase", "offline-dotnet-dependencies-v1.json")));

        StringAssert.Contains(project,
            "<WindowsSdkPackageVersion>10.0.19041.38</WindowsSdkPackageVersion>");
        StringAssert.Contains(project,
            "<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>");
        StringAssert.Contains(project,
            "<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>");
        StringAssert.Contains(build,
            "SelectNodes('/Project/PropertyGroup/WindowsSdkPackageVersion')");
        StringAssert.Contains(build,
            "SelectNodes('/Project/PropertyGroup/TargetFramework')");
        StringAssert.Contains(build,
            "SelectNodes('/Project/PropertyGroup/TargetPlatformMinVersion')");
        Assert.AreEqual(5,
            Regex.Matches(build, "'-p:UseSharedCompilation=false'").Count);
        Assert.AreEqual(5,
            Regex.Matches(build, "'-nodeReuse:false'").Count);
        Assert.AreEqual(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        var package = manifest.RootElement.GetProperty("packages")[0];
        Assert.AreEqual("Microsoft.Windows.SDK.NET.Ref", package.GetProperty("id").GetString());
        Assert.AreEqual("10.0.19041.38", package.GetProperty("version").GetString());
        Assert.AreEqual(9385829, package.GetProperty("size").GetInt64());
        Assert.AreEqual("c16a0a93ad01556b69ee24441f482f37706f2a9fdb5832cd7fdcd261d66c1558",
            package.GetProperty("sha256").GetString());
        StringAssert.Contains(build, "Assert-WindowsSdkReferenceResolution");
        StringAssert.Contains(build, "windowsSdkReferenceResolvedVersionInvalid");
        StringAssert.Contains(build, "windowsAppSdkRequiredReferenceUnverified");
        StringAssert.Contains(build,
            "<Required>10.0.$([System.Version]::Parse(\"$(WindowsSdkPackageVersion.Split(''-'')[0])\").Build).38</Required>");
        StringAssert.Contains(build,
            "VersionGreaterThanOrEquals(%(Referenced), %(Required))");
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
                WorkingDirectory = FindRepositoryRoot(),
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
