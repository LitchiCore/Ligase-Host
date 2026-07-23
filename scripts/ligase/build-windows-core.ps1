[CmdletBinding()]
param(
    [string]$BuildRoot = (Join-Path $env:LOCALAPPDATA "LigaseBuild"),
    [int]$Parallel = 8
)

$ErrorActionPreference = "Stop"

$msysRelease = "2026-03-22"
$msysArchiveName = "msys2-base-x86_64-20260322.sfx.exe"
$msysArchiveSha256 = "6fe0cc8154132040e034ff4daface2a4163a9d1f6ebaaa1133394bff460bd5cf"
$nodeVersion = "v24.18.0"
$nodeArchiveName = "node-$nodeVersion-win-x64.zip"
$nodeArchiveSha256 = "0ae68406b42d7725661da979b1403ec9926da205c6770827f33aac9d8f26e821"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$downloads = Join-Path $BuildRoot "downloads"
$msysContainer = Join-Path $BuildRoot "msys64-$($msysRelease.Replace('-', ''))"
$msysRoot = Join-Path $msysContainer "msys64"
$bash = Join-Path $msysRoot "usr\bin\bash.exe"
$nodeRoot = Join-Path $BuildRoot "node\node-$nodeVersion-win-x64"
$nodeExe = Join-Path $nodeRoot "node.exe"

New-Item -ItemType Directory -Force -Path $downloads | Out-Null

function Get-VerifiedDownload {
    param(
        [Parameter(Mandatory)] [string]$Uri,
        [Parameter(Mandatory)] [string]$Destination,
        [Parameter(Mandatory)] [string]$Sha256
    )

    if (-not (Test-Path -LiteralPath $Destination)) {
        Invoke-WebRequest $Uri -OutFile $Destination
    }

    $actual = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
    if ($actual -ne $Sha256) {
        throw "Checksum mismatch for $Destination. Expected $Sha256, got $actual."
    }
}

function Convert-ToMsysPath {
    param([Parameter(Mandatory)] [string]$Path)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    if ($resolved -notmatch "^([A-Za-z]):\\(.*)$") {
        throw "Only absolute Windows drive paths are supported: $resolved"
    }

    return "/$($Matches[1].ToLowerInvariant())/$($Matches[2].Replace('\', '/'))"
}

$msysArchive = Join-Path $downloads $msysArchiveName
Get-VerifiedDownload `
    -Uri "https://github.com/msys2/msys2-installer/releases/download/$msysRelease/$msysArchiveName" `
    -Destination $msysArchive `
    -Sha256 $msysArchiveSha256

if (-not (Test-Path -LiteralPath $bash)) {
    New-Item -ItemType Directory -Force -Path $msysContainer | Out-Null
    & $msysArchive -y "-o$msysContainer" | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $bash)) {
        throw "MSYS2 extraction failed."
    }
}

$env:MSYSTEM = "UCRT64"
$env:CHERE_INVOKING = "1"

# A core-system update can terminate the first MSYS2 shell. Running the update
# again completes the transaction before package installation.
& $bash -lc "pacman -Syu --noconfirm" | Out-Host
& $bash -lc "pacman -Syu --noconfirm" | Out-Host

$packages = @(
    "git",
    "make",
    "mingw-w64-ucrt-x86_64-cmake",
    "mingw-w64-ucrt-x86_64-cppwinrt",
    "mingw-w64-ucrt-x86_64-curl-winssl",
    "mingw-w64-ucrt-x86_64-MinHook",
    "mingw-w64-ucrt-x86_64-miniupnpc",
    "mingw-w64-ucrt-x86_64-nsis",
    "mingw-w64-ucrt-x86_64-onevpl",
    "mingw-w64-ucrt-x86_64-openssl",
    "mingw-w64-ucrt-x86_64-opus",
    "mingw-w64-ucrt-x86_64-toolchain",
    "mingw-w64-ucrt-x86_64-nlohmann_json",
    "mingw-w64-ucrt-x86_64-ninja"
)
& $bash -lc ("pacman -S --needed --noconfirm " + ($packages -join " ")) | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "MSYS2 dependency installation failed."
}

$nodeArchive = Join-Path $downloads $nodeArchiveName
Get-VerifiedDownload `
    -Uri "https://nodejs.org/dist/$nodeVersion/$nodeArchiveName" `
    -Destination $nodeArchive `
    -Sha256 $nodeArchiveSha256

if (-not (Test-Path -LiteralPath $nodeExe)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $nodeRoot) | Out-Null
    Expand-Archive -LiteralPath $nodeArchive -DestinationPath (Split-Path $nodeRoot)
}

$commit = (git -C $repositoryRoot rev-parse --short=8 HEAD).Trim()
$branch = (git -C $repositoryRoot rev-parse --abbrev-ref HEAD).Trim()
$version = "0.0.0.$commit"
$buildDirectory = Join-Path $BuildRoot "build\$commit"
$deployDirectory = Join-Path $BuildRoot "deploy\$commit"
$msysSource = Convert-ToMsysPath $repositoryRoot
$msysBuild = Convert-ToMsysPath $buildDirectory
$msysDeploy = Convert-ToMsysPath $deployDirectory
$nodePath = Convert-ToMsysPath $nodeRoot

$env:BRANCH = $branch
$env:BUILD_VERSION = $version
$env:COMMIT = $commit
$env:PATH = "$nodeRoot;C:\Windows\System32;C:\Windows"

$configure = @"
export PATH="/ucrt64/bin:/usr/bin:${nodePath}:`$PATH"
cmake -S "$msysSource" -B "$msysBuild" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DSUNSHINE_PUBLISHER_NAME=LitchiCore \
  -DSUNSHINE_PUBLISHER_WEBSITE=https://github.com/LitchiCore/Ligase-Host \
  -DSUNSHINE_PUBLISHER_ISSUE_URL=https://github.com/LitchiCore/Ligase-Host/issues
"@
& $bash -lc $configure | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "CMake configuration failed."
}

& $bash -lc "export PATH=`"/ucrt64/bin:/usr/bin:$nodePath`"; cmake --build `"$msysBuild`" --parallel $Parallel" | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Core build failed."
}

& $bash -lc "export PATH=`"/ucrt64/bin:/usr/bin`"; cmake --install `"$msysBuild`" --prefix `"$msysDeploy`"" | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Core installation failed."
}

$binary = Join-Path $deployDirectory "sunshine.exe"
$hash = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
[pscustomobject]@{
    Commit = $commit
    Version = $version
    Binary = $binary
    Sha256 = $hash
    Toolchain = $msysRelease
    Node = $nodeVersion
}
