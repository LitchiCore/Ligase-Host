[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RepositoryRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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
        'if (args.Length != 0)',
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
        'ResultCode = evidenceWriteInProgress',
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
    '#endif',
    $preflightStart,
    [StringComparison]::Ordinal)
if ($preflightStart -lt 0 -or $preflightEnd -le $preflightStart) {
    throw 'secureStorePreflightMainBoundaryInvalid'
}
$preflight = $program.Substring(
    $preflightStart, $preflightEnd - $preflightStart)
foreach ($forbidden in @(
        'Process.Start',
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

[pscustomobject]@{
    result = 'passed'
    checks = 3
    executableBuilt = $false
    systemMutation = $false
} | ConvertTo-Json -Compress
