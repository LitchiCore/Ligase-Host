[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Readback', 'Apply', 'Remove', 'DryRun')]
    [string]$Action,

    [Parameter(Mandatory)]
    [string]$Manifest,

    [Parameter(Mandatory)]
    [string]$Program,

    [Parameter(Mandatory)]
    [ValidateRange(1029, 65514)]
    [int]$BasePort
)

$ErrorActionPreference = 'Stop'

function Write-MachineResult {
    param(
        [Parameter(Mandatory)][string]$Code,
        [Parameter(Mandatory)][bool]$Configured
    )

    [ordered]@{
        code = $Code
        configured = $Configured
    } | ConvertTo-Json -Compress
}

function Test-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

$manifestPath = [IO.Path]::GetFullPath($Manifest)
$programPath = [IO.Path]::GetFullPath($Program)
$definition = Get-Content -LiteralPath $manifestPath -Raw |
    ConvertFrom-Json

if ($definition.schemaVersion -ne 1 -or
    $definition.ruleGroup -cne 'Ligase Host LAN Access' -or
    $definition.profile -cne 'Private' -or
    $definition.remoteAddress -cne 'LocalSubnet' -or
    [IO.Path]::GetFileName($programPath) -ine 'sunshine.exe') {
    throw 'invalidManifest'
}

$plans = @(
    [ordered]@{
        Name = 'Ligase.Host.Lan.Tcp.v1'
        DisplayName = 'Ligase Host LAN TCP'
        Protocol = 'TCP'
        LocalPort = (($BasePort - 5), $BasePort, ($BasePort + 21))
    },
    [ordered]@{
        Name = 'Ligase.Host.Lan.Udp.v1'
        DisplayName = 'Ligase Host LAN UDP'
        Protocol = 'UDP'
        LocalPort = (($BasePort + 9), ($BasePort + 10), ($BasePort + 11))
    }
)

function Get-OwnedRule {
    param([string]$Name)
    Get-NetFirewallRule -Name $Name -ErrorAction SilentlyContinue
}

function Test-RuleMatches {
    param($Plan)
    $rule = Get-OwnedRule -Name $Plan.Name
    if ($null -eq $rule -or @($rule).Count -ne 1) {
        return $false
    }

    $application = $rule | Get-NetFirewallApplicationFilter
    $ports = $rule | Get-NetFirewallPortFilter
    $addresses = $rule | Get-NetFirewallAddressFilter
    $actualPorts = @($ports.LocalPort) -join ','
    $expectedPorts = @($Plan.LocalPort) -join ','
    return $rule.Enabled -eq 'True' -and
        $rule.Direction -eq 'Inbound' -and
        $rule.Action -eq 'Allow' -and
        $rule.Profile -eq 'Private' -and
        $application.Program -ieq $programPath -and
        $ports.Protocol -eq $Plan.Protocol -and
        $actualPorts -eq $expectedPorts -and
        @($addresses.RemoteAddress).Count -eq 1 -and
        $addresses.RemoteAddress -eq 'LocalSubnet'
}

function Test-AllConfigured {
    foreach ($plan in $plans) {
        if (-not (Test-RuleMatches -Plan $plan)) {
            return $false
        }
    }
    return $true
}

function Get-OwnedSnapshots {
    $snapshots = @()
    foreach ($name in @($definition.ownedRuleNames)) {
        foreach ($rule in @(Get-OwnedRule -Name $name)) {
            if ($null -eq $rule) {
                continue
            }
            $application = $rule | Get-NetFirewallApplicationFilter
            $ports = $rule | Get-NetFirewallPortFilter
            $addresses = $rule | Get-NetFirewallAddressFilter
            $snapshots += [ordered]@{
                Name = $rule.Name
                DisplayName = $rule.DisplayName
                Group = $rule.Group
                Enabled = $rule.Enabled
                Direction = $rule.Direction
                Action = $rule.Action
                Profile = $rule.Profile
                Program = $application.Program
                Protocol = $ports.Protocol
                LocalPort = @($ports.LocalPort)
                RemoteAddress = @($addresses.RemoteAddress)
                EdgeTraversalPolicy = $rule.EdgeTraversalPolicy
            }
        }
    }
    return $snapshots
}

function Restore-OwnedSnapshots {
    param([array]$Snapshots)
    foreach ($snapshot in $Snapshots) {
        New-NetFirewallRule `
            -Name $snapshot.Name `
            -DisplayName $snapshot.DisplayName `
            -Group $snapshot.Group `
            -Enabled $snapshot.Enabled `
            -Direction $snapshot.Direction `
            -Action $snapshot.Action `
            -Profile $snapshot.Profile `
            -Program $snapshot.Program `
            -Protocol $snapshot.Protocol `
            -LocalPort $snapshot.LocalPort `
            -RemoteAddress $snapshot.RemoteAddress `
            -EdgeTraversalPolicy $snapshot.EdgeTraversalPolicy | Out-Null
    }
}

if ($Action -eq 'DryRun') {
    Write-MachineResult -Code 'notConfigured' -Configured $false
    exit 0
}

if ($Action -eq 'Readback') {
    $configured = Test-AllConfigured
    Write-MachineResult `
        -Code $(if ($configured) { 'configured' } else { 'notConfigured' }) `
        -Configured $configured
    exit 0
}

if (-not (Test-Elevated)) {
    [Console]::Error.WriteLine('requiresElevation')
    exit 5
}

$snapshots = @(Get-OwnedSnapshots)

if ($Action -eq 'Remove') {
    try {
        foreach ($name in @($definition.ownedRuleNames)) {
            Get-OwnedRule -Name $name |
                Remove-NetFirewallRule -ErrorAction SilentlyContinue
        }
    } catch {
        Restore-OwnedSnapshots -Snapshots $snapshots
        throw
    }
    Write-MachineResult -Code 'removed' -Configured $false
    exit 0
}

foreach ($name in @($definition.ownedRuleNames)) {
    Get-OwnedRule -Name $name |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue
}

try {
    foreach ($plan in $plans) {
        New-NetFirewallRule `
            -Name $plan.Name `
            -DisplayName $plan.DisplayName `
            -Group $definition.ruleGroup `
            -Enabled True `
            -Direction Inbound `
            -Action Allow `
            -Profile Private `
            -Program $programPath `
            -Protocol $plan.Protocol `
            -LocalPort $plan.LocalPort `
            -RemoteAddress LocalSubnet `
            -EdgeTraversalPolicy Block | Out-Null
    }

    if (-not (Test-AllConfigured)) {
        throw 'readbackMismatch'
    }
} catch {
    foreach ($name in @($definition.ownedRuleNames)) {
        Get-OwnedRule -Name $name |
            Remove-NetFirewallRule -ErrorAction SilentlyContinue
    }
    Restore-OwnedSnapshots -Snapshots $snapshots
    throw
}

Write-MachineResult -Code 'configured' -Configured $true
