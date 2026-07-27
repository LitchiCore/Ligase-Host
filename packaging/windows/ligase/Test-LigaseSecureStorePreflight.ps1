[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RepositoryRoot,

    [string] $ProductionArtifact,

    [string] $ValidationArtifact,

    [string] $FixtureRoot,

    [switch] $RunArtifactGates,

    [switch] $RunEvidenceSelfTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script:ProcessStartCount = 0
$script:GateEvidenceRoot = $null

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
public static class LigaseArtifactStrictJson {
  public static bool IsUniqueAndComplete(string s) {
    try { var p=new P(s); p.V(); p.W(); return p.End; } catch { return false; }
  }
  sealed class P {
    readonly string s; int i;
    internal P(string value) { if(value==null)throw new FormatException();s=value; }
    internal bool End { get { return i==s.Length; } }
    internal void W(){while(i<s.Length&&(s[i]==' '||s[i]=='\t'||s[i]=='\r'||s[i]=='\n'))i++;}
    internal void V(){W();if(i>=s.Length)throw new FormatException();
      switch(s[i]){case '{':O();return;case '[':A();return;case '"':S();return;
      case 't':L("true");return;case 'f':L("false");return;case 'n':L("null");return;
      default:N();return;}}
    void O(){i++;W();var n=new HashSet<string>(StringComparer.Ordinal);
      if(C('}'))return;while(true){W();var k=S();if(!n.Add(k))throw new FormatException();
      W();R(':');V();W();if(C('}'))return;R(',');}}
    void A(){i++;W();if(C(']'))return;while(true){V();W();if(C(']'))return;R(',');}}
    string S(){R('"');var b=new StringBuilder();while(i<s.Length){var c=s[i++];
      if(c=='"')return b.ToString();if(c<0x20)throw new FormatException();
      if(c!='\\'){b.Append(c);continue;}if(i>=s.Length)throw new FormatException();
      c=s[i++];switch(c){case '"':b.Append('"');break;case '\\':b.Append('\\');break;
      case '/':b.Append('/');break;case 'b':b.Append('\b');break;case 'f':b.Append('\f');break;
      case 'n':b.Append('\n');break;case 'r':b.Append('\r');break;case 't':b.Append('\t');break;
      case 'u':if(i+4>s.Length)throw new FormatException();b.Append((char)Int32.Parse(
      s.Substring(i,4),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture));i+=4;break;
      default:throw new FormatException();}}throw new FormatException();}
    void N(){var a=i;if(C('-')){}if(C('0')){if(i<s.Length&&Char.IsDigit(s[i]))throw new FormatException();}
      else{if(i>=s.Length||s[i]<'1'||s[i]>'9')throw new FormatException();
      while(i<s.Length&&Char.IsDigit(s[i]))i++;}if(C('.')){var q=i;while(i<s.Length&&Char.IsDigit(s[i]))i++;
      if(i==q)throw new FormatException();}if(i<s.Length&&(s[i]=='e'||s[i]=='E')){i++;
      if(i<s.Length&&(s[i]=='+'||s[i]=='-'))i++;var q=i;while(i<s.Length&&Char.IsDigit(s[i]))i++;
      if(i==q)throw new FormatException();}if(i==a)throw new FormatException();}
    void L(string x){if(i+x.Length>s.Length||String.CompareOrdinal(s,i,x,0,x.Length)!=0)
      throw new FormatException();i+=x.Length;}
    bool C(char c){if(i>=s.Length||s[i]!=c)return false;i++;return true;}
    void R(char c){if(!C(c))throw new FormatException();}
  }
}
"@

function Get-TextSha256 {
    param([AllowEmptyString()][string] $Value)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $sha.ComputeHash(
                [Text.Encoding]::UTF8.GetBytes($Value)))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Get-BytesSha256 {
    param([Parameter(Mandatory = $true)][byte[]] $Value)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $sha.ComputeHash($Value))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Get-ArtifactPropertyNameSetHash {
    param([Parameter(Mandatory = $true)][object] $Value)

    $names = [string[]]@(
        $Value.PSObject.Properties | ForEach-Object { [string]$_.Name })
    [Array]::Sort($names, [StringComparer]::Ordinal)
    return Get-TextSha256 ([string]::Join("`n", $names))
}

function Test-FixedTimeSha256 {
    param(
        [Parameter(Mandatory = $true)][string] $Left,
        [Parameter(Mandatory = $true)][string] $Right
    )

    if ($Left.Length -ne 64 -or $Right.Length -ne 64) { return $false }
    $difference = 0
    for ($index = 0; $index -lt 64; $index++) {
        $difference = $difference -bor (
            [int][char]$Left[$index] -bxor [int][char]$Right[$index])
    }
    return $difference -eq 0
}

function New-ArtifactGateObservation {
    param(
        [Parameter(Mandatory = $true)][string] $GateId,
        [Parameter(Mandatory = $true)]
        [ValidateSet('production', 'validation', 'harness')]
        [string] $ArtifactKind,
        [object] $Invocation,
        [object] $Projection,
        [bool] $Passed,
        [string] $CleanupState = 'notRequired',
        [bool] $SentinelExists = $false,
        [ValidateSet('closed', 'invalid', 'notParsed')]
        [string] $ParseState = 'notParsed',
        [string] $SchemaId = 'none',
        [string] $ParseReason = 'none',
        [object] $ProjectionMetadata
    )

    $property = {
        param([object] $Object, [string] $Name, [object] $Default)
        if ($null -eq $Object) { return $Default }
        $candidate = $Object.PSObject.Properties[$Name]
        if ($null -eq $candidate) { return $Default }
        return $candidate.Value
    }
    $stdout = [string](& $property $Invocation 'stdout' '')
    $stderr = [string](& $property $Invocation 'stderr' '')
    $nativeExit = [int](& $property $Invocation 'exitCode' -1)
    $elapsed = [int64](& $property $Invocation 'elapsedMilliseconds' 0)
    $timedOut = [bool](& $property $Invocation 'timedOut' $false)
    $policyActive = [bool](& $property $Projection 'policyActive' $false)
    $childBlocked = [bool](
        & $property $Projection 'childCreationBlocked' $false)
    $childCreated = [bool](
        & $property $Projection 'childProcessCreated' $false)
    $handlesZero = [bool](
        & $property $Projection 'processHandlesZero' $false)
    $childPid = [int](& $property $Projection 'childPid' 0)
    $nativeCode = [int](& $property $Projection 'nativeCode' 0)
    $childCleanup = [string](
        & $property $Projection 'childCleanup' 'notEvaluated')
    $observedPropertyCount = [int](
        & $property $ProjectionMetadata 'observedPropertyCount' 0)
    $propertyNameSetHash = [string](
        & $property $ProjectionMetadata 'propertyNameSetHash' (
            Get-TextSha256 ''))
    $declaredSchemaId = [string](
        & $property $ProjectionMetadata 'declaredSchemaId' 'none')
    $observedResultCode = [string](
        & $property $ProjectionMetadata 'observedResultCode' 'none')
    $observedStage = [string](
        & $property $ProjectionMetadata 'observedStage' 'none')
    $observedNativeCode = [int](
        & $property $ProjectionMetadata 'observedNativeCode' 0)

    return [ordered]@{
        schemaVersion = 1
        gateId = $GateId
        artifactKind = $ArtifactKind
        schemaId = $SchemaId
        passed = $Passed
        nativeExit = $nativeExit
        policyActive = $policyActive
        policySet = if ($policyActive) { 'active' } else { 'notActive' }
        policyReadback = if ($policyActive) { 'exact' } else { 'notExact' }
        createProcessReturned = $childCreated
        createProcessWin32Code = $nativeCode
        processHandleZero = $handlesZero
        threadHandleZero = $handlesZero
        childPid = $childPid
        terminateState = $childCleanup
        waitState = $childCleanup
        closeState = $childCleanup
        cleanupState = $CleanupState
        sentinelExists = $SentinelExists
        stdoutLength = [Text.Encoding]::UTF8.GetByteCount($stdout)
        stderrLength = [Text.Encoding]::UTF8.GetByteCount($stderr)
        stdoutSha256 = Get-TextSha256 $stdout
        stderrSha256 = Get-TextSha256 $stderr
        stdoutClosed = -not $timedOut
        stderrClosed = -not $timedOut
        parseState = $ParseState
        parseReason = $ParseReason
        observedPropertyCount = $observedPropertyCount
        propertyNameSetHash = $propertyNameSetHash
        declaredSchemaId = $declaredSchemaId
        observedResultCode = $observedResultCode
        observedStage = $observedStage
        observedNativeCode = $observedNativeCode
        elapsedMilliseconds = $elapsed
        timedOut = $timedOut
    }
}

function Get-ArtifactGateObservationFieldNames {
    return @(
        'schemaVersion', 'gateId', 'artifactKind', 'schemaId', 'passed',
        'nativeExit', 'policyActive', 'policySet', 'policyReadback',
        'createProcessReturned', 'createProcessWin32Code',
        'processHandleZero', 'threadHandleZero', 'childPid',
        'terminateState', 'waitState', 'closeState', 'cleanupState',
        'sentinelExists', 'stdoutLength', 'stderrLength',
        'stdoutSha256', 'stderrSha256',
        'stdoutClosed', 'stderrClosed', 'parseState', 'parseReason',
        'observedPropertyCount', 'propertyNameSetHash',
        'declaredSchemaId', 'observedResultCode', 'observedStage',
        'observedNativeCode',
        'elapsedMilliseconds', 'timedOut')
}

function Test-ClosedArtifactGateObservation {
    param([Parameter(Mandatory = $true)][object] $Observation)

    $expectedNames = Get-ArtifactGateObservationFieldNames
    $properties = @($Observation.PSObject.Properties)
    if ($properties.Count -ne $expectedNames.Count -or
        [string]::Join(
            "`n", [string[]]@($properties | ForEach-Object Name)) -ne
        [string]::Join("`n", $expectedNames)) {
        return $false
    }
    $integerFields = @(
        'schemaVersion', 'nativeExit', 'createProcessWin32Code',
        'childPid', 'stdoutLength', 'stderrLength', 'elapsedMilliseconds')
    $integerFields += @('observedPropertyCount', 'observedNativeCode')
    $booleanFields = @(
        'passed', 'policyActive', 'createProcessReturned',
        'processHandleZero', 'threadHandleZero', 'sentinelExists',
        'stdoutClosed', 'stderrClosed', 'timedOut')
    foreach ($property in $properties) {
        $valid = if ($property.Name -in $integerFields) {
            $property.Value -is [int] -or $property.Value -is [long]
        }
        elseif ($property.Name -in $booleanFields) {
            $property.Value -is [bool]
        }
        else {
            $property.Value -is [string]
        }
        if (-not $valid) { return $false }
    }
    if ($Observation.schemaVersion -ne 1 -or
        $Observation.gateId -notmatch '\A[a-z0-9-]+\z' -or
        $Observation.artifactKind -notin @(
            'production', 'validation', 'harness') -or
        $Observation.stdoutLength -lt 0 -or
        $Observation.stderrLength -lt 0 -or
        $Observation.elapsedMilliseconds -lt 0 -or
        $Observation.stdoutSha256 -notmatch '\A[0-9A-F]{64}\z' -or
        $Observation.stderrSha256 -notmatch '\A[0-9A-F]{64}\z') {
        return $false
    }
    if ($Observation.observedPropertyCount -lt 0 -or
        $Observation.observedNativeCode -lt 0 -or
        $Observation.observedNativeCode -gt 65535 -or
        $Observation.propertyNameSetHash -notmatch '\A[0-9A-F]{64}\z') {
        return $false
    }
    $knownSchemas = @(
        'productionInvalidArgumentsV1',
        'productionPolicySetFailureV1',
        'productionPolicyReadbackFailureV1',
        'validationArgvV1',
        'validationChildPolicyV1',
        'validationChildFailureV1',
        'validationChildCleanupV1')
    return (
        ($Observation.parseState -eq 'closed' -and
            $Observation.schemaId -in $knownSchemas -and
            $Observation.parseReason -eq 'none') -or
        ($Observation.parseState -eq 'invalid' -and
            $Observation.schemaId -in @($knownSchemas + 'none') -and
            $Observation.parseReason -ne 'none') -or
        ($Observation.parseState -eq 'notParsed' -and
            $Observation.schemaId -eq 'none' -and
            $Observation.parseReason -eq 'none'))
}

function Write-ArtifactGateObservation {
    param(
        [Parameter(Mandatory = $true)][Collections.IDictionary] $Observation,
        [switch] $Cleanup
    )

    try {
        if ([string]::IsNullOrWhiteSpace($script:GateEvidenceRoot)) {
            throw 'gateEvidenceRootUnavailable'
        }
        $expectedNames = Get-ArtifactGateObservationFieldNames
        if ($Observation.Count -ne $expectedNames.Count -or
            [string]::Join(
                "`n", [string[]]@(
                    $Observation.Keys | ForEach-Object { [string]$_ })) -ne
            [string]::Join("`n", $expectedNames)) {
            throw 'gateEvidenceShapeInvalid'
        }
        $candidateObject = [pscustomobject]$Observation
        if (-not (Test-ClosedArtifactGateObservation $candidateObject)) {
            throw 'gateEvidenceProvenanceInvalid'
        }
        $json = $Observation | ConvertTo-Json -Compress -Depth 3
        $parsed = $json | ConvertFrom-Json
        if (@($parsed.PSObject.Properties).Count -ne $expectedNames.Count) {
            throw 'gateEvidenceRoundtripInvalid'
        }
        foreach ($name in $expectedNames) {
            $escaped = [Regex]::Escape('"' + $name + '"')
            if ([Regex]::Matches($json, $escaped).Count -ne 1) {
                throw 'gateEvidenceDuplicateProperty'
            }
        }
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
        $suffix = if ($Cleanup) { '.cleanup' } else { '.first' }
        $fileName = [string]$Observation.gateId + $suffix + '.json'
        $destination = Join-Path $script:GateEvidenceRoot $fileName
        if (Test-Path -LiteralPath $destination) {
            throw 'gateEvidenceAlreadyExists'
        }
        $temp = Join-Path $script:GateEvidenceRoot (
            '.' + $fileName + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
        try {
            [IO.File]::WriteAllBytes($temp, $bytes)
            [IO.File]::Move($temp, $destination)
        }
        finally {
            if (Test-Path -LiteralPath $temp) {
                Remove-Item -LiteralPath $temp -Force
            }
        }
        $readback = [IO.File]::ReadAllBytes($destination)
        if ([Convert]::ToBase64String($readback) -ne
            [Convert]::ToBase64String($bytes)) {
            throw 'gateEvidenceReadbackInvalid'
        }
        return Get-TextSha256 ([Text.Encoding]::UTF8.GetString($readback))
    }
    catch {
        throw 'gateEvidenceUnavailable'
    }
}

function Complete-ArtifactGate {
    param(
        [Parameter(Mandatory = $true)][Collections.IDictionary] $Observation,
        [Parameter(Mandatory = $true)][string] $FailureCode
    )

    $sha = Write-ArtifactGateObservation $Observation
    if (-not [bool]$Observation.passed) {
        throw (
            $FailureCode + ' gateId=' + [string]$Observation.gateId +
            ' evidenceSha256=' + $sha)
    }
    return $sha
}

function Write-ArtifactGateCleanupObservation {
    param(
        [Parameter(Mandatory = $true)]
        [Collections.IDictionary] $Observation,
        [Parameter(Mandatory = $true)]
        [string] $ExpectedFirstSha256
    )

    try {
        $firstPath = Join-Path $script:GateEvidenceRoot (
            [string]$Observation.gateId + '.first.json')
        if (-not (Test-Path -LiteralPath $firstPath -PathType Leaf)) {
            throw 'firstEvidenceMissing'
        }
        $firstBytes = [IO.File]::ReadAllBytes($firstPath)
        $firstRaw = [Text.UTF8Encoding]::new(
            $false, $true).GetString($firstBytes)
        if (-not [LigaseArtifactStrictJson]::IsUniqueAndComplete($firstRaw)) {
            throw 'firstEvidenceJsonInvalid'
        }
        $first = $firstRaw | ConvertFrom-Json
        if (-not (Test-ClosedArtifactGateObservation $first)) {
            throw 'firstEvidenceShapeInvalid'
        }
        $actualFirstSha256 = Get-BytesSha256 $firstBytes
        if (-not (Test-FixedTimeSha256 `
                $actualFirstSha256 $ExpectedFirstSha256)) {
            throw 'firstEvidenceShaMismatch'
        }
        foreach ($name in @(
                'schemaId', 'parseState', 'parseReason',
                'observedPropertyCount', 'propertyNameSetHash',
                'declaredSchemaId', 'observedResultCode',
                'observedStage', 'observedNativeCode',
                'stdoutLength', 'stderrLength',
                'stdoutSha256', 'stderrSha256')) {
            if ($first.$name -ne $Observation[$name]) {
                throw 'cleanupProvenanceMismatch'
            }
        }
        return Write-ArtifactGateObservation $Observation -Cleanup
    }
    catch {
        throw 'gateEvidenceUnavailable'
    }
}

function ConvertTo-ClosedArtifactProjection {
    param(
        [AllowEmptyString()][string] $Text,
        [Parameter(Mandatory = $true)]
        [ValidateSet(
            'productionInvalidArgumentsV1',
            'productionPolicySetFailureV1',
            'productionPolicyReadbackFailureV1',
            'validationArgvV1',
            'validationChildPolicyV1',
            'validationChildFailureV1',
            'validationChildCleanupV1')]
        [string] $SchemaId
    )

    if ([string]::IsNullOrWhiteSpace($Text) -or $Text.Length -gt 4096) {
        return [pscustomobject]@{
            state = 'invalid'; reason = 'size'; value = $null
        }
    }
    if (-not [LigaseArtifactStrictJson]::IsUniqueAndComplete($Text)) {
        return [pscustomobject]@{
            state = 'invalid'; reason = 'syntaxOrDuplicate'; value = $null
        }
    }
    try {
        $value = $Text | ConvertFrom-Json
        if ($null -eq $value -or
            $value -is [Array] -or
            $value -isnot [pscustomobject]) {
            return [pscustomobject]@{
                state = 'invalid'; reason = 'rootType'; value = $null
            }
        }
        $productionShape = [ordered]@{
            schemaVersion = 'integer'; releaseKind = 'string'
            trustBoundary = 'string'; success = 'boolean'
            resultCode = 'string'; stage = 'string'
            nativeCategory = 'string'; nativeCode = 'integer'
            acl = 'string'; recovery = 'string'; probe = 'string'
            cleanup = 'string'; aclMutationOccurred = 'boolean'
            aclRollback = 'string'
        }
        $schemas = @{
            productionInvalidArgumentsV1 = $productionShape
            productionPolicySetFailureV1 = $productionShape
            productionPolicyReadbackFailureV1 = $productionShape
            validationArgvV1 = [ordered]@{
                result = 'string'; stage = 'string'; policyActive = 'boolean'
                argumentCount = 'integer'; token = 'string'
            }
            validationChildPolicyV1 = [ordered]@{
                schemaId = 'string'; result = 'string'; stage = 'string'
                policyActive = 'boolean'
                argumentCount = 'integer'; token = 'string'
                childCreationBlocked = 'boolean'
                childProcessCreated = 'boolean'
                processHandlesZero = 'boolean'; nativeCode = 'integer'
                sentinelExists = 'boolean'
            }
            validationChildFailureV1 = [ordered]@{
                schemaId = 'string'; result = 'string'; resultCode = 'string'
                stage = 'string'; nativeCategory = 'string'
                nativeCode = 'integer'
            }
            validationChildCleanupV1 = [ordered]@{
                schemaId = 'string'; result = 'string'; stage = 'string'
                childPid = 'integer'
                childCleanup = 'string'
            }
        }
        $schema = $schemas[$SchemaId]
        $properties = @($value.PSObject.Properties)
        if ($properties.Count -ne $schema.Count) {
            return [pscustomobject]@{
                state = 'invalid'; reason = 'propertySet'; value = $null
            }
        }
        foreach ($property in $properties) {
            if (-not $schema.Contains([string]$property.Name)) {
                return [pscustomobject]@{
                    state = 'invalid'; reason = 'propertySet'; value = $null
                }
            }
        }
        foreach ($name in $schema.Keys) {
            $candidate = $value.PSObject.Properties[[string]$name]
            if ($null -eq $candidate) {
                return [pscustomobject]@{
                    state = 'invalid'; reason = 'propertySet'; value = $null
                }
            }
            $actual = $candidate.Value
            $kind = [string]$schema[$name]
            $valid = switch ($kind) {
                'string' { $actual -is [string] }
                'boolean' { $actual -is [bool] }
                'integer' {
                    ($actual -is [int] -or $actual -is [long]) -and
                    [long]$actual -ge 0 -and [long]$actual -le 65535
                }
                default { $false }
            }
            if (-not $valid) {
                return [pscustomobject]@{
                    state = 'invalid'; reason = 'propertyType'; value = $null
                }
            }
        }
        $closedValue = switch ($SchemaId) {
            { $_ -in @(
                    'productionInvalidArgumentsV1',
                    'productionPolicySetFailureV1',
                    'productionPolicyReadbackFailureV1') } {
                $tupleValid = switch ($SchemaId) {
                    'productionInvalidArgumentsV1' {
                        $value.resultCode -eq 'invalidArguments' -and
                        $value.stage -eq 'inputValidation' -and
                        $value.nativeCategory -eq 'none' -and
                        $value.nativeCode -eq 0
                    }
                    'productionPolicySetFailureV1' {
                        $value.resultCode -eq 'secureStorePreflightFailed' -and
                        $value.stage -eq 'securityInitialization' -and
                        $value.nativeCategory -eq 'accessDenied' -and
                        $value.nativeCode -eq 5
                    }
                    'productionPolicyReadbackFailureV1' {
                        $value.resultCode -eq 'secureStorePreflightFailed' -and
                        $value.stage -eq 'securityInitialization' -and
                        $value.nativeCategory -eq 'managedFailure' -and
                        $value.nativeCode -eq 20013
                    }
                }
                $value.schemaVersion -eq 1 -and
                $value.releaseKind -eq 'UnsignedDev' -and
                $value.trustBoundary -eq 'localManualExactSha' -and
                $value.success -eq $false -and
                $value.acl -eq 'notAttempted' -and
                $value.recovery -eq 'notAttempted' -and
                $value.probe -eq 'notAttempted' -and
                $value.cleanup -eq 'notAttempted' -and
                $value.aclMutationOccurred -eq $false -and
                $value.aclRollback -eq 'notRequired' -and
                $tupleValid
            }
            'validationArgvV1' {
                $value.result -eq 'passed' -and
                $value.stage -eq 'inputValidation' -and
                $value.policyActive -eq $true -and
                $value.argumentCount -eq 1 -and
                $value.token -eq '--validate-argv-token'
            }
            'validationChildPolicyV1' {
                $value.schemaId -eq 'validationChildPolicyV1' -and
                $value.result -eq 'passed' -and
                $value.stage -eq 'childPolicy' -and
                $value.policyActive -eq $true -and
                $value.argumentCount -eq 1 -and
                $value.token -eq '--validate-child-policy' -and
                $value.nativeCode -eq 367
            }
            'validationChildCleanupV1' {
                $value.schemaId -eq 'validationChildCleanupV1' -and
                $value.result -eq 'failed' -and
                $value.stage -eq 'childCleanup' -and
                (@('completed', 'failed') -contains
                    [string]$value.childCleanup)
            }
            'validationChildFailureV1' {
                $knownNativeCategories = @(
                    'none', 'accessDenied', 'privilegeNotHeld',
                    'invalidOwner', 'invalidAcl', 'notSupported',
                    'identityChanged', 'unknown', 'managedFailure')
                $value.schemaId -eq 'validationChildFailureV1' -and
                $value.result -eq 'failed' -and
                $value.resultCode -eq 'secureStorePreflightFailed' -and
                $value.stage -in @(
                    'securityInitialization', 'childPolicy') -and
                $value.nativeCategory -in $knownNativeCategories -and
                (($value.nativeCategory -eq 'none' -and
                        $value.nativeCode -eq 0) -or
                    ($value.nativeCategory -ne 'none' -and
                        $value.nativeCode -gt 0))
            }
            default { $false }
        }
        if (-not $closedValue) {
            return [pscustomobject]@{
                state = 'invalid'; reason = 'propertyValue'; value = $null
            }
        }
        return [pscustomobject]@{
            state = 'closed'; reason = 'none'; value = $value
        }
    }
    catch {
        return [pscustomobject]@{
            state = 'invalid'; reason = 'projection'; value = $null
        }
    }
}

function Get-ClosedChildArtifactDiscriminator {
    param([AllowEmptyString()][string] $Text)

    $emptyHash = Get-TextSha256 ''
    $invalid = {
        param([string] $Reason)
        return [pscustomobject]@{
            state = 'invalid'; reason = $Reason; value = $null
            observedPropertyCount = 0
            propertyNameSetHash = $emptyHash
            declaredSchemaId = 'none'
            observedResultCode = 'none'
            observedStage = 'none'
            observedNativeCode = 0
        }
    }
    if ([string]::IsNullOrWhiteSpace($Text) -or $Text.Length -gt 4096) {
        return & $invalid 'discriminatorSize'
    }
    if (-not [LigaseArtifactStrictJson]::IsUniqueAndComplete($Text)) {
        return & $invalid 'discriminatorSyntaxOrDuplicate'
    }
    try {
        $value = $Text | ConvertFrom-Json
        if ($null -eq $value -or $value -is [Array] -or
            $value -isnot [pscustomobject]) {
            return & $invalid 'discriminatorRootType'
        }
        $properties = @($value.PSObject.Properties)
        $metadata = [ordered]@{
            observedPropertyCount = $properties.Count
            propertyNameSetHash = Get-ArtifactPropertyNameSetHash $value
            declaredSchemaId = 'none'
            observedResultCode = 'none'
            observedStage = 'none'
            observedNativeCode = 0
        }
        $schema = $value.PSObject.Properties['schemaId']
        if ($null -eq $schema -or $schema.Value -isnot [string] -or
            $schema.Value -notin @(
                'validationChildPolicyV1',
                'validationChildFailureV1',
                'validationChildCleanupV1')) {
            return [pscustomobject](@{
                state = 'invalid'; reason = 'discriminatorValue'
                value = $null
            } + $metadata)
        }
        $metadata.declaredSchemaId = [string]$schema.Value
        $resultCode = $value.PSObject.Properties['resultCode']
        if ($null -ne $resultCode -and $resultCode.Value -is [string]) {
            $metadata.observedResultCode = [string]$resultCode.Value
        }
        $stage = $value.PSObject.Properties['stage']
        if ($null -ne $stage -and $stage.Value -is [string]) {
            $metadata.observedStage = [string]$stage.Value
        }
        $nativeCode = $value.PSObject.Properties['nativeCode']
        if ($null -ne $nativeCode -and
            ($nativeCode.Value -is [int] -or
                $nativeCode.Value -is [long]) -and
            [long]$nativeCode.Value -ge 0 -and
            [long]$nativeCode.Value -le 65535) {
            $metadata.observedNativeCode = [int]$nativeCode.Value
        }
        return [pscustomobject](@{
            state = 'closed'; reason = 'none'; value = $value
        } + $metadata)
    }
    catch {
        return & $invalid 'discriminatorProjection'
    }
}

function ConvertTo-DiscriminatedChildProjection {
    param([Parameter(Mandatory = $true)][object] $Invocation)

    $stdout = [string]$Invocation.stdout
    $stderr = [string]$Invocation.stderr
    if (($stdout.Length -eq 0) -eq ($stderr.Length -eq 0)) {
        return Get-ClosedChildArtifactDiscriminator ''
    }
    $raw = if ($stdout.Length -ne 0) { $stdout } else { $stderr }
    $discriminator = Get-ClosedChildArtifactDiscriminator $raw
    if ($discriminator.state -ne 'closed') {
        return $discriminator
    }
    $full = ConvertTo-ClosedArtifactProjection `
        $raw -SchemaId $discriminator.declaredSchemaId
    return [pscustomobject]@{
        state = $full.state
        reason = $full.reason
        value = $full.value
        observedPropertyCount = $discriminator.observedPropertyCount
        propertyNameSetHash = $discriminator.propertyNameSetHash
        declaredSchemaId = $discriminator.declaredSchemaId
        observedResultCode = $discriminator.observedResultCode
        observedStage = $discriminator.observedStage
        observedNativeCode = $discriminator.observedNativeCode
    }
}

function Close-ArtifactProjectionSemantics {
    param(
        [Parameter(Mandatory = $true)][object] $Projection,
        [Parameter(Mandatory = $true)][bool] $NativeTupleValid
    )

    if ($Projection.state -eq 'closed' -and -not $NativeTupleValid) {
        $metadata = {
            param([string] $Name, [object] $Default)
            $property = $Projection.PSObject.Properties[$Name]
            if ($null -eq $property) { return $Default }
            return $property.Value
        }
        return [pscustomobject]@{
            state = 'invalid'
            reason = 'semanticTupleMismatch'
            value = $null
            observedPropertyCount = [int](
                & $metadata 'observedPropertyCount' 0)
            propertyNameSetHash = [string](
                & $metadata 'propertyNameSetHash' (Get-TextSha256 ''))
            declaredSchemaId = [string](
                & $metadata 'declaredSchemaId' 'none')
            observedResultCode = [string](
                & $metadata 'observedResultCode' 'none')
            observedStage = [string](
                & $metadata 'observedStage' 'none')
            observedNativeCode = [int](
                & $metadata 'observedNativeCode' 0)
        }
    }
    return $Projection
}

function Test-ArtifactNativeTuple {
    param(
        [Parameter(Mandatory = $true)][string] $SchemaId,
        [Parameter(Mandatory = $true)][object] $Invocation
    )

    switch ($SchemaId) {
        'validationChildPolicyV1' {
            return $Invocation.exitCode -eq 0 -and
                $Invocation.stderr.Length -eq 0
        }
        'validationChildCleanupV1' {
            return $Invocation.exitCode -eq 18 -and
                $Invocation.stdout.Length -eq 0 -and
                $Invocation.stderr.Length -le 4096
        }
        'validationChildFailureV1' {
            return $Invocation.exitCode -eq 18 -and
                $Invocation.stdout.Length -eq 0 -and
                $Invocation.stderr.Length -le 4096
        }
        default { throw 'gateEvidenceNativeTupleSchemaInvalid' }
    }
}

function Invoke-GateEvidenceSelfTests {
    param([Parameter(Mandatory = $true)][string] $Root)

    if (Test-Path -LiteralPath $Root) {
        throw 'gateEvidenceSelfTestRootPreexisting'
    }
    New-Item -ItemType Directory -Path $Root -Force | Out-Null
    $savedRoot = $script:GateEvidenceRoot
    try {
        $script:GateEvidenceRoot = $Root
        $continuedAfterFailure = $false
        $failurePattern =
            '\AselfTestExpectedFailure gateId=self-test-failure-flow ' +
            'evidenceSha256=[0-9A-F]{64}\z'
        try {
            Complete-ArtifactGate (
                New-ArtifactGateObservation `
                    -GateId 'self-test-failure-flow' `
                    -ArtifactKind 'harness' `
                    -Invocation ([pscustomobject]@{
                        exitCode = 18; stdout = ''; stderr = ''
                        timedOut = $false; elapsedMilliseconds = 1
                    }) `
                    -Projection $null -Passed $false) `
                -FailureCode 'selfTestExpectedFailure' | Out-Null
            $continuedAfterFailure = $true
        }
        catch {
            if ($_.Exception.Message -notmatch $failurePattern) {
                throw
            }
        }
        if ($continuedAfterFailure) {
            throw 'gateEvidenceFailureFlowContinued'
        }
        foreach ($case in @(
                @{ id = 'policy'; policy = $false; handles = $true;
                    pid = 0; sentinel = $false; cleanup = 'completed' },
                @{ id = 'handles'; policy = $true; handles = $false;
                    pid = 0; sentinel = $false; cleanup = 'completed' },
                @{ id = 'pid'; policy = $true; handles = $true;
                    pid = 41; sentinel = $false; cleanup = 'required' },
                @{ id = 'sentinel'; policy = $true; handles = $true;
                    pid = 0; sentinel = $true; cleanup = 'completed' },
                @{ id = 'cleanup'; policy = $true; handles = $true;
                    pid = 0; sentinel = $false; cleanup = 'failed' })) {
            $id = 'self-test-child-' + [string]$case.id
            $observation = New-ArtifactGateObservation `
                -GateId $id -ArtifactKind 'harness' `
                -Invocation ([pscustomobject]@{
                    exitCode = 18; stdout = ''; stderr = ''
                    timedOut = $false; elapsedMilliseconds = 1
                }) `
                -Projection ([pscustomobject]@{
                    policyActive = [bool]$case.policy
                    childCreationBlocked = $false
                    childProcessCreated = ([int]$case.pid -ne 0)
                    processHandlesZero = [bool]$case.handles
                    childPid = [int]$case.pid
                    nativeCode = 5
                    childCleanup = [string]$case.cleanup
                }) `
                -Passed $false `
                -CleanupState ([string]$case.cleanup) `
                -SentinelExists ([bool]$case.sentinel)
            $sha = Write-ArtifactGateObservation $observation
            $path = Join-Path $Root ($id + '.first.json')
            $raw = Get-Content -LiteralPath $path -Raw
            $closed = $raw | ConvertFrom-Json
            if ($closed.gateId -ne $id -or $closed.passed -ne $false -or
                $sha -ne (Get-TextSha256 $raw)) {
                throw 'gateEvidenceSelfTestReadbackInvalid'
            }
        }
        $productionBase =
            '{"schemaVersion":1,"releaseKind":"UnsignedDev",' +
            '"trustBoundary":"localManualExactSha","success":false,' +
            '"resultCode":"invalidArguments","stage":"inputValidation",' +
            '"nativeCategory":"none","nativeCode":0,"acl":"notAttempted",' +
            '"recovery":"notAttempted","probe":"notAttempted",' +
            '"cleanup":"notAttempted","aclMutationOccurred":false,' +
            '"aclRollback":"notRequired"}'
        $policySetBase = $productionBase.
            Replace('"resultCode":"invalidArguments"',
                '"resultCode":"secureStorePreflightFailed"').
            Replace('"stage":"inputValidation"',
                '"stage":"securityInitialization"').
            Replace('"nativeCategory":"none"',
                '"nativeCategory":"accessDenied"').
            Replace('"nativeCode":0', '"nativeCode":5')
        $policyReadbackBase = $policySetBase.
            Replace('"nativeCategory":"accessDenied"',
                '"nativeCategory":"managedFailure"').
            Replace('"nativeCode":5', '"nativeCode":20013')
        $argvBase =
            '{"result":"passed","stage":"inputValidation",' +
            '"policyActive":true,"argumentCount":1,' +
            '"token":"--validate-argv-token"}'
        $childBase =
            '{"schemaId":"validationChildPolicyV1",' +
            '"result":"passed","stage":"childPolicy",' +
            '"policyActive":true,"argumentCount":1,' +
            '"token":"--validate-child-policy",' +
            '"childCreationBlocked":true,"childProcessCreated":false,' +
            '"processHandlesZero":true,"nativeCode":367,' +
            '"sentinelExists":false}'
        $cleanupBase =
            '{"schemaId":"validationChildCleanupV1",' +
            '"result":"failed","stage":"childCleanup",' +
            '"childPid":0,"childCleanup":"failed"}'
        $childFailureBase =
            '{"schemaId":"validationChildFailureV1",' +
            '"result":"failed",' +
            '"resultCode":"secureStorePreflightFailed",' +
            '"stage":"childPolicy","nativeCategory":"accessDenied",' +
            '"nativeCode":5}'
        foreach ($validCase in @(
                @{ schema = 'productionInvalidArgumentsV1';
                    raw = $productionBase },
                @{ schema = 'productionPolicySetFailureV1';
                    raw = $policySetBase },
                @{ schema = 'productionPolicyReadbackFailureV1';
                    raw = $policyReadbackBase },
                @{ schema = 'validationArgvV1'; raw = $argvBase },
                @{ schema = 'validationChildPolicyV1'; raw = $childBase },
                @{ schema = 'validationChildFailureV1';
                    raw = $childFailureBase },
                @{ schema = 'validationChildCleanupV1'; raw = $cleanupBase })) {
            $validProjection = ConvertTo-ClosedArtifactProjection `
                ([string]$validCase.raw) -SchemaId ([string]$validCase.schema)
            if ($validProjection.state -ne 'closed' -or
                $null -eq $validProjection.value) {
                throw 'gateEvidenceValidProjectionRejected'
            }
        }
        foreach ($childCase in @(
                @{ id = 'policy'; raw = $childBase; exit = 0;
                    stdout = $true; schema = 'validationChildPolicyV1' },
                @{ id = 'failure'; raw = $childFailureBase; exit = 18;
                    stdout = $false; schema = 'validationChildFailureV1' },
                @{ id = 'cleanup'; raw = $cleanupBase; exit = 18;
                    stdout = $false; schema = 'validationChildCleanupV1' })) {
            $invocation = [pscustomobject]@{
                exitCode = [int]$childCase.exit
                stdout = if ([bool]$childCase.stdout) {
                    [string]$childCase.raw
                } else { '' }
                stderr = if ([bool]$childCase.stdout) {
                    ''
                } else { [string]$childCase.raw }
                timedOut = $false
                elapsedMilliseconds = 1
            }
            $projection = ConvertTo-DiscriminatedChildProjection $invocation
            if ($projection.state -ne 'closed' -or
                $projection.declaredSchemaId -ne
                    [string]$childCase.schema -or
                $projection.observedPropertyCount -le 0 -or
                $projection.propertyNameSetHash -notmatch
                    '\A[0-9A-F]{64}\z' -or
                $projection.observedStage -eq 'none' -or
                -not (Test-ArtifactNativeTuple `
                    ([string]$childCase.schema) $invocation)) {
                throw 'gateEvidenceChildDiscriminatorRejected'
            }
            if ([string]$childCase.schema -eq
                    'validationChildFailureV1' -and
                ($projection.observedResultCode -ne
                        'secureStorePreflightFailed' -or
                    $projection.observedNativeCode -ne 5)) {
                throw 'gateEvidenceChildFailureMetadataInvalid'
            }
        }
        foreach ($discriminatorCase in @(
                @{ id = 'missing'; raw = $childBase.Replace(
                    '"schemaId":"validationChildPolicyV1",', '') },
                @{ id = 'duplicate-same'; raw = $childBase.Replace(
                    '"schemaId":"validationChildPolicyV1"',
                    '"schemaId":"validationChildPolicyV1",' +
                    '"schemaId":"validationChildPolicyV1"') },
                @{ id = 'duplicate-conflict'; raw = $childBase.Replace(
                    '"schemaId":"validationChildPolicyV1"',
                    '"schemaId":"validationChildPolicyV1",' +
                    '"schemaId":"validationChildCleanupV1"') },
                @{ id = 'unknown'; raw = $childBase.Replace(
                    'validationChildPolicyV1', 'validationChildUnknownV1') })) {
            $badInvocation = [pscustomobject]@{
                exitCode = 0
                stdout = [string]$discriminatorCase.raw
                stderr = ''
                timedOut = $false
                elapsedMilliseconds = 1
            }
            $badProjection =
                ConvertTo-DiscriminatedChildProjection $badInvocation
            if ($badProjection.state -ne 'invalid' -or
                $badProjection.reason -eq 'none') {
                throw 'gateEvidenceChildDiscriminatorAccepted'
            }
            $invalidSchema = if (
                $badProjection.declaredSchemaId -eq 'none') {
                'none'
            } else {
                [string]$badProjection.declaredSchemaId
            }
            $invalidObservation = New-ArtifactGateObservation `
                -GateId ('self-test-child-discriminator-' +
                    [string]$discriminatorCase.id) `
                -ArtifactKind 'validation' `
                -Invocation $badInvocation `
                -Projection $null -Passed $false `
                -ParseState $badProjection.state `
                -SchemaId $invalidSchema `
                -ParseReason $badProjection.reason `
                -ProjectionMetadata $badProjection
            Write-ArtifactGateObservation $invalidObservation | Out-Null
        }
        $canonicalCleanupInvocation = [pscustomobject]@{
            exitCode = 18
            stdout = ''
            stderr = $cleanupBase
            timedOut = $false
            elapsedMilliseconds = 1
        }
        $canonicalCleanupProjection = ConvertTo-ClosedArtifactProjection `
            $cleanupBase -SchemaId 'validationChildCleanupV1'
        $canonicalCleanupProjection = Close-ArtifactProjectionSemantics `
            $canonicalCleanupProjection `
            (Test-ArtifactNativeTuple `
                'validationChildCleanupV1' $canonicalCleanupInvocation)
        if ($canonicalCleanupProjection.state -ne 'closed' -or
            $canonicalCleanupProjection.value.childPid -ne 0 -or
            $canonicalCleanupProjection.value.childCleanup -ne 'failed') {
            throw 'gateEvidenceCanonicalCleanupRejected'
        }
        $provenanceGateId = 'self-test-cleanup-provenance'
        $provenanceFirst = New-ArtifactGateObservation `
            -GateId $provenanceGateId -ArtifactKind 'validation' `
            -Invocation $canonicalCleanupInvocation `
            -Projection $canonicalCleanupProjection.value `
            -Passed $false -CleanupState 'required' `
            -ParseState $canonicalCleanupProjection.state `
            -SchemaId 'validationChildCleanupV1' `
            -ParseReason $canonicalCleanupProjection.reason
        $provenanceCleanup = New-ArtifactGateObservation `
            -GateId $provenanceGateId -ArtifactKind 'validation' `
            -Invocation $canonicalCleanupInvocation `
            -Projection $canonicalCleanupProjection.value `
            -Passed $false -CleanupState 'completed' `
            -ParseState $canonicalCleanupProjection.state `
            -SchemaId 'validationChildCleanupV1' `
            -ParseReason $canonicalCleanupProjection.reason
        $provenanceFirstSha = Write-ArtifactGateObservation $provenanceFirst
        Write-ArtifactGateCleanupObservation `
            $provenanceCleanup $provenanceFirstSha | Out-Null
        $provenanceFirstReadback = Get-Content -LiteralPath (
            Join-Path $Root ($provenanceGateId + '.first.json')) -Raw |
            ConvertFrom-Json
        $provenanceCleanupReadback = Get-Content -LiteralPath (
            Join-Path $Root ($provenanceGateId + '.cleanup.json')) -Raw |
            ConvertFrom-Json
        foreach ($provenance in @(
                $provenanceFirstReadback, $provenanceCleanupReadback)) {
            if ($provenance.schemaId -ne 'validationChildCleanupV1' -or
                $provenance.parseState -ne 'closed' -or
                $provenance.parseReason -ne 'none') {
                throw 'gateEvidenceCleanupProvenanceInvalid'
            }
        }
        if ($provenanceFirstReadback.stdoutSha256 -ne
                $provenanceCleanupReadback.stdoutSha256 -or
            $provenanceFirstReadback.stderrSha256 -ne
                $provenanceCleanupReadback.stderrSha256 -or
            $provenanceFirstReadback.stdoutLength -ne
                $provenanceCleanupReadback.stdoutLength -or
            $provenanceFirstReadback.stderrLength -ne
                $provenanceCleanupReadback.stderrLength -or
            $provenanceFirstReadback.cleanupState -ne 'required' -or
            $provenanceCleanupReadback.cleanupState -ne 'completed') {
            throw 'gateEvidenceCleanupProvenanceDrifted'
        }
        $noInference = New-ArtifactGateObservation `
            -GateId 'self-test-no-parse-inference' `
            -ArtifactKind 'harness' -Invocation $canonicalCleanupInvocation `
            -Projection $canonicalCleanupProjection.value -Passed $false
        if ($noInference.parseState -ne 'notParsed' -or
            $noInference.schemaId -ne 'none') {
            throw 'gateEvidenceParseInferred'
        }
        $wrongSchema = New-ArtifactGateObservation `
            -GateId 'self-test-wrong-schema' -ArtifactKind 'harness' `
            -Invocation $canonicalCleanupInvocation `
            -Projection $canonicalCleanupProjection.value -Passed $false `
            -ParseState 'closed' -SchemaId 'wrongSchema' -ParseReason 'none'
        try {
            Write-ArtifactGateObservation $wrongSchema | Out-Null
            throw 'gateEvidenceWrongSchemaAccepted'
        }
        catch {
            if ($_.Exception.Message -ne 'gateEvidenceUnavailable') { throw }
        }
        $driftGate = 'self-test-cleanup-hash-drift'
        $driftFirst = New-ArtifactGateObservation `
            -GateId $driftGate -ArtifactKind 'validation' `
            -Invocation $canonicalCleanupInvocation `
            -Projection $canonicalCleanupProjection.value -Passed $false `
            -CleanupState 'required' -ParseState 'closed' `
            -SchemaId 'validationChildCleanupV1' -ParseReason 'none'
        $driftFirstSha = Write-ArtifactGateObservation $driftFirst
        $driftInvocation = [pscustomobject]@{
            exitCode = 18; stdout = ''; stderr = $cleanupBase + ' '
            timedOut = $false; elapsedMilliseconds = 1
        }
        $driftCleanup = New-ArtifactGateObservation `
            -GateId $driftGate -ArtifactKind 'validation' `
            -Invocation $driftInvocation `
            -Projection $canonicalCleanupProjection.value -Passed $false `
            -CleanupState 'completed' -ParseState 'closed' `
            -SchemaId 'validationChildCleanupV1' -ParseReason 'none'
        try {
            Write-ArtifactGateCleanupObservation `
                $driftCleanup $driftFirstSha | Out-Null
            throw 'gateEvidenceCleanupHashDriftAccepted'
        }
        catch {
            if ($_.Exception.Message -ne 'gateEvidenceUnavailable') { throw }
        }
        foreach ($tamperKind in @(
                'passed', 'gateId', 'nativeExit', 'cleanupState',
                'duplicate', 'unknown', 'missing', 'whitespace', 'encoding')) {
            $tamperGate = 'self-test-first-tamper-' + $tamperKind
            $tamperFirst = New-ArtifactGateObservation `
                -GateId $tamperGate -ArtifactKind 'validation' `
                -Invocation $canonicalCleanupInvocation `
                -Projection $canonicalCleanupProjection.value -Passed $false `
                -CleanupState 'required' -ParseState 'closed' `
                -SchemaId 'validationChildCleanupV1' -ParseReason 'none'
            $tamperFirstSha = Write-ArtifactGateObservation $tamperFirst
            $tamperPath = Join-Path $Root ($tamperGate + '.first.json')
            $tamperRaw = Get-Content -LiteralPath $tamperPath -Raw
            $tamperedRaw = switch ($tamperKind) {
                'passed' {
                    $tamperRaw.Replace('"passed":false', '"passed":true')
                }
                'gateId' {
                    $tamperRaw.Replace(
                        '"gateId":"' + $tamperGate + '"',
                        '"gateId":"self-test-first-tamper-changed"')
                }
                'nativeExit' {
                    $tamperRaw.Replace('"nativeExit":18', '"nativeExit":17')
                }
                'cleanupState' {
                    $tamperRaw.Replace(
                        '"cleanupState":"required"',
                        '"cleanupState":"failed"')
                }
                'duplicate' {
                    $tamperRaw.Replace(
                        '"passed":false',
                        '"passed":false,"passed":false')
                }
                'unknown' {
                    $tamperRaw.Substring(0, $tamperRaw.Length - 1) +
                        ',"unknown":false}'
                }
                'missing' {
                    $tamperRaw.Replace(',"nativeExit":18', '')
                }
                'whitespace' { $tamperRaw + ' ' }
                'encoding' { $tamperRaw }
            }
            if ($tamperKind -eq 'encoding') {
                $tamperBytes = [Collections.Generic.List[byte]]::new()
                $tamperBytes.AddRange(
                    [Text.UTF8Encoding]::new($false).GetBytes($tamperedRaw))
                $tamperBytes.Add(255)
                [IO.File]::WriteAllBytes(
                    $tamperPath, $tamperBytes.ToArray())
            }
            else {
                [IO.File]::WriteAllText(
                    $tamperPath, $tamperedRaw,
                    [Text.UTF8Encoding]::new($false))
            }
            $tamperCleanup = New-ArtifactGateObservation `
                -GateId $tamperGate -ArtifactKind 'validation' `
                -Invocation $canonicalCleanupInvocation `
                -Projection $canonicalCleanupProjection.value -Passed $false `
                -CleanupState 'completed' -ParseState 'closed' `
                -SchemaId 'validationChildCleanupV1' -ParseReason 'none'
            try {
                Write-ArtifactGateCleanupObservation `
                    $tamperCleanup $tamperFirstSha | Out-Null
                throw 'gateEvidenceTamperAccepted'
            }
            catch {
                if ($_.Exception.Message -ne 'gateEvidenceUnavailable') {
                    throw
                }
            }
            if (Test-Path -LiteralPath (
                    Join-Path $Root ($tamperGate + '.cleanup.json'))) {
                throw 'gateEvidenceTamperCleanupCreated'
            }
        }
        $invalidCases = @(
            @{ id = 'production-duplicate-same'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase.Replace(
                    '"nativeCode":0', '"nativeCode":0,"nativeCode":0') },
            @{ id = 'production-duplicate-conflict'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase.Replace(
                    '"nativeCode":0', '"nativeCode":0,"nativeCode":5') },
            @{ id = 'production-unknown'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase.Replace(
                    '"aclRollback":"notRequired"',
                    '"aclRollback":"notRequired","unknown":false') },
            @{ id = 'production-missing'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase.Replace(',"nativeCode":0', '') },
            @{ id = 'production-type'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase.Replace(
                    '"success":false', '"success":"false"') },
            @{ id = 'production-negative'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase.Replace('"nativeCode":0', '"nativeCode":-1') },
            @{ id = 'production-overflow'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase.Replace(
                    '"nativeCode":0', '"nativeCode":9223372036854775808') },
            @{ id = 'production-trailing'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase + ' false' },
            @{ id = 'production-ready-code'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase.Replace(
                    '"resultCode":"invalidArguments"',
                    '"resultCode":"secureStorePreflightReady"') },
            @{ id = 'production-success-true'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase.Replace('"success":false', '"success":true') },
            @{ id = 'production-stage-drift'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase.Replace(
                    '"stage":"inputValidation"', '"stage":"finalReadback"') },
            @{ id = 'production-acl-drift'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase.Replace(
                    '"acl":"notAttempted"', '"acl":"exact"') },
            @{ id = 'production-recovery-drift'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase.Replace(
                    '"recovery":"notAttempted"', '"recovery":"recovered"') },
            @{ id = 'production-probe-drift'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase.Replace(
                    '"probe":"notAttempted"', '"probe":"completed"') },
            @{ id = 'production-cleanup-drift'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase.Replace(
                    '"cleanup":"notAttempted"', '"cleanup":"completed"') },
            @{ id = 'production-rollback-drift'; schema = 'productionInvalidArgumentsV1';
                raw = $productionBase.Replace(
                    '"aclRollback":"notRequired"', '"aclRollback":"completed"') },
            @{ id = 'policy-set-cross-code'; schema = 'productionPolicySetFailureV1';
                raw = $policySetBase.Replace(
                    '"nativeCode":5', '"nativeCode":20013') },
            @{ id = 'policy-readback-cross-category';
                schema = 'productionPolicyReadbackFailureV1';
                raw = $policyReadbackBase.Replace(
                    '"nativeCategory":"managedFailure"',
                    '"nativeCategory":"accessDenied"') },
            @{ id = 'argv-policy-duplicate'; schema = 'validationArgvV1';
                raw = $argvBase.Replace(
                    '"policyActive":true',
                    '"policyActive":true,"policyActive":true') },
            @{ id = 'argv-object-confusion'; schema = 'validationArgvV1';
                raw = $argvBase.Replace('"policyActive":true', '"policyActive":{}') },
            @{ id = 'child-handles-type'; schema = 'validationChildPolicyV1';
                raw = $childBase.Replace(
                    '"processHandlesZero":true',
                    '"processHandlesZero":"true"') },
            @{ id = 'child-code-five'; schema = 'validationChildPolicyV1';
                raw = $childBase.Replace(
                    '"nativeCode":367', '"nativeCode":5') },
            @{ id = 'child-code-other'; schema = 'validationChildPolicyV1';
                raw = $childBase.Replace(
                    '"nativeCode":367', '"nativeCode":87') },
            @{ id = 'cleanup-pid-duplicate'; schema = 'validationChildCleanupV1';
                raw = $cleanupBase.Replace(
                    '"childPid":0', '"childPid":0,"childPid":41') },
            @{ id = 'cleanup-array-confusion'; schema = 'validationChildCleanupV1';
                raw = $cleanupBase.Replace(
                    '"childCleanup":"failed"', '"childCleanup":[]') },
            @{ id = 'cleanup-stderr-empty'; schema = 'validationChildCleanupV1';
                raw = '' },
            @{ id = 'cleanup-wrong-schema'; schema = 'validationChildCleanupV1';
                raw = $childBase })
        foreach ($invalidCase in $invalidCases) {
            $projection = ConvertTo-ClosedArtifactProjection `
                ([string]$invalidCase.raw) `
                -SchemaId ([string]$invalidCase.schema)
            if ($projection.state -ne 'invalid' -or
                $null -ne $projection.value) {
                throw 'gateEvidenceInvalidProjectionAccepted'
            }
            $invalidId = 'self-test-invalid-' + [string]$invalidCase.id
            $invalidInvocation = [pscustomobject]@{
                exitCode = 0
                stdout = [string]$invalidCase.raw
                stderr = ''
                timedOut = $false
                elapsedMilliseconds = 1
            }
            $invalidObservation = New-ArtifactGateObservation `
                -GateId $invalidId -ArtifactKind 'harness' `
                -Invocation $invalidInvocation -Projection $null `
                -Passed $false -ParseState 'invalid' `
                -SchemaId ([string]$invalidCase.schema) `
                -ParseReason $projection.reason
            $invalidSha = Write-ArtifactGateObservation $invalidObservation
            $invalidPath = Join-Path $Root ($invalidId + '.first.json')
            $invalidClosed = Get-Content -LiteralPath $invalidPath -Raw |
                ConvertFrom-Json
            if ($invalidClosed.passed -ne $false -or
                $invalidClosed.parseState -ne 'invalid' -or
                $invalidClosed.schemaId -ne [string]$invalidCase.schema -or
                [string]::IsNullOrWhiteSpace($invalidSha)) {
                throw 'gateEvidenceInvalidProjectionReadbackFailed'
            }
        }
        foreach ($nativeCase in @(
                @{ id = 'failed-exit-zero'; exit = 0; schema =
                    'productionPolicySetFailureV1'; raw = $policySetBase },
                @{ id = 'success-exit-eighteen'; exit = 18; schema =
                    'validationArgvV1'; raw = $argvBase },
                @{ id = 'cleanup-exit-zero'; exit = 0; schema =
                    'validationChildCleanupV1'; raw = $cleanupBase },
                @{ id = 'child-failure-exit-zero'; exit = 0; schema =
                    'validationChildFailureV1'; raw = $childFailureBase },
                @{ id = 'child-policy-exit-eighteen'; exit = 18; schema =
                    'validationChildPolicyV1'; raw = $childBase })) {
            $nativeProjection = ConvertTo-ClosedArtifactProjection `
                ([string]$nativeCase.raw) -SchemaId ([string]$nativeCase.schema)
            $nativeInvocation = [pscustomobject]@{
                exitCode = [int]$nativeCase.exit
                stdout = if ([int]$nativeCase.exit -eq 0) {
                    [string]$nativeCase.raw
                } else { '' }
                stderr = if ([int]$nativeCase.exit -eq 18) {
                    [string]$nativeCase.raw
                } else { '' }
            }
            $nativeTupleValid = if (
                [string]$nativeCase.schema -in @(
                    'validationChildCleanupV1',
                    'validationChildFailureV1',
                    'validationChildPolicyV1')) {
                Test-ArtifactNativeTuple `
                    ([string]$nativeCase.schema) $nativeInvocation
            }
            else {
                $false
            }
            $nativeProjection = Close-ArtifactProjectionSemantics `
                $nativeProjection $nativeTupleValid
            if ($nativeProjection.state -ne 'invalid' -or
                $nativeProjection.reason -ne 'semanticTupleMismatch') {
                throw 'gateEvidenceSemanticTupleAccepted'
            }
            $nativeId = 'self-test-' + [string]$nativeCase.id
            $nativeObservation = New-ArtifactGateObservation `
                -GateId $nativeId -ArtifactKind 'harness' `
                -Invocation ([pscustomobject]@{
                    exitCode = [int]$nativeCase.exit
                    stdout = [string]$nativeInvocation.stdout
                    stderr = [string]$nativeInvocation.stderr
                    timedOut = $false
                    elapsedMilliseconds = 1
                }) `
                -Projection $null -Passed $false -ParseState 'invalid' `
                -SchemaId ([string]$nativeCase.schema) `
                -ParseReason $nativeProjection.reason
            Write-ArtifactGateObservation $nativeObservation | Out-Null
        }
        $script:GateEvidenceRoot = Join-Path $Root 'missing\child'
        try {
            Write-ArtifactGateObservation $observation | Out-Null
            throw 'gateEvidenceUnavailableNotRejected'
        }
        catch {
            if ($_.Exception.Message -ne 'gateEvidenceUnavailable') {
                throw
            }
        }
    }
    finally {
        $script:GateEvidenceRoot = $savedRoot
    }
}

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
if ($RunEvidenceSelfTests) {
    if ([string]::IsNullOrWhiteSpace($FixtureRoot)) {
        throw 'gateEvidenceSelfTestRootMissing'
    }
    $evidenceSelfTestRoot = [IO.Path]::GetFullPath($FixtureRoot)
    if (-not $evidenceSelfTestRoot.StartsWith(
            'D:\Development\Ligase\Build\',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'gateEvidenceSelfTestRootInvalid'
    }
    Invoke-GateEvidenceSelfTests $evidenceSelfTestRoot
}
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
    $script:GateEvidenceRoot = Join-Path $fixtureFull 'gate-evidence'
    New-Item -ItemType Directory -Path $script:GateEvidenceRoot `
        -Force | Out-Null
    $selfTest = New-ArtifactGateObservation `
        -GateId 'self-test-child-field-failure' `
        -ArtifactKind 'harness' `
        -Invocation ([pscustomobject]@{
            exitCode = 18
            stdout = ''
            stderr = '{"result":"failed"}'
            timedOut = $false
            elapsedMilliseconds = 1
        }) `
        -Projection ([pscustomobject]@{
            policyActive = $true
            childCreationBlocked = $false
            childProcessCreated = $false
            processHandlesZero = $true
            childPid = 0
            nativeCode = 5
            childCleanup = 'completed'
        }) `
        -Passed $false `
        -CleanupState 'completed'
    $selfTestSha = Write-ArtifactGateObservation $selfTest
    $selfTestPath = Join-Path $script:GateEvidenceRoot `
        'self-test-child-field-failure.first.json'
    $selfTestReadback = Get-Content -LiteralPath $selfTestPath -Raw |
        ConvertFrom-Json
    if ($selfTestReadback.gateId -ne 'self-test-child-field-failure' -or
        $selfTestReadback.passed -ne $false -or
        $selfTestReadback.createProcessWin32Code -ne 5 -or
        $selfTestReadback.stdoutSha256 -ne (Get-TextSha256 '') -or
        $selfTestSha -ne (Get-TextSha256 (
            Get-Content -LiteralPath $selfTestPath -Raw))) {
        throw 'gateEvidenceUnavailable'
    }
    foreach ($fieldCase in @(
            @{ id = 'policy'; policy = $false; handles = $true;
                pid = 0; sentinel = $false; cleanup = 'completed' },
            @{ id = 'handles'; policy = $true; handles = $false;
                pid = 0; sentinel = $false; cleanup = 'completed' },
            @{ id = 'pid'; policy = $true; handles = $true;
                pid = 41; sentinel = $false; cleanup = 'required' },
            @{ id = 'sentinel'; policy = $true; handles = $true;
                pid = 0; sentinel = $true; cleanup = 'completed' },
            @{ id = 'cleanup'; policy = $true; handles = $true;
                pid = 0; sentinel = $false; cleanup = 'failed' })) {
        $caseId = 'self-test-child-' + [string]$fieldCase.id
        $caseObservation = New-ArtifactGateObservation `
            -GateId $caseId `
            -ArtifactKind 'harness' `
            -Invocation ([pscustomobject]@{
                exitCode = 18; stdout = ''; stderr = ''
                timedOut = $false; elapsedMilliseconds = 1
            }) `
            -Projection ([pscustomobject]@{
                policyActive = [bool]$fieldCase.policy
                childCreationBlocked = $false
                childProcessCreated = ([int]$fieldCase.pid -ne 0)
                processHandlesZero = [bool]$fieldCase.handles
                childPid = [int]$fieldCase.pid
                nativeCode = 5
                childCleanup = [string]$fieldCase.cleanup
            }) `
            -Passed $false `
            -CleanupState ([string]$fieldCase.cleanup) `
            -SentinelExists ([bool]$fieldCase.sentinel)
        $caseSha = Write-ArtifactGateObservation $caseObservation
        $casePath = Join-Path $script:GateEvidenceRoot (
            $caseId + '.first.json')
        $caseRaw = Get-Content -LiteralPath $casePath -Raw
        $caseClosed = $caseRaw | ConvertFrom-Json
        if ($caseClosed.gateId -ne $caseId -or
            $caseClosed.passed -ne $false -or
            $caseSha -ne (Get-TextSha256 $caseRaw)) {
            throw 'gateEvidenceUnavailable'
        }
    }
    $savedEvidenceRoot = $script:GateEvidenceRoot
    try {
        $script:GateEvidenceRoot = Join-Path $fixtureFull 'missing-parent\x'
        try {
            Write-ArtifactGateObservation $selfTest | Out-Null
            throw 'gateEvidenceUnavailableNotRejected'
        }
        catch {
            if ($_.Exception.Message -ne 'gateEvidenceUnavailable') {
                throw
            }
        }
    }
    finally {
        $script:GateEvidenceRoot = $savedEvidenceRoot
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
        Complete-ArtifactGate (
            New-ArtifactGateObservation `
                -GateId 'harness-unsupported-argument-api' `
                -ArtifactKind 'harness' `
                -Invocation $null -Projection $null -Passed $false) `
            -FailureCode 'secureStorePreflightUnsupportedApiStartedProcess' |
            Out-Null
    }
    Complete-ArtifactGate (
        New-ArtifactGateObservation `
            -GateId 'harness-unsupported-argument-api' `
            -ArtifactKind 'harness' `
            -Invocation $null -Projection $null -Passed $true) `
        -FailureCode 'secureStorePreflightUnsupportedApiStartedProcess' |
        Out-Null

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
    $productionPassed = $production.exitCode -eq 18 -and
        $production.stdout.Length -eq 0 -and
        $production.stderr.Length -le 4096
    $productionParse = ConvertTo-ClosedArtifactProjection `
        $production.stderr -SchemaId 'productionInvalidArgumentsV1'
    $productionParse = Close-ArtifactProjectionSemantics `
        $productionParse `
        ($production.exitCode -eq 18 -and
            $production.stdout.Length -eq 0 -and
            $production.stderr.Length -le 4096)
    $productionFailure = $productionParse.value
    $productionPassed = $productionPassed -and
        $productionParse.state -eq 'closed' -and
        $productionFailure.stage -eq 'inputValidation' -and
        $productionFailure.resultCode -eq 'invalidArguments' -and
        $productionFailure.success -eq $false
    Complete-ArtifactGate (
        New-ArtifactGateObservation `
            -GateId 'production-extra-argv' `
            -ArtifactKind 'production' `
            -Invocation $production `
            -Projection $productionFailure `
            -Passed $productionPassed `
            -ParseState $productionParse.state `
            -SchemaId 'productionInvalidArgumentsV1' `
            -ParseReason $productionParse.reason) `
        -FailureCode 'secureStorePreflightProductionArgvBoundaryInvalid' |
        Out-Null

    $argv = Invoke-FixedArtifactToken `
        -Artifact $ValidationArtifact `
        -Token '--validate-argv-token'
    $argvParse = ConvertTo-ClosedArtifactProjection `
        $argv.stdout -SchemaId 'validationArgvV1'
    $argvParse = Close-ArtifactProjectionSemantics `
        $argvParse ($argv.exitCode -eq 0 -and $argv.stderr.Length -eq 0)
    $argvResult = $argvParse.value
    $argvPassed = $argv.exitCode -eq 0 -and
        $argv.stderr.Length -eq 0 -and
        $argvParse.state -eq 'closed' -and
        $argvResult.result -eq 'passed' -and
        $argvResult.policyActive -eq $true -and
        $argvResult.argumentCount -eq 1 -and
        $argvResult.token -eq '--validate-argv-token'
    Complete-ArtifactGate (
        New-ArtifactGateObservation `
            -GateId 'validation-argv-echo' `
            -ArtifactKind 'validation' `
            -Invocation $argv `
            -Projection $argvResult `
            -Passed $argvPassed `
            -ParseState $argvParse.state `
            -SchemaId 'validationArgvV1' `
            -ParseReason $argvParse.reason) `
        -FailureCode 'secureStorePreflightValidationArgvInvalid' |
        Out-Null

    $sentinel = Join-Path (
        Split-Path -Parent $ValidationArtifact) `
        'ligase-preflight-child-sentinel.txt'
    if (Test-Path -LiteralPath $sentinel) {
        Complete-ArtifactGate (
            New-ArtifactGateObservation `
                -GateId 'validation-child-sentinel-preflight' `
                -ArtifactKind 'validation' `
                -Invocation $null -Projection $null -Passed $false `
                -SentinelExists $true) `
            -FailureCode 'secureStorePreflightChildSentinelPreexisting' |
            Out-Null
    }
    Complete-ArtifactGate (
        New-ArtifactGateObservation `
            -GateId 'validation-child-sentinel-preflight' `
            -ArtifactKind 'validation' `
            -Invocation $null -Projection $null -Passed $true `
            -SentinelExists $false) `
        -FailureCode 'secureStorePreflightChildSentinelPreexisting' |
        Out-Null
    $child = Invoke-FixedArtifactToken `
        -Artifact $ValidationArtifact `
        -Token '--validate-child-policy'
    $childParse = ConvertTo-DiscriminatedChildProjection $child
    $childSchemaId = [string]$childParse.declaredSchemaId
    $childNativeTupleValid = if ($childSchemaId -in @(
            'validationChildPolicyV1',
            'validationChildFailureV1',
            'validationChildCleanupV1')) {
        Test-ArtifactNativeTuple $childSchemaId $child
    }
    else {
        $false
    }
    $childParse = Close-ArtifactProjectionSemantics `
        $childParse $childNativeTupleValid
    $childResult = $childParse.value
    if ($childParse.state -ne 'closed') {
        Complete-ArtifactGate (
            New-ArtifactGateObservation `
                -GateId 'validation-child-policy' `
                -ArtifactKind 'validation' `
                -Invocation $child `
                -Projection $null `
                -Passed $false `
                -CleanupState 'notEvaluated' `
                -SentinelExists (Test-Path -LiteralPath $sentinel) `
                -ParseState $childParse.state `
                -SchemaId $childSchemaId `
                -ParseReason $childParse.reason `
                -ProjectionMetadata $childParse) `
            -FailureCode 'secureStorePreflightChildPolicyInvalid' |
            Out-Null
    }
    if ($childSchemaId -eq 'validationChildFailureV1') {
        Complete-ArtifactGate (
            New-ArtifactGateObservation `
                -GateId 'validation-child-policy' `
                -ArtifactKind 'validation' `
                -Invocation $child `
                -Projection $childResult `
                -Passed $false `
                -CleanupState 'notRequired' `
                -SentinelExists (Test-Path -LiteralPath $sentinel) `
                -ParseState $childParse.state `
                -SchemaId $childSchemaId `
                -ParseReason $childParse.reason `
                -ProjectionMetadata $childParse) `
            -FailureCode 'secureStorePreflightChildPolicyMachineFailure' |
            Out-Null
    }
    $childPidProperty = $childResult.PSObject.Properties['childPid']
    if ($childSchemaId -eq 'validationChildCleanupV1') {
        $childPid = [int]$childPidProperty.Value
        $firstFailure = New-ArtifactGateObservation `
            -GateId 'validation-child-policy' `
            -ArtifactKind 'validation' `
            -Invocation $child `
            -Projection $childResult `
            -Passed $false `
            -CleanupState 'required' `
            -SentinelExists (Test-Path -LiteralPath $sentinel) `
            -ParseState $childParse.state `
            -SchemaId $childSchemaId `
            -ParseReason $childParse.reason `
            -ProjectionMetadata $childParse
        $firstFailureSha = Write-ArtifactGateObservation $firstFailure
        $childCleanupState = if (
            $childResult.childCleanup -eq 'completed' -and
            $childPid -eq 0 -and
            -not (Test-Path -LiteralPath $sentinel)) {
            'completed'
        } else {
            'failed'
        }
        try {
            if ($childPid -ne 0) {
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
                            throw 'cleanupFailed'
                        }
                    }
                    finally {
                        $childProcess.Dispose()
                    }
                }
                if ($null -ne (Get-Process -Id $childPid `
                        -ErrorAction SilentlyContinue) -or
                    (Test-Path -LiteralPath $sentinel)) {
                    throw 'cleanupFailed'
                }
                $childCleanupState = 'completed'
            }
        }
        catch {
            $childCleanupState = 'failed'
        }
        $childCleanupObservation = New-ArtifactGateObservation `
            -GateId 'validation-child-policy' `
            -ArtifactKind 'validation' `
            -Invocation $child `
            -Projection $childResult `
            -Passed $false `
            -CleanupState $childCleanupState `
            -SentinelExists (Test-Path -LiteralPath $sentinel) `
            -ParseState $childParse.state `
            -SchemaId $childSchemaId `
            -ParseReason $childParse.reason `
            -ProjectionMetadata $childParse
        Write-ArtifactGateCleanupObservation `
            $childCleanupObservation $firstFailureSha |
            Out-Null
        if ($childCleanupState -ne 'completed') {
            throw (
                'secureStorePreflightChildCleanupFailed gateId=' +
                'validation-child-policy evidenceSha256=' + $firstFailureSha)
        }
        throw (
            'secureStorePreflightChildCreationUnexpected gateId=' +
            'validation-child-policy evidenceSha256=' + $firstFailureSha)
    }
    $childPassed = $child.exitCode -eq 0 -and
        $child.stderr.Length -eq 0 -and
        $childResult.policyActive -eq $true -and
        $childResult.childCreationBlocked -eq $true -and
        $childResult.childProcessCreated -eq $false -and
        $childResult.processHandlesZero -eq $true -and
        $childResult.nativeCode -eq 367 -and
        $childResult.sentinelExists -eq $false -and
        -not (Test-Path -LiteralPath $sentinel)
    Complete-ArtifactGate (
        New-ArtifactGateObservation `
            -GateId 'validation-child-policy' `
            -ArtifactKind 'validation' `
            -Invocation $child `
            -Projection $childResult `
            -Passed $childPassed `
            -CleanupState 'notRequired' `
            -SentinelExists (Test-Path -LiteralPath $sentinel) `
            -ParseState $childParse.state `
            -SchemaId $childSchemaId `
            -ParseReason $childParse.reason `
            -ProjectionMetadata $childParse) `
        -FailureCode 'secureStorePreflightChildPolicyInvalid' |
        Out-Null
    $hang = Invoke-FixedArtifactToken `
        -Artifact $ValidationArtifact `
        -Token '--validate-hang-pipes'
    $hangPassed = $hang.timedOut -eq $true -and
        $hang.elapsedMilliseconds -le 20000 -and
        $hang.pipesDrained -and
        $null -eq (Get-Process -Id $hang.processId `
            -ErrorAction SilentlyContinue)
    Complete-ArtifactGate (
        New-ArtifactGateObservation `
            -GateId 'validation-hang-bounded' `
            -ArtifactKind 'validation' `
            -Invocation $hang `
            -Projection $null `
            -Passed $hangPassed `
            -CleanupState $(if ($hangPassed) { 'completed' } else { 'failed' })) `
        -FailureCode 'secureStorePreflightArtifactHangNotBounded' |
        Out-Null
    foreach ($cleanupFault in @(
            'taskkillHang',
            'taskkillExitNonzero',
            'targetKillFailure',
            'targetWaitTimeout')) {
        $cleanup = Invoke-FixedArtifactToken `
            -Artifact $ValidationArtifact `
            -Token '--validate-hang-pipes' `
            -CleanupFaultMode $cleanupFault
        $cleanupPassed = $cleanup.timedOut -eq $true -and
            $cleanup.elapsedMilliseconds -le 20000 -and
            $cleanup.pipesDrained -and
            $null -eq (Get-Process -Id $cleanup.processId `
                -ErrorAction SilentlyContinue)
        Complete-ArtifactGate (
            New-ArtifactGateObservation `
                -GateId ('validation-cleanup-' + $cleanupFault) `
                -ArtifactKind 'validation' `
                -Invocation $cleanup `
                -Projection $null `
                -Passed $cleanupPassed `
                -CleanupState $(if ($cleanupPassed) {
                    'completed'
                } else {
                    'failed'
                })) `
            -FailureCode 'secureStorePreflightCleanupFaultNotBounded' |
            Out-Null
    }

    foreach ($fault in @(
            '--validate-policy-set-fault',
            '--validate-policy-readback-mismatch')) {
        $failure = Invoke-FixedArtifactToken `
            -Artifact $ValidationArtifact -Token $fault
        $failureSchemaId = if ($fault -eq '--validate-policy-set-fault') {
            'productionPolicySetFailureV1'
        }
        else {
            'productionPolicyReadbackFailureV1'
        }
        $failureParse = ConvertTo-ClosedArtifactProjection `
            $failure.stderr -SchemaId $failureSchemaId
        $failureParse = Close-ArtifactProjectionSemantics `
            $failureParse `
            ($failure.exitCode -eq 18 -and $failure.stdout.Length -eq 0)
        $failureResult = $failureParse.value
        $failurePassed = $failure.exitCode -eq 18 -and
            $failure.stdout.Length -eq 0 -and
            $failureParse.state -eq 'closed' -and
            $failureResult.success -eq $false -and
            $failureResult.stage -eq 'securityInitialization'
        Complete-ArtifactGate (
            New-ArtifactGateObservation `
                -GateId ('validation-policy-' + $fault.Substring(11)) `
                -ArtifactKind 'validation' `
                -Invocation $failure `
                -Projection $failureResult `
                -Passed $failurePassed `
                -ParseState $failureParse.state `
                -SchemaId $failureSchemaId `
                -ParseReason $failureParse.reason) `
            -FailureCode 'secureStorePreflightPolicyFaultProjectionInvalid' |
            Out-Null
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
            Complete-ArtifactGate (
                New-ArtifactGateObservation `
                    -GateId 'production-validation-seam-scan' `
                    -ArtifactKind 'production' `
                    -Invocation $null -Projection $null -Passed $false) `
                -FailureCode `
                    'secureStorePreflightValidationSeamInProductionArtifact' |
                Out-Null
        }
    }
    Complete-ArtifactGate (
        New-ArtifactGateObservation `
            -GateId 'production-validation-seam-scan' `
            -ArtifactKind 'production' `
            -Invocation $null -Projection $null -Passed $true) `
        -FailureCode 'secureStorePreflightValidationSeamInProductionArtifact' |
        Out-Null
    $rootUnchanged = (Get-SafeAdminRootFingerprint) -eq $before
    Complete-ArtifactGate (
        New-ArtifactGateObservation `
            -GateId 'real-admin-root-fingerprint' `
            -ArtifactKind 'harness' `
            -Invocation $null -Projection $null -Passed $rootUnchanged) `
        -FailureCode 'secureStorePreflightArtifactTouchedAdminRoot' |
        Out-Null
    $artifactChecks = 5
}

[pscustomobject]@{
    result = 'passed'
    checks = 3 + $artifactChecks
    executableBuilt = $false
    artifactGatesRun = [bool]$RunArtifactGates
    evidenceSelfTestsRun = [bool]$RunEvidenceSelfTests
    processStartCount = $script:ProcessStartCount
    systemMutation = $false
} | ConvertTo-Json -Compress
