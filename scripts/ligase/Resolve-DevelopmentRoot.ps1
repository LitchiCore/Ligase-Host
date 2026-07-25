Set-StrictMode -Version Latest

function Resolve-LigaseBuildRoot {
    param([AllowEmptyString()] [string]$ExplicitRoot)

    $candidate = if (-not [string]::IsNullOrWhiteSpace($ExplicitRoot)) {
        $ExplicitRoot
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:LIGASE_BUILD_ROOT)) {
        $env:LIGASE_BUILD_ROOT
    }
    else {
        Join-Path $env:LOCALAPPDATA "LigaseBuild"
    }

    return [IO.Path]::GetFullPath(
        [Environment]::ExpandEnvironmentVariables($candidate))
}

function Resolve-LigaseTempRoot {
    param(
        [AllowEmptyString()] [string]$ExplicitRoot,
        [Parameter(Mandatory)] [string]$BuildRoot
    )

    $candidate = if (-not [string]::IsNullOrWhiteSpace($ExplicitRoot)) {
        $ExplicitRoot
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:LIGASE_TEMP_ROOT)) {
        $env:LIGASE_TEMP_ROOT
    }
    else {
        Join-Path $BuildRoot "temp"
    }

    return [IO.Path]::GetFullPath(
        [Environment]::ExpandEnvironmentVariables($candidate))
}
