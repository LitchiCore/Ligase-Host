[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RepositoryRoot,

    [string] $ProductionArtifact,

    [string] $ValidationArtifact,

    [string] $FixtureRoot,

    [switch] $RunArtifactGates
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script:ProcessStartCount = 0

function Get-SafeAdminRootFingerprint {
    param([string] $Root)

    $root = if ([string]::IsNullOrWhiteSpace($Root)) {
        Join-Path $env:ProgramData 'Ligase Host Admin'
    }
    else {
        $Root
    }
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        return 'absent'
    }

    $acl = Get-Acl -LiteralPath $root
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('root=present')
    $lines.Add('owner=' + $acl.Owner)
    $lines.Add(
        'protected=' +
        $acl.AreAccessRulesProtected.ToString().ToLowerInvariant())
    $lines.Add(
        'sddl=' + $acl.GetSecurityDescriptorSddlForm(
            [Security.AccessControl.AccessControlSections]::All))
    foreach ($entry in @(
            Get-ChildItem -LiteralPath $root -Force -Recurse |
                Sort-Object FullName)) {
        $relative = $entry.FullName.Substring($root.Length)
        $type = if ($entry -is [IO.FileInfo]) { 'file' } else { 'directory' }
        $length = if ($entry -is [IO.FileInfo]) { $entry.Length } else { 0 }
        $lines.Add(
            $relative + '|' + $type + '|' +
            $length + '|' + [int]$entry.Attributes)
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes(
            [string]::Join("`n", $lines))
        return [Convert]::ToBase64String($sha.ComputeHash($bytes))
    }
    finally {
        $sha.Dispose()
    }
}

function New-FixedAsciiProcessStartInfo {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Artifact,
        [Parameter(Mandatory = $true)]
        [ValidateSet(
            '--production-extra-argv',
            '--validate-argv-token',
            '--validate-child-policy',
            '--validate-hang-pipes',
            '--validate-policy-set-fault',
            '--validate-policy-readback-mismatch')]
        [string] $Token,
        [switch] $SimulateUnsupportedArgumentApi
    )

    if ($Token -notmatch '\A[\x20-\x7e]+\z' -or
        $Token.Contains('"') -or $Token.Contains('\')) {
        throw 'secureStorePreflightArtifactTokenInvalid'
    }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Artifact
    $start.WorkingDirectory = Split-Path -Parent $Artifact
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.CreateNoWindow = $true
    if ($SimulateUnsupportedArgumentApi) {
        throw 'secureStorePreflightUnsupportedArgumentApi'
    }
    # All tokens are fixed ASCII with no whitespace or shell metacharacters.
    # Windows PowerShell does not expose the newer per-argument list API.
    $start.Arguments = $Token
    return $start
}

function Invoke-FixedArtifactToken {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Artifact,
        [Parameter(Mandatory = $true)]
        [string] $Token,
        [hashtable] $Environment = @{},
        [ValidateSet(
            'none',
            'taskkillHang',
            'taskkillExitNonzero',
            'targetKillFailure',
            'targetWaitTimeout')]
        [string] $CleanupFaultMode = 'none'
    )

    $start = New-FixedAsciiProcessStartInfo `
        -Artifact $Artifact -Token $Token
    foreach ($name in $Environment.Keys) {
        $start.EnvironmentVariables[$name] = [string]$Environment[$name]
    }
    $script:ProcessStartCount++
    $process = [Diagnostics.Process]::Start($start)
    if ($null -eq $process) {
        throw 'secureStorePreflightArtifactStartFailed'
    }
    try {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $deadline = [Diagnostics.Stopwatch]::StartNew()
        while (-not $process.WaitForExit(25) -and
            $deadline.ElapsedMilliseconds -lt 15000) {
        }
        $timedOut = -not $process.HasExited
        if ($timedOut) {
            $targetPid = $process.Id
            try {
                $killer = $null
                try {
                $taskkill = [Diagnostics.ProcessStartInfo]::new()
                $taskkill.FileName = if ($CleanupFaultMode -eq 'none') {
                    Join-Path $env:SystemRoot 'System32\taskkill.exe'
                }
                else {
                    $Artifact
                }
                $taskkill.UseShellExecute = $false
                $taskkill.CreateNoWindow = $true
                $taskkill.Arguments = switch ($CleanupFaultMode) {
                    'none' { '/PID ' + $targetPid + ' /T /F' }
                    'taskkillHang' { '--validate-hang-pipes' }
                    default { '--validate-policy-set-fault' }
                }
                $killer = [Diagnostics.Process]::Start($taskkill)
                if ($null -eq $killer) {
                    throw 'secureStorePreflightTaskkillStartFailed'
                }
                $killerPid = $killer.Id
                $killerWait = [Math]::Max(
                    1,
                    [Math]::Min(
                        2000,
                        20000 - [int]$deadline.ElapsedMilliseconds))
                if (-not $killer.WaitForExit($killerWait)) {
                    $killer.Kill()
                    $killerKillWait = [Math]::Max(
                        1,
                        [Math]::Min(
                            1000,
                            20000 - [int]$deadline.ElapsedMilliseconds))
                    if (-not $killer.WaitForExit($killerKillWait)) {
                        throw 'secureStorePreflightTaskkillWaitFailed'
                    }
                }
                if ($null -ne (Get-Process -Id $killerPid `
                        -ErrorAction SilentlyContinue)) {
                    throw 'secureStorePreflightTaskkillResidue'
                }
                }
                catch {
                    if ($null -ne $killer -and -not $killer.HasExited) {
                        $killer.Kill()
                        $killerFallbackWait = [Math]::Max(
                            1,
                            [Math]::Min(
                                1000,
                                20000 - [int]$deadline.ElapsedMilliseconds))
                        if (-not $killer.WaitForExit($killerFallbackWait)) {
                            throw 'secureStorePreflightCleanupFailed'
                        }
                    }
                }
                finally {
                    if ($null -ne $killer) {
                        $killer.Dispose()
                    }
                }
                $targetWait = [Math]::Max(
                    1,
                    [Math]::Min(
                        1000,
                        20000 - [int]$deadline.ElapsedMilliseconds))
                $targetExited = $process.WaitForExit($targetWait)
                if (-not $targetExited) {
                    if ($CleanupFaultMode -ne 'targetKillFailure') {
                        $process.Kill()
                    }
                    $targetKillWait = [Math]::Max(
                        1,
                        [Math]::Min(
                            1000,
                            20000 - [int]$deadline.ElapsedMilliseconds))
                    $targetExited = if (
                        $CleanupFaultMode -eq 'targetWaitTimeout') {
                        $false
                    }
                    else {
                        $process.WaitForExit($targetKillWait)
                    }
                    if (-not $targetExited) {
                        if (-not $process.HasExited) {
                            $process.Kill()
                        }
                        if (-not $process.WaitForExit($targetKillWait)) {
                            throw 'secureStorePreflightCleanupFailed'
                        }
                    }
                    if (-not $process.HasExited) {
                        throw 'secureStorePreflightCleanupFailed'
                    }
                }
                if ($null -ne (Get-Process -Id $targetPid `
                        -ErrorAction SilentlyContinue)) {
                    throw 'secureStorePreflightCleanupFailed'
                }
            }
            catch {
                throw 'secureStorePreflightCleanupFailed'
            }
        }
        $drainWait = [Math]::Max(
            1,
            [Math]::Min(
                2000,
                20000 - [int]$deadline.ElapsedMilliseconds))
        $drained = [Threading.Tasks.Task]::WaitAll(
            [Threading.Tasks.Task[]]@($stdoutTask, $stderrTask),
            $drainWait)
        if ($timedOut) {
            return [pscustomobject]@{
                exitCode = 18
                stdout = ''
                stderr = ''
                timedOut = $true
                pipesDrained = $drained
                elapsedMilliseconds = $deadline.ElapsedMilliseconds
                processId = $process.Id
            }
        }
        if (-not $drained) {
            throw 'secureStorePreflightArtifactPipeDrainTimeout'
        }
        return [pscustomobject]@{
            exitCode = $process.ExitCode
            stdout = $stdoutTask.Result
            stderr = $stderrTask.Result
            timedOut = $false
            pipesDrained = $true
            elapsedMilliseconds = $deadline.ElapsedMilliseconds
            processId = $process.Id
        }
    }
    finally {
        $process.Dispose()
    }
}

$programPath = Join-Path $RepositoryRoot `
    'tools\Ligase.Installation.TransactionHelper\Program.cs'
$projectPath = Join-Path $RepositoryRoot `
    'tools\Ligase.SecureStore.Preflight\Ligase.SecureStore.Preflight.csproj'
$documentPath = Join-Path $RepositoryRoot `
    'docs\ligase-host\fresh-install.md'

foreach ($path in @($programPath, $projectPath, $documentPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'secureStorePreflightSourceMissing'
    }
}

$program = Get-Content -LiteralPath $programPath -Raw
$project = Get-Content -LiteralPath $projectPath -Raw
$document = Get-Content -LiteralPath $documentPath -Raw

foreach ($token in @(
        '#if PREFLIGHT_ONLY',
        'RunStandaloneSecureStorePreflight',
        'private static string _stage = "inputValidation"',
        '_stage = "securityInitialization"',
        'EnableChildProcessMitigation()',
        'ProcessChildProcessPolicy = 13',
        'NoChildProcessCreation = 1',
        'SetProcessMitigationPolicy(',
        'GetProcessMitigationPolicy(',
        'if (readback != NoChildProcessCreation)',
        '_nativeCode = 20013',
        '#if PREFLIGHT_VALIDATION',
        'ValidationArgvToken',
        'ValidationChildPolicy',
        'ValidationHangPipes',
        'ValidationPolicySetFault',
        'ValidationPolicyReadbackMismatch',
        'CreateSuspended',
        'RunPreflightValidation()',
        'CreateProcessW(',
        '\"childCreationBlocked\":true',
        '_stage = "inputValidation"',
        'SetStage("inputValidation")',
        'if (args.Length != 0)',
        '? "invalidArguments"',
        'PreflightReleaseKind = "UnsignedDev"',
        'PreflightTrustBoundary = "localManualExactSha"',
        '"Ligase Host Admin", "Transactions"',
        'store.Preflight()',
        'store.WriteEvidence(',
        'store.DeleteEvidence()',
        'PreflightEvidencePath',
        'terminalCommit: true',
        'if (terminalCommit)',
        'if (!committed && File.Exists(temp))',
        'if (store is not null && !evidenceWriteInProgress)',
        'Console.Error.Write(',
        'Success = true',
        'ResultCode = "secureStorePreflightReady"',
        ': evidenceWriteInProgress',
        'var committingStore = store',
        'store = null',
        'private static void AppendJsonString(',
        'secureStorePreflightEncodingFailed',
        'Environment.SetEnvironmentVariable(',
        '"LIGASE_INSTALL_VALIDATION_HARNESS", null',
        '#if !PREFLIGHT_ONLY')) {
    if ($program.IndexOf($token, [StringComparison]::Ordinal) -lt 0) {
        throw 'secureStorePreflightBoundaryMissing'
    }
}
if ($program.IndexOf(
        'ResumeThread',
        [StringComparison]::Ordinal) -ge 0) {
    throw 'secureStorePreflightValidationChildResumePresent'
}

foreach ($token in @(
        '<DefineConstants>$(DefineConstants);PREFLIGHT_ONLY</DefineConstants>',
        '<PublishTrimmed>true</PublishTrimmed>',
        '<WarningsAsErrors>$(WarningsAsErrors);IL2026;IL3050</WarningsAsErrors>',
        '<SelfContained>true</SelfContained>',
        '<RuntimeIdentifier>win-x64</RuntimeIdentifier>')) {
    if ($project.IndexOf($token, [StringComparison]::Ordinal) -lt 0) {
        throw 'secureStorePreflightProjectBoundaryInvalid'
    }
}
if ($project.IndexOf(
        'PREFLIGHT_VALIDATION',
        [StringComparison]::Ordinal) -ge 0) {
    throw 'secureStorePreflightValidationSymbolInProductionProject'
}

foreach ($forbiddenSerializer in @(
        'JsonSerializer',
        'JsonSerializerOptions',
        'JsonNamingPolicy')) {
    if ($program.IndexOf(
            $forbiddenSerializer,
            [StringComparison]::Ordinal) -ge 0) {
        throw 'secureStorePreflightReflectionSerializerPresent'
    }
}

$preflightStart = $program.IndexOf(
    'private static int RunStandaloneSecureStorePreflight',
    [StringComparison]::Ordinal)
$preflightEnd = $program.IndexOf(
    'private static byte[] SerializeStandalonePreflightEvidence',
    $preflightStart,
    [StringComparison]::Ordinal)
if ($preflightStart -lt 0 -or $preflightEnd -le $preflightStart) {
    throw 'secureStorePreflightMainBoundaryInvalid'
}
$ordinaryProgram = $program.Substring(0, $preflightStart)
if ($ordinaryProgram.IndexOf(
        '_stage = "securityInitialization"',
        [StringComparison]::Ordinal) -ge 0 -or
    $ordinaryProgram.IndexOf(
        'private static string _stage = "inputValidation"',
        [StringComparison]::Ordinal) -lt 0 -or
    $ordinaryProgram.IndexOf(
        'if (args.Length is < 1 or > 3)',
        [StringComparison]::Ordinal) -lt 0) {
    throw 'transactionHelperInitialStageDrifted'
}
$preflight = $program.Substring(
    $preflightStart, $preflightEnd - $preflightStart)
$mitigation = $preflight.IndexOf(
    'EnableChildProcessMitigation()',
    [StringComparison]::Ordinal)
$securityStage = $preflight.IndexOf(
    '_stage = "securityInitialization"',
    [StringComparison]::Ordinal)
$inputStage = $preflight.IndexOf(
    '_stage = "inputValidation"',
    [StringComparison]::Ordinal)
$argumentCheck = $preflight.IndexOf(
    'if (args.Length != 0)',
    [StringComparison]::Ordinal)
$environmentAccess = $preflight.IndexOf(
    'Environment.SetEnvironmentVariable(',
    [StringComparison]::Ordinal)
$validatedStage = $preflight.IndexOf(
    'SetStage("inputValidation")',
    [StringComparison]::Ordinal)
$programDataAccess = $preflight.IndexOf(
    'Environment.GetFolderPath(',
    [StringComparison]::Ordinal)
$secureStoreAccess = $preflight.IndexOf(
    'SecureStore.Open(',
    [StringComparison]::Ordinal)
if ($securityStage -lt 0 -or $mitigation -le $securityStage -or
    $inputStage -le $mitigation -or
    $argumentCheck -le $inputStage -or
    $environmentAccess -le $argumentCheck -or
    $validatedStage -le $environmentAccess -or
    $programDataAccess -le $validatedStage -or
    $secureStoreAccess -le $validatedStage) {
    throw 'secureStorePreflightInputStageOrderInvalid'
}
foreach ($forbidden in @(
        'Process.Start',
        'ProcessStartInfo',
        'powershell',
        'Manage-LigaseInstallation',
        'ligase-install-manifest',
        'PayloadRoot',
        'Ligase Host Diagnostics',
        'bootstrap',
        'firewall',
        'shortcut',
        'registry')) {
    if ($preflight.IndexOf(
            $forbidden, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw 'secureStorePreflightForbiddenSurface'
    }
}

if ($program.IndexOf(
        '"secure-store-preflight-outcome.json"',
        [StringComparison]::Ordinal) -lt 0 -or
    $program.IndexOf(
        'bytes, PreflightEvidencePath, ".preflight-evidence-"',
        [StringComparison]::Ordinal) -lt 0) {
    throw 'secureStorePreflightEvidenceAuthorityInvalid'
}
if ($program.IndexOf(
        'ReadBounded(stream).SequenceEqual(bytes)',
        [StringComparison]::Ordinal) -lt 0) {
    throw 'secureStorePreflightEvidenceReadbackMissing'
}
$terminalCommit = $program.IndexOf(
    'if (terminalCommit)',
    [StringComparison]::Ordinal)
$finalReadback = $program.IndexOf(
    'SetStage("finalReadback")',
    $terminalCommit,
    [StringComparison]::Ordinal)
if ($terminalCommit -lt 0 -or $finalReadback -le $terminalCommit) {
    throw 'secureStorePreflightTerminalCommitOrderInvalid'
}
if ($preflight.IndexOf(
        'Console.Out.Write',
        [StringComparison]::Ordinal) -ge 0) {
    throw 'secureStorePreflightSuccessConsoleIsAuthority'
}
$commitCall = $preflight.IndexOf(
    'committingStore.WriteEvidence(',
    [StringComparison]::Ordinal)
$nativeSuccess = $preflight.IndexOf(
    'return 0;',
    $commitCall,
    [StringComparison]::Ordinal)
if ($commitCall -lt 0 -or $nativeSuccess -lt 0 -or
    $preflight.Substring(
        $commitCall,
        $nativeSuccess - $commitCall).IndexOf(
            'Console.', [StringComparison]::Ordinal) -ge 0) {
    throw 'secureStorePreflightPostCommitWorkDetected'
}

$validationStart = $program.IndexOf(
    'private static void ApplyValidationHarnessBehavior',
    [StringComparison]::Ordinal)
$validationGuard = $program.LastIndexOf(
    '#if !PREFLIGHT_ONLY',
    $validationStart,
    [StringComparison]::Ordinal)
if ($validationStart -lt 0 -or $validationGuard -lt 0 -or
    $validationStart - $validationGuard -gt 80) {
    throw 'secureStorePreflightChildSeamNotExcluded'
}

foreach ($token in @(
        'localManualExactSha',
        'Unknown publisher',
        'hash check and UAC image load',
        'Authenticode publisher',
        'timestamp',
        'protected staging')) {
    if ($document.IndexOf(
            $token, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw 'secureStorePreflightDocumentationMissing'
    }
}

$artifactChecks = 0
if ($RunArtifactGates) {
    foreach ($artifact in @($ProductionArtifact, $ValidationArtifact)) {
        if ([string]::IsNullOrWhiteSpace($artifact) -or
            -not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
            throw 'secureStorePreflightArtifactMissing'
        }
    }
    if ([string]::IsNullOrWhiteSpace($FixtureRoot)) {
        throw 'secureStorePreflightFixtureRootMissing'
    }
    $fixtureFull = [IO.Path]::GetFullPath($FixtureRoot)
    if (-not $fixtureFull.StartsWith(
            'D:\Development\Ligase\Build\',
            [StringComparison]::OrdinalIgnoreCase) -or
        (Test-Path -LiteralPath $fixtureFull)) {
        throw 'secureStorePreflightFixtureRootInvalid'
    }
    New-Item -ItemType Directory -Path (
        Join-Path $fixtureFull 'Admin\Transactions\Nested') `
        -Force | Out-Null
    try {
        $nestedFingerprint =
            Get-SafeAdminRootFingerprint (
                Join-Path $fixtureFull 'Admin')
        if ([string]::IsNullOrWhiteSpace($nestedFingerprint) -or
            $nestedFingerprint -eq 'absent' -or
            $nestedFingerprint -ne (
                Get-SafeAdminRootFingerprint (
                    Join-Path $fixtureFull 'Admin'))) {
            throw 'secureStorePreflightNestedFingerprintInvalid'
        }
    }
    finally {
        if (Test-Path -LiteralPath $fixtureFull) {
            Remove-Item -LiteralPath $fixtureFull -Recurse -Force
        }
    }

    $before = Get-SafeAdminRootFingerprint
    $startCountBefore = $script:ProcessStartCount
    try {
        New-FixedAsciiProcessStartInfo `
            -Artifact $ProductionArtifact `
            -Token '--production-extra-argv' `
            -SimulateUnsupportedArgumentApi | Out-Null
        throw 'secureStorePreflightUnsupportedArgumentApiNotRejected'
    }
    catch {
        if ($_.Exception.Message -ne
            'secureStorePreflightUnsupportedArgumentApi') {
            throw
        }
    }
    if ($script:ProcessStartCount -ne $startCountBefore) {
        throw 'secureStorePreflightUnsupportedApiStartedProcess'
    }

    $maliciousEnvironment = @{
        LIGASE_INSTALL_VALIDATION_HARNESS = '1'
        LIGASE_TRANSACTION_TEST_BEHAVIOR = 'emitDuplicateCode'
        LIGASE_TRANSACTION_FAILURE_STAGE = 'inputValidation'
        LIGASE_TRANSACTION_TEST_ROOT = 'C:\must-not-be-used'
    }
    $production = Invoke-FixedArtifactToken `
        -Artifact $ProductionArtifact `
        -Token '--production-extra-argv' `
        -Environment $maliciousEnvironment
    if ($production.exitCode -ne 18 -or
        $production.stdout.Length -ne 0 -or
        $production.stderr.Length -gt 4096) {
        throw 'secureStorePreflightProductionArgvBoundaryInvalid'
    }
    $productionFailure = $production.stderr | ConvertFrom-Json
    if ($productionFailure.stage -ne 'inputValidation' -or
        $productionFailure.resultCode -ne 'invalidArguments' -or
        $productionFailure.success -ne $false) {
        throw 'secureStorePreflightProductionArgvProjectionInvalid'
    }

    $argv = Invoke-FixedArtifactToken `
        -Artifact $ValidationArtifact `
        -Token '--validate-argv-token'
    $argvResult = $argv.stdout | ConvertFrom-Json
    if ($argv.exitCode -ne 0 -or $argv.stderr.Length -ne 0 -or
        $argvResult.result -ne 'passed' -or
        $argvResult.policyActive -ne $true -or
        $argvResult.argumentCount -ne 1 -or
        $argvResult.token -ne '--validate-argv-token') {
        throw 'secureStorePreflightValidationArgvInvalid'
    }

    $sentinel = Join-Path (
        Split-Path -Parent $ValidationArtifact) `
        'ligase-preflight-child-sentinel.txt'
    if (Test-Path -LiteralPath $sentinel) {
        throw 'secureStorePreflightChildSentinelPreexisting'
    }
    $child = Invoke-FixedArtifactToken `
        -Artifact $ValidationArtifact `
        -Token '--validate-child-policy'
    $childResult = if ($child.exitCode -eq 0) {
        $child.stdout | ConvertFrom-Json
    }
    else {
        $child.stderr | ConvertFrom-Json
    }
    $childPidProperty = $childResult.PSObject.Properties['childPid']
    if ($null -ne $childPidProperty -and
        [int]$childPidProperty.Value -ne 0) {
        $childPid = [int]$childPidProperty.Value
        $childDeadline = [Diagnostics.Stopwatch]::StartNew()
        while ($null -ne (Get-Process -Id $childPid `
                -ErrorAction SilentlyContinue) -and
            $childDeadline.ElapsedMilliseconds -lt 2000) {
            Start-Sleep -Milliseconds 25
        }
        $childProcess = Get-Process -Id $childPid `
            -ErrorAction SilentlyContinue
        if ($null -ne $childProcess) {
            try {
                $childProcess.Kill()
                if (-not $childProcess.WaitForExit(2000)) {
                    throw 'secureStorePreflightChildCleanupFailed'
                }
            }
            finally {
                $childProcess.Dispose()
            }
        }
        if ($null -ne (Get-Process -Id $childPid `
                -ErrorAction SilentlyContinue) -or
            (Test-Path -LiteralPath $sentinel)) {
            throw 'secureStorePreflightChildCleanupFailed'
        }
        throw 'secureStorePreflightChildCreationUnexpected'
    }
    if ($child.exitCode -ne 0 -or $child.stderr.Length -ne 0 -or
        $childResult.policyActive -ne $true -or
        $childResult.childCreationBlocked -ne $true -or
        $childResult.childProcessCreated -ne $false -or
        $childResult.processHandlesZero -ne $true -or
        $childResult.sentinelExists -ne $false -or
        (Test-Path -LiteralPath $sentinel)) {
        throw 'secureStorePreflightChildPolicyInvalid'
    }

    $hang = Invoke-FixedArtifactToken `
        -Artifact $ValidationArtifact `
        -Token '--validate-hang-pipes'
    if ($hang.timedOut -ne $true -or
        $hang.elapsedMilliseconds -gt 20000 -or
        -not $hang.pipesDrained -or
        $null -ne (Get-Process -Id $hang.processId `
            -ErrorAction SilentlyContinue)) {
        throw 'secureStorePreflightArtifactHangNotBounded'
    }
    foreach ($cleanupFault in @(
            'taskkillHang',
            'taskkillExitNonzero',
            'targetKillFailure',
            'targetWaitTimeout')) {
        $cleanup = Invoke-FixedArtifactToken `
            -Artifact $ValidationArtifact `
            -Token '--validate-hang-pipes' `
            -CleanupFaultMode $cleanupFault
        if ($cleanup.timedOut -ne $true -or
            $cleanup.elapsedMilliseconds -gt 20000 -or
            -not $cleanup.pipesDrained -or
            $null -ne (Get-Process -Id $cleanup.processId `
                -ErrorAction SilentlyContinue)) {
            throw 'secureStorePreflightCleanupFaultNotBounded'
        }
    }

    foreach ($fault in @(
            '--validate-policy-set-fault',
            '--validate-policy-readback-mismatch')) {
        $failure = Invoke-FixedArtifactToken `
            -Artifact $ValidationArtifact -Token $fault
        $failureResult = $failure.stderr | ConvertFrom-Json
        if ($failure.exitCode -ne 18 -or
            $failure.stdout.Length -ne 0 -or
            $failureResult.success -ne $false -or
            $failureResult.stage -ne 'securityInitialization') {
            throw 'secureStorePreflightPolicyFaultProjectionInvalid'
        }
    }

    $productionBytes = [IO.File]::ReadAllBytes($ProductionArtifact)
    $productionStrings =
        [Text.Encoding]::UTF8.GetString($productionBytes) +
        [Text.Encoding]::Unicode.GetString($productionBytes)
    foreach ($forbiddenValidationString in @(
            '--validate-argv-token',
            '--validate-child-policy',
            '--validate-hang-pipes',
            '--validate-policy-set-fault',
            '--validate-policy-readback-mismatch',
            'ligase-preflight-child-sentinel.txt')) {
        if ($productionStrings.IndexOf(
                $forbiddenValidationString,
                [StringComparison]::Ordinal) -ge 0) {
            throw 'secureStorePreflightValidationSeamInProductionArtifact'
        }
    }
    if ((Get-SafeAdminRootFingerprint) -ne $before) {
        throw 'secureStorePreflightArtifactTouchedAdminRoot'
    }
    $artifactChecks = 5
}

[pscustomobject]@{
    result = 'passed'
    checks = 3 + $artifactChecks
    executableBuilt = $false
    artifactGatesRun = [bool]$RunArtifactGates
    processStartCount = $script:ProcessStartCount
    systemMutation = $false
} | ConvertTo-Json -Compress
