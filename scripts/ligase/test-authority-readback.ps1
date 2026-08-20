[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Binary,
    [string]$RunRoot,
    [ValidateRange(1024, 65000)] [int]$Port = 51989
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "Resolve-DevelopmentRoot.ps1")
if ([string]::IsNullOrWhiteSpace($RunRoot)) {
    $RunRoot = Join-Path (Resolve-LigaseBuildRoot) "acceptance"
}
$RunRoot = [IO.Path]::GetFullPath(
    [Environment]::ExpandEnvironmentVariables($RunRoot))
Add-Type -AssemblyName System.Net.Http
$runId = "authority-p1-" + (Get-Date -Format "yyyyMMdd-HHmmss")
$root = Join-Path $RunRoot $runId
$configRoot = Join-Path $root "config"
New-Item -ItemType Directory -Path $configRoot -Force | Out-Null

$token = "p1-" + [Guid]::NewGuid().ToString("N")
$nonce = [Guid]::NewGuid().ToString("N")
$fingerprint = "authority-smoke-root"
$gameUuid = "f3d67f4d-b1fe-4c5d-a77e-b78a51051c1a"
$fixtureUuid = "11111111-2222-3333-4444-555555555555"
$utf8 = [Text.UTF8Encoding]::new($false)
$httpHandler = [System.Net.Http.HttpClientHandler]::new()
$httpHandler.UseProxy = $false
$httpClient = [System.Net.Http.HttpClient]::new($httpHandler)
$httpClient.Timeout = [TimeSpan]::FromSeconds(5)

function Write-Json {
    param([string]$Path, [object]$Value)
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 20), $utf8)
}

function Invoke-AuthorityPost {
    param([string]$Path, [string]$Body)
    $content = [System.Net.Http.StringContent]::new($Body, $utf8, "application/json")
    $response = $httpClient.PostAsync(
        "http://127.0.0.1:$Port$Path",
        $content).GetAwaiter().GetResult()
    $payload = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if (-not $response.IsSuccessStatusCode) {
        throw "Authority endpoint returned HTTP $([int]$response.StatusCode): $payload"
    }
    return $payload | ConvertFrom-Json
}

function Invoke-AuthorityPostRaw {
    param([string]$Path, [string]$Body)
    $content = [System.Net.Http.StringContent]::new($Body, $utf8, "application/json")
    $response = $httpClient.PostAsync(
        "http://127.0.0.1:$Port$Path", $content).GetAwaiter().GetResult()
    $payload = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    [pscustomobject]@{
        Status = [int]$response.StatusCode
        Body = $payload | ConvertFrom-Json
    }
}

function Assert-ReloadFailurePreservesOldState {
    param([string]$Reason, [string]$Stage, [string]$Body)
    $failed = Invoke-AuthorityPostRaw "/ligase/v1/authority/reload" $Body
    if ($failed.Status -ne 500 -or
        $failed.Body.reload.resultCode -ne "failed" -or
        $failed.Body.reload.stage -ne $Stage -or
        $failed.Body.reload.reasonCode -ne $Reason) {
        throw "Unexpected typed reload failure for $Reason`: $($failed | ConvertTo-Json -Depth 8 -Compress)"
    }
    $unchanged = Invoke-AuthorityPost "/ligase/v1/authority/readback" $Body
    if (-not ($unchanged.apps | Where-Object uuid -EQ $gameUuid) -or
        ($unchanged.apps | Where-Object uuid -EQ $fixtureUuid)) {
        throw "Failed reload $Reason replaced the active app catalog."
    }
}

Write-Json (Join-Path $root "ligase-authority.json") ([ordered]@{
    schemaVersion = 1
    token = $token
    startNonce = $nonce
    rootFingerprint = $fingerprint
})
Write-Json (Join-Path $root "library.json") ([ordered]@{
    schemaVersion = 1
    revision = 1
    updatedAt = "2026-07-23T15:02:50Z"
    sortMode = 0
    items = @()
})
Write-Json (Join-Path $root "ligase-sync.json") ([ordered]@{
    schemaVersion = 1
    library = [ordered]@{
        revision = 1
        updatedAt = "2026-07-23T15:02:50Z"
        sortMode = "nameAscending"
        items = @([ordered]@{
            id = $gameUuid
            kind = "steam"
            name = "Chill with You Lo-Fi Story"
            steamAppId = 3548580
            system = $false
            publishedToClients = $true
            addedAt = "2026-07-23T15:02:50Z"
            updatedAt = "2026-07-23T15:02:50Z"
        })
    }
    streaming = [ordered]@{
        schemaVersion = 1
        revision = 1
        updatedAt = "2026-07-23T15:02:50Z"
        globalResolution = [ordered]@{ width = 1600; height = 900 }
        apps = @{}
    }
})
$apps = [ordered]@{
    version = 2
    env = @{}
    apps = @([ordered]@{
        uuid = $gameUuid
        name = "Chill with You Lo-Fi Story"
        cmd = "cmd /c exit 0"
        "working-dir" = "C:\Windows"
        "wait-all" = $true
        "auto-detach" = $false
    })
}
Write-Json (Join-Path $configRoot "apps.json") $apps

$configuration = @(
    "port = $Port"
    "address_family = both"
    "upnp = disabled"
    "system_tray = disabled"
    "enable_discovery = disabled"
    "sunshine_name = Ligase Authority P1"
    "file_apps = $(Join-Path $configRoot 'apps.json')"
    "file_state = $(Join-Path $configRoot 'state.json')"
    "credentials_file = $(Join-Path $configRoot 'credentials.json')"
    "pkey = $(Join-Path $configRoot 'cakey.pem')"
    "cert = $(Join-Path $configRoot 'cacert.pem')"
    "log_path = $(Join-Path $configRoot 'sunshine.log')"
) -join [Environment]::NewLine
$configurationPath = Join-Path $configRoot "authority.conf"
[IO.File]::WriteAllText($configurationPath, $configuration, $utf8)

$process = Start-Process `
    -FilePath $Binary `
    -ArgumentList "`"$configurationPath`"" `
    -WorkingDirectory (Split-Path $Binary) `
    -WindowStyle Hidden `
    -PassThru
try {
    Start-Sleep -Seconds 2
    $body = @{ token = $token } | ConvertTo-Json -Compress
    $readback = Invoke-AuthorityPost "/ligase/v1/authority/readback" $body
    if ($readback.authorityToken -ne $token -or
        $readback.startNonce -ne $nonce -or
        $readback.rootFingerprint -ne $fingerprint -or
        -not ($readback.apps | Where-Object uuid -EQ $gameUuid)) {
        throw "Initial core read-back did not match the managed projection."
    }

    $appsPath = Join-Path $configRoot "apps.json"
    [IO.File]::WriteAllText($appsPath, "{malformed", $utf8)
    Assert-ReloadFailurePreservesOldState "catalogMalformed" "appCatalogParse" $body

    Write-Json $appsPath $apps
    $held = [IO.File]::Open($appsPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        Assert-ReloadFailurePreservesOldState "catalogUnreadable" "appCatalogLoad" $body
    }
    finally {
        $held.Dispose()
    }

    Write-Json $appsPath ([ordered]@{
        version = 2
        env = @{}
        apps = @([ordered]@{ name = "missing UUID" })
    })
    Assert-ReloadFailurePreservesOldState "catalogLoadFailed" "appCatalogLoad" $body

    $apps.apps += [ordered]@{
        uuid = $fixtureUuid
        name = "Authority fixture"
        cmd = "cmd /c exit 0"
        "working-dir" = "C:\Windows"
        "wait-all" = $true
        "auto-detach" = $false
    }
    Write-Json $appsPath $apps
    $reloaded = Invoke-AuthorityPost "/ligase/v1/authority/reload" $body
    if (-not ($reloaded.apps | Where-Object uuid -EQ $fixtureUuid)) {
        throw "Core reload did not expose the new UUID."
    }

    [pscustomobject]@{
        RunId = $runId
        ProcessId = $process.Id
        HostUniqueId = $readback.hostUniqueId
        InitialGameUuid = $gameUuid
        InitialAppId = ($readback.apps | Where-Object uuid -EQ $gameUuid).appId
        ReloadedFixture = $true
        MalformedRejected = $true
        UnreadableRejected = $true
        LoadFailureRejected = $true
        Root = $root
    }
}
finally {
    if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
        Stop-Process -Id $process.Id
        Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue
    }
    $httpClient.Dispose()
    $httpHandler.Dispose()
}
