[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$RunDirectory,
    [Parameter(Mandatory)] [string]$Pin,
    [string]$DeviceName = "Ligase Android IPv6 acceptance"
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
if ($Pin -notmatch '^\d{4}$') {
    throw "Pairing PIN must contain exactly four digits."
}
$metadataFile = Join-Path $RunDirectory "acceptance-metadata.json"
$secretFile = Join-Path $RunDirectory "management-password.dpapi"
if (-not (Test-Path -LiteralPath $metadataFile -PathType Leaf) -or
    -not (Test-Path -LiteralPath $secretFile -PathType Leaf)) {
    throw "Acceptance metadata or protected credential is missing."
}
$metadata = Get-Content -Raw -LiteralPath $metadataFile | ConvertFrom-Json
$process = Get-CimInstance Win32_Process -Filter "ProcessId=$($metadata.pid)"
if (-not $process) {
    throw "Acceptance core is not running."
}

$protectedPassword = (Get-Content -Raw -LiteralPath $secretFile).Trim()
$securePassword = $protectedPassword | ConvertTo-SecureString
$credential = [System.Net.NetworkCredential]::new($metadata.managementUsername, $securePassword)
$expectedPort = [int]$metadata.basePort + 1
try {
    $password = $credential.Password
    $loginPayload = @{
        username = $metadata.managementUsername
        password = $password
    } | ConvertTo-Json -Compress
    $escapedLoginPayload = $loginPayload.Replace('"', '\"')
    $loginConfiguration = @"
url = "https://localhost:$expectedPort/api/login"
insecure
silent
show-error
fail-with-body
include
header = "Content-Type: application/json"
data = "$escapedLoginPayload"
"@
    $loginResponse = Invoke-LoopbackCurlConfig -Configuration $loginConfiguration
    $authCookie = [regex]::Match($loginResponse, 'Set-Cookie:\s*auth=([^;]+)').Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($authCookie)) {
        throw "Acceptance management login did not return an auth cookie."
    }

    $payload = @{ pin = $Pin; name = $DeviceName } | ConvertTo-Json -Compress
    $escapedPayload = $payload.Replace('"', '\"')
    $curlConfiguration = @"
url = "https://localhost:$expectedPort/api/pin"
insecure
silent
show-error
fail-with-body
header = "Content-Type: application/json"
cookie = "auth=$authCookie"
data = "$escapedPayload"
"@
    $result = Invoke-LoopbackCurlConfig -Configuration $curlConfiguration | ConvertFrom-Json
    [pscustomobject]@{
        HttpStatus = 200
        PairingStatus = [bool]$result.status
        AcceptancePid = [int]$metadata.pid
        BasePort = [int]$metadata.basePort
        HostUniqueId = $metadata.hostUniqueId
    }
}
finally {
    $Pin = $null
    $payload = $null
    $password = $null
    $loginPayload = $null
    $escapedLoginPayload = $null
    $loginConfiguration = $null
    $loginResponse = $null
    $authCookie = $null
    $escapedPayload = $null
    $curlConfiguration = $null
    $credential = $null
    $securePassword = $null
    $protectedPassword = $null
}
