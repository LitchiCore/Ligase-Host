[CmdletBinding()]
param(
    [string]$Binary = "$env:LOCALAPPDATA\LigaseBuild\deploy\7d7a06de\sunshine.exe",
    [string]$ProjectionRoot = "$env:LOCALAPPDATA\LigaseBuild\deploy\14d56844",
    [string]$AcceptanceRoot = "$env:LOCALAPPDATA\LigaseBuild\acceptance",
    [int]$BasePort = 50989
)

$ErrorActionPreference = "Stop"

function Invoke-LoopbackCurlConfig {
    param([Parameter(Mandatory)] [string]$Configuration)
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = "curl.exe"
    $startInfo.Arguments = "--config -"
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $curl = [System.Diagnostics.Process]::Start($startInfo)
    $curl.StandardInput.Write($Configuration)
    $curl.StandardInput.Close()
    $output = $curl.StandardOutput.ReadToEnd()
    $errorOutput = $curl.StandardError.ReadToEnd()
    $curl.WaitForExit()
    if ($curl.ExitCode -ne 0) {
        throw "Loopback management request failed with curl exit code $($curl.ExitCode): $errorOutput"
    }
    return $output
}
$requiredOffsets = @(-5, 0, 1, 9, 10, 11, 21)
$requiredPorts = $requiredOffsets | ForEach-Object { $BasePort + $_ }
if ($BasePort -lt 48989 -or $BasePort -gt 65464) {
    throw "Acceptance base port is outside the supported range."
}
if (-not (Test-Path -LiteralPath $Binary -PathType Leaf)) {
    throw "Acceptance core binary was not found."
}
$occupied = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object LocalPort -In $requiredPorts
if ($occupied) {
    $details = $occupied | ForEach-Object { "$($_.LocalPort):PID$($_.OwningProcess)" }
    throw "Acceptance port family is already occupied: $($details -join ', ')"
}

$projectionFiles = @(
    [pscustomobject]@{
        Source = Join-Path $ProjectionRoot "config\apps.json"
        RelativeDestination = "config\apps.json"
    },
    [pscustomobject]@{
        Source = Join-Path $ProjectionRoot "library.json"
        RelativeDestination = "library.json"
    },
    [pscustomobject]@{
        Source = Join-Path $ProjectionRoot "ligase-sync.json"
        RelativeDestination = "ligase-sync.json"
    },
    [pscustomobject]@{
        Source = Join-Path $ProjectionRoot "streaming.json"
        RelativeDestination = "streaming.json"
    }
)
foreach ($projection in $projectionFiles) {
    if (-not (Test-Path -LiteralPath $projection.Source -PathType Leaf)) {
        throw "Required product projection is missing: $($projection.RelativeDestination)"
    }
}

$runSuffix = ([guid]::NewGuid().ToString("N"))[0..7] -join ""
$runId = "ipv6-stage2-{0:yyyyMMdd-HHmmss}-{1}" -f (Get-Date), $runSuffix
$runDirectory = Join-Path $AcceptanceRoot $runId
$configDirectory = Join-Path $runDirectory "config"
New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null

$copyEvidence = foreach ($projection in $projectionFiles) {
    $destination = Join-Path $runDirectory $projection.RelativeDestination
    $destinationDirectory = Split-Path -Parent $destination
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $projection.Source -Destination $destination
    [pscustomobject]@{
        Name = $projection.RelativeDestination.Replace('\', '/')
        SourceSha256 = (Get-FileHash -LiteralPath $projection.Source -Algorithm SHA256).Hash
        DestinationSha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    }
}

$configFile = Join-Path $configDirectory "ligase-ipv6-acceptance.conf"
$stateFile = Join-Path $configDirectory "state.json"
$credentialsFile = Join-Path $configDirectory "credentials.json"
$privateKeyFile = Join-Path $configDirectory "cakey.pem"
$certificateFile = Join-Path $configDirectory "cacert.pem"
$logFile = Join-Path $configDirectory "sunshine.log"
$configuration = @"
port = $BasePort
address_family = both
upnp = disabled
system_tray = disabled
enable_discovery = disabled
sunshine_name = Ligase IPv6 Acceptance $runId
file_apps = $configDirectory\apps.json
file_state = $stateFile
credentials_file = $credentialsFile
pkey = $privateKeyFile
cert = $certificateFile
log_path = $logFile
"@
[System.IO.File]::WriteAllText(
    $configFile,
    $configuration,
    [System.Text.UTF8Encoding]::new($false))

$stdout = Join-Path $configDirectory "process.stdout.log"
$stderr = Join-Path $configDirectory "process.stderr.log"
$process = Start-Process `
    -FilePath $Binary `
    -ArgumentList @($configFile) `
    -WorkingDirectory (Split-Path -Parent $Binary) `
    -WindowStyle Hidden `
    -RedirectStandardOutput $stdout `
    -RedirectStandardError $stderr `
    -PassThru

try {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        if ($process.HasExited) {
            throw "Acceptance core exited during startup."
        }
        Start-Sleep -Milliseconds 250
        $listener = Get-NetTCPConnection -State Listen -LocalPort ($BasePort + 1) -ErrorAction SilentlyContinue |
            Where-Object OwningProcess -eq $process.Id
    } while (-not $listener -and (Get-Date) -lt $deadline)
    if (-not $listener) {
        throw "Acceptance management listener did not become ready."
    }

    $username = "ligase-acceptance-$($runId.Split('-')[-1])"
    $randomBytes = [byte[]]::new(32)
    $randomNumberGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $randomNumberGenerator.GetBytes($randomBytes)
    $randomNumberGenerator.Dispose()
    $password = [Convert]::ToBase64String($randomBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

    $payload = @{
        currentUsername = ""
        currentPassword = ""
        newUsername = $username
        newPassword = $password
        confirmNewPassword = $password
    } | ConvertTo-Json -Compress
    $escapedPayload = $payload.Replace('"', '\"')
    $curlConfiguration = @"
url = "https://localhost:$($BasePort + 1)/api/password"
insecure
silent
show-error
fail-with-body
header = "Content-Type: application/json"
data = "$escapedPayload"
"@
    $initializationResult = Invoke-LoopbackCurlConfig -Configuration $curlConfiguration |
        ConvertFrom-Json
    if (-not $initializationResult.status) {
        throw "Acceptance first-run initialization returned a negative status."
    }

    $secretFile = Join-Path $runDirectory "management-password.dpapi"
    ConvertTo-SecureString -String $password -AsPlainText -Force |
        ConvertFrom-SecureString |
        Set-Content -LiteralPath $secretFile -Encoding ASCII
    $currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $acl = [System.Security.AccessControl.FileSecurity]::new()
    $acl.SetOwner([System.Security.Principal.NTAccount]::new($currentIdentity))
    $acl.SetAccessRuleProtection($true, $false)
    $accessRule = [System.Security.AccessControl.FileSystemAccessRule]::new(
        $currentIdentity,
        [System.Security.AccessControl.FileSystemRights]::FullControl,
        [System.Security.AccessControl.AccessControlType]::Allow)
    $acl.AddAccessRule($accessRule)
    Set-Acl -LiteralPath $secretFile -AclObject $acl

    $password = $null
    [Array]::Clear($randomBytes, 0, $randomBytes.Length)
    $payload = $null
    $escapedPayload = $null
    $curlConfiguration = $null

    $serverInfo = Invoke-RestMethod `
        -Uri "http://127.0.0.1:$BasePort/serverinfo?uniqueid=ligase-acceptance-readiness" `
        -TimeoutSec 5
    $hostUniqueId = ([xml]$serverInfo.OuterXml).root.uniqueid
    $x509Certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $certificateFile)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $certificateSha256 = ([BitConverter]::ToString(
        $sha256.ComputeHash($x509Certificate.RawData))).Replace("-", "")
    $sha256.Dispose()
    $x509Certificate.Dispose()
    $aclEvidence = (Get-Acl -LiteralPath $secretFile).Access |
        ForEach-Object { "$($_.IdentityReference):$($_.FileSystemRights):$($_.AccessControlType)" }
    $metadata = [ordered]@{
        runId = $runId
        pid = $process.Id
        basePort = $BasePort
        binarySha256 = (Get-FileHash -LiteralPath $Binary -Algorithm SHA256).Hash
        hostUniqueId = $hostUniqueId
        certificateSha256 = $certificateSha256
        managementUsername = $username
        copiedProjections = $copyEvidence
    }
    $metadata | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $runDirectory "acceptance-metadata.json") -Encoding UTF8

    [pscustomobject]@{
        RunId = $runId
        RunDirectory = $runDirectory
        Pid = $process.Id
        BasePort = $BasePort
        HostUniqueId = $hostUniqueId
        CertificateSha256 = $certificateSha256
        ListenerAddress = ($listener | Select-Object -ExpandProperty LocalAddress -Unique) -join ","
        CopiedFiles = ($copyEvidence.Name -join ",")
        CopyHashesMatch = -not ($copyEvidence | Where-Object SourceSha256 -ne DestinationSha256)
        SecretAclProtected = (Get-Acl -LiteralPath $secretFile).AreAccessRulesProtected
        SecretAclEntries = $aclEvidence -join ";"
    }
}
catch {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
    throw
}
