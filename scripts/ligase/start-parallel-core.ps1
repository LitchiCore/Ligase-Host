[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$DeployDirectory,
    [int]$BasePort = 49989
)

$ErrorActionPreference = "Stop"
$DeployDirectory = (Resolve-Path -LiteralPath $DeployDirectory).Path
$binary = Join-Path $DeployDirectory "sunshine.exe"
$configDirectory = Join-Path $DeployDirectory "config"
$configFile = Join-Path $configDirectory "ligase-parallel.conf"
$ports = @(
    $BasePort - 5,
    $BasePort,
    $BasePort + 1,
    $BasePort + 9,
    $BasePort + 10,
    $BasePort + 11,
    $BasePort + 21
)

if (-not (Test-Path -LiteralPath $binary)) {
    throw "Ligase core binary not found: $binary"
}

$occupied = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object LocalPort -In $ports
if ($occupied) {
    $details = $occupied |
        Sort-Object LocalPort |
        ForEach-Object { "$($_.LocalPort) (PID $($_.OwningProcess))" }
    throw "The isolated Ligase port family is already in use: $($details -join ', ')"
}

New-Item -ItemType Directory -Force -Path $configDirectory | Out-Null
$configuration = @"
port = $BasePort
address_family = ipv4
upnp = disabled
system_tray = disabled
enable_discovery = disabled
sunshine_name = Ligase Host Parallel
file_apps = $DeployDirectory\config\apps.json
file_state = $DeployDirectory\config\state.json
credentials_file = $DeployDirectory\config\credentials.json
pkey = $DeployDirectory\config\cakey.pem
cert = $DeployDirectory\config\cacert.pem
log_path = $DeployDirectory\config\sunshine.log
"@
Set-Content -LiteralPath $configFile -Value $configuration -Encoding UTF8

$stdout = Join-Path $configDirectory "process.stdout.log"
$stderr = Join-Path $configDirectory "process.stderr.log"
$process = Start-Process `
    -FilePath $binary `
    -ArgumentList @($configFile) `
    -WorkingDirectory $DeployDirectory `
    -WindowStyle Hidden `
    -RedirectStandardOutput $stdout `
    -RedirectStandardError $stderr `
    -PassThru

Start-Sleep -Seconds 8
if ($process.HasExited) {
    $diagnostic = Get-Content -LiteralPath $stdout -Tail 40 -ErrorAction SilentlyContinue
    throw "Ligase core exited during startup.`n$($diagnostic -join [Environment]::NewLine)"
}

[pscustomobject]@{
    Pid = $process.Id
    Binary = $binary
    Config = $configFile
    BasePort = $BasePort
    HttpsPort = $BasePort - 5
    WebUiPort = $BasePort + 1
    Rollback = "Stop-Process -Id $($process.Id)"
}
