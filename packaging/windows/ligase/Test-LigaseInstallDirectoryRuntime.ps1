[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [string] $MakeNsis,
  [Parameter(Mandatory)]
  [string] $OutputRoot,
  [string] $DotNet = "dotnet.exe"
)

$ErrorActionPreference = "Stop"

$root = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $root -Force | Out-Null
$harness = Join-Path $root "LigaseInstallDirectoryHarness.exe"
$harnessDiagnostic = Join-Path $root "harness-runtime.diagnostic"
$resolver = Join-Path $PSScriptRoot "Resolve-LigaseInstallDirectory.ps1"
& $MakeNsis `
  "/DOutputFile=$harness" `
  "/DResolverScript=$resolver" `
  "/DHarnessDiagnosticPath=$harnessDiagnostic" `
  (Join-Path $PSScriptRoot "LigaseInstallDirectoryHarness.nsi") | Out-Null
if ($LASTEXITCODE -ne 0) { throw "harnessCompileFailed" }

$argumentListRunnerRoot = Join-Path $root "argument-list-runner"
$argumentListRunnerOutput = Join-Path $argumentListRunnerRoot "out"
New-Item -ItemType Directory -Path $argumentListRunnerRoot -Force | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (
  Join-Path $argumentListRunnerRoot "ArgumentListRunner.csproj") -Encoding UTF8
@'
if (args.Length < 1)
    return 90;
var start = new System.Diagnostics.ProcessStartInfo(args[0])
{
    UseShellExecute = false,
    CreateNoWindow = true,
};
foreach (var argument in args.Skip(1))
    start.ArgumentList.Add(argument);
using var process = System.Diagnostics.Process.Start(start);
if (process is null)
    return 91;
await process.WaitForExitAsync();
return process.ExitCode;
'@ | Set-Content -LiteralPath (
  Join-Path $argumentListRunnerRoot "Program.cs") -Encoding UTF8
& $DotNet build (
  Join-Path $argumentListRunnerRoot "ArgumentListRunner.csproj") `
  -c Release -o $argumentListRunnerOutput --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "argumentListRunnerBuildFailed" }
$argumentListRunner = Join-Path $argumentListRunnerOutput "ArgumentListRunner.dll"

Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class LigaseRawProcess
{
    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref STARTUPINFO startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static int Run(string application, string commandLine)
    {
        var startup = new STARTUPINFO();
        startup.cb = Marshal.SizeOf<STARTUPINFO>();
        PROCESS_INFORMATION process;
        if (!CreateProcessW(
            application,
            new StringBuilder(commandLine),
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            0,
            IntPtr.Zero,
            null,
            ref startup,
            out process))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (WaitForSingleObject(process.hProcess, 10000) != 0)
                throw new InvalidOperationException("harnessProcessTimeout");
            uint exitCode;
            if (!GetExitCodeProcess(process.hProcess, out exitCode))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return unchecked((int)exitCode);
        }
        finally
        {
            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
        }
    }
}
"@

function ConvertTo-WindowsCommandLineArgument(
  [Parameter(Mandatory)][AllowEmptyString()][string] $Value
) {
  if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') {
    return $Value
  }
  $builder = [Text.StringBuilder]::new()
  [void]$builder.Append('"')
  $backslashes = 0
  foreach ($character in $Value.ToCharArray()) {
    if ($character -eq '\') {
      $backslashes++
      continue
    }
    if ($character -eq '"') {
      [void]$builder.Append(('\' * (($backslashes * 2) + 1)))
      [void]$builder.Append('"')
      $backslashes = 0
      continue
    }
    [void]$builder.Append(('\' * $backslashes))
    $backslashes = 0
    [void]$builder.Append($character)
  }
  [void]$builder.Append(('\' * ($backslashes * 2)))
  [void]$builder.Append('"')
  return $builder.ToString()
}

function Invoke-Harness(
  [Parameter(Mandatory)][string] $Name,
  [Parameter(Mandatory)][string[]] $Arguments,
  [Parameter(Mandatory)][bool] $ShouldSucceed,
  [ValidateSet(
    "PowerShellDirect",
    "PowerShellStartProcess",
    "PowerShellStartProcessUnsafe",
    "ProcessStartInfo",
    "RawWin32")]
  [string] $LaunchMode = "PowerShellDirect",
  [string] $ExpectedInstallDirectory = "",
  [string] $ExpectedDataRoot = "",
  [string] $ExpectedDataRootPattern = "",
  [string[]] $ForbiddenPaths = @()
) {
  $result = Join-Path $root "$Name.result"
  if (Test-Path -LiteralPath $result) {
    Remove-Item -LiteralPath $result -Force
  }
  if (Test-Path -LiteralPath $harnessDiagnostic) {
    Remove-Item -LiteralPath $harnessDiagnostic -Force
  }
  $nativeArguments = @($Arguments) + "/ResultFile=$result"
  switch ($LaunchMode) {
    "PowerShellDirect" {
      & $harness @nativeArguments
    }
    "PowerShellStartProcess" {
      $serialized = ($nativeArguments |
        ForEach-Object { ConvertTo-WindowsCommandLineArgument $_ }) -join " "
      $process = Start-Process -FilePath $harness `
        -ArgumentList $serialized -PassThru -Wait -WindowStyle Hidden
    }
    "PowerShellStartProcessUnsafe" {
      $process = Start-Process -FilePath $harness `
        -ArgumentList $nativeArguments -PassThru -Wait -WindowStyle Hidden
    }
    "ProcessStartInfo" {
      & $DotNet $argumentListRunner $harness @nativeArguments
      if ($LASTEXITCODE -notin 0,12,13,14,15,16,17,18) {
        throw "argumentListRunnerFailed:$LASTEXITCODE"
      }
    }
    "RawWin32" {
      $allArguments = @($harness) + $nativeArguments
      $commandLine = ($allArguments |
        ForEach-Object { ConvertTo-WindowsCommandLineArgument $_ }) -join " "
      [void][LigaseRawProcess]::Run($harness, $commandLine)
    }
  }
  $deadline = [DateTime]::UtcNow.AddSeconds(5)
  do {
    Start-Sleep -Milliseconds 50
    $diagnosticLines = if (Test-Path -LiteralPath $harnessDiagnostic) {
      @(Get-Content -LiteralPath $harnessDiagnostic)
    } else {
      @()
    }
    $enoughDiagnostics = if ($ShouldSucceed) {
      @($diagnosticLines).Count -ge 4
    } else {
      @($diagnosticLines).Count -ge 1
    }
  } until (
    ([DateTime]::UtcNow -ge $deadline) -or
    ($enoughDiagnostics -and (
      -not $ShouldSucceed -or
      (Test-Path -LiteralPath $result -PathType Leaf))))
  $resolverCodes = @($diagnosticLines | ForEach-Object {
    $parts = $_ -split '\|', 3
    if (@($parts).Count -ge 2 -and $parts[1] -match '^[0-9]+$') {
      [int]$parts[1]
    }
  })
  $exitCode = if (@($resolverCodes).Count -gt 0) {
    $resolverCodes[-1]
  } else {
    255
  }
  $exists = Test-Path -LiteralPath $result -PathType Leaf
  if ($ShouldSucceed) {
    if (@($resolverCodes).Count -ne 4 -or
        @($resolverCodes.Where({ $_ -ne 0 })).Count -ne 0 -or
        -not $exists) {
      $detail = if (Test-Path -LiteralPath $harnessDiagnostic) {
        [IO.File]::ReadAllText($harnessDiagnostic)
      } else {
        "noDiagnostic"
      }
      throw "harnessPositiveFailed:${Name}:$detail"
    }
    $actual = @([IO.File]::ReadAllLines($result))
    if (@($actual).Count -ne 2 -or
        -not $actual[0].Equals(
          $ExpectedInstallDirectory,
          [StringComparison]::OrdinalIgnoreCase) -or
        (($ExpectedDataRootPattern.Length -eq 0 -and
          -not $actual[1].Equals(
            $ExpectedDataRoot,
            [StringComparison]::OrdinalIgnoreCase)) -or
         ($ExpectedDataRootPattern.Length -gt 0 -and
          $actual[1] -notmatch $ExpectedDataRootPattern))) {
      throw "harnessResultMismatch:$Name"
    }
  } elseif (@($resolverCodes).Count -lt 1 -or
      $resolverCodes[0] -eq 0 -or
      $exists) {
    throw "harnessNegativeAccepted:$Name"
  }
  foreach ($forbiddenPath in $ForbiddenPaths) {
    if (Test-Path -LiteralPath $forbiddenPath) {
      throw "harnessNegativeWroteTarget:$Name"
    }
  }
  [ordered]@{
    name = $Name
    exitCode = $exitCode
    resultCreated = $exists
  }
}

function Invoke-FailureFlowHarness(
  [Parameter(Mandatory)]
  [ValidateSet(
    "helperFailure",
    "migrationFailure",
    "integrationFailure",
    "rollbackFailure",
    "silentProvisional")]
  [string] $FailureMode,
  [Parameter(Mandatory)][string] $ExpectedCode,
  [Parameter(Mandatory)][string] $ExpectedRollback
) {
  $caseRoot = Join-Path $root "failure-flow-$FailureMode"
  New-Item -ItemType Directory -Path $caseRoot -Force | Out-Null
  $result = Join-Path $caseRoot "result.txt"
  $evidence = Join-Path $caseRoot "last-outcome.json"
  $nativeArguments = @(
    "/InstallDirectory=$(Join-Path $caseRoot 'program')",
    "/DataRoot=$(Join-Path $caseRoot 'data')",
    "/ResultFile=$result",
    "/EvidenceFile=$evidence",
    "/FailureMode=$FailureMode")
  $serialized = ($nativeArguments |
    ForEach-Object { ConvertTo-WindowsCommandLineArgument $_ }) -join " "
  $process = Start-Process -FilePath $harness `
    -ArgumentList $serialized -PassThru -Wait -WindowStyle Hidden
  $nativeExitCode = $process.ExitCode
  if ($nativeExitCode -eq 0) {
    throw "failureFlowNativeExitWasZero:$FailureMode"
  }
  $deadline = [DateTime]::UtcNow.AddSeconds(5)
  while (
    [DateTime]::UtcNow -lt $deadline -and
    (-not (Test-Path -LiteralPath $result -PathType Leaf) -or
     -not (Test-Path -LiteralPath $evidence -PathType Leaf))
  ) {
    Start-Sleep -Milliseconds 50
  }
  if (-not (Test-Path -LiteralPath $result -PathType Leaf) -or
      -not (Test-Path -LiteralPath $evidence -PathType Leaf)) {
    throw "failureFlowEvidenceMissing:$FailureMode"
  }
  $lines = @([IO.File]::ReadAllLines($result, [Text.Encoding]::Unicode))
  if (@($lines).Count -ne 3 -or
      $lines[0] -cne "failed" -or
      $lines[1] -cne $ExpectedCode -or
      $lines[2] -cne $ExpectedRollback) {
    throw "failureFlowResultMismatch:$FailureMode"
  }
  $document = [IO.File]::ReadAllText(
    $evidence, [Text.Encoding]::Unicode) | ConvertFrom-Json
  if (
    $document.schemaVersion -ne 1 -or
    $document.phase -cne "failed" -or
    $document.success -ne $false -or
    $document.resultCode -cne $ExpectedCode -or
    $document.helper.exitCode -ne 10 -or
    $document.rollback.state -cne $ExpectedRollback -or
    $document.firewall.state -cne "failed" -or
    $document.displayedSuccess -ne $false -or
    $document.displayedFailure -ne $true
  ) {
    throw "failureFlowEvidenceMismatch:$FailureMode"
  }
  if ((Test-Path -LiteralPath (Join-Path $caseRoot "program")) -or
      (Test-Path -LiteralPath (Join-Path $caseRoot "data"))) {
    throw "failureFlowUnexpectedProductWrite:$FailureMode"
  }
  [ordered]@{
    name = "normal-pages-$FailureMode"
    nativeExitCode = $nativeExitCode
    machineFailure = $true
    displayedSuccess = $false
    displayedFailure = $true
    evidenceReadable = $true
    rollback = $ExpectedRollback
  }
}

$chineseProgramPath =
  "D:\" + ([string][char]0x7A0B) + ([char]0x5E8F) +
  ([char]0x6587) + ([char]0x4EF6) + "\Ligase Host"
$chineseDataPath =
  "D:\" + ([string][char]0x4E3B) + ([char]0x673A) +
  ([char]0x6570) + ([char]0x636E) + "\Ligase Host"
$spaceProgramPath = 'D:\Program Files\Ligase Host Harness'
$existingInstall = Join-Path $root "existing-install"
$existingData = Join-Path $root "existing-data"
New-Item -ItemType Directory -Path $existingInstall, $existingData -Force |
  Out-Null
[IO.File]::WriteAllText(
  (Join-Path $existingInstall "ligase-bootstrap.json"),
  (@{ schemaVersion = 1; dataRoot = $existingData } |
    ConvertTo-Json -Compress),
  [Text.UTF8Encoding]::new($false))

$results = @(
  Invoke-Harness `
    -Name "upgrade-rejects-inherited-existing-acl" `
    -Arguments @(
      "/InstallDirectory=$existingInstall",
      "/DataRoot=$existingData") `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "upgrade-rejects-data-root-change" `
    -Arguments @(
      "/InstallDirectory=$existingInstall",
      "/DataRoot=$(Join-Path $root 'different-data')") `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "fresh-session-stable-programdata-uuid-next-back-next" `
    -Arguments @("/InstallDirectory=$spaceProgramPath") `
    -ShouldSucceed $true `
    -ExpectedInstallDirectory $spaceProgramPath `
    -ExpectedDataRootPattern ('^' +
      [regex]::Escape((Join-Path $env:ProgramData 'Ligase Host\Instances\')) +
      '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')
  Invoke-Harness `
    -Name "custom-chinese-path-next-back-next" `
    -Arguments @(
      "/InstallDirectory=$chineseProgramPath",
      "/DataRoot=$chineseDataPath") `
    -ShouldSucceed $true `
    -ExpectedInstallDirectory $chineseProgramPath `
    -ExpectedDataRoot $chineseDataPath
  Invoke-Harness `
    -Name "d-paths-with-spaces-powershell-direct" `
    -Arguments @(
      "/InstallDirectory=$spaceProgramPath",
      '/DataRoot=D:\Development\Ligase Data\Host') `
    -ShouldSucceed $true `
    -ExpectedInstallDirectory $spaceProgramPath `
    -ExpectedDataRoot 'D:\Development\Ligase Data\Host'
  Invoke-Harness `
    -Name "d-paths-with-spaces-start-process" `
    -LaunchMode "PowerShellStartProcess" `
    -Arguments @(
      "/InstallDirectory=$spaceProgramPath",
      '/DataRoot=D:\Development\Ligase Data\Host') `
    -ShouldSucceed $true `
    -ExpectedInstallDirectory $spaceProgramPath `
    -ExpectedDataRoot 'D:\Development\Ligase Data\Host'
  Invoke-Harness `
    -Name "d-paths-with-spaces-process-start-info" `
    -LaunchMode "ProcessStartInfo" `
    -Arguments @(
      "/InstallDirectory=$spaceProgramPath",
      '/DataRoot=D:\Development\Ligase Data\Host') `
    -ShouldSucceed $true `
    -ExpectedInstallDirectory $spaceProgramPath `
    -ExpectedDataRoot 'D:\Development\Ligase Data\Host'
  Invoke-Harness `
    -Name "d-paths-with-spaces-raw-win32" `
    -LaunchMode "RawWin32" `
    -Arguments @(
      "/InstallDirectory=$spaceProgramPath",
      '/DataRoot=D:\Development\Ligase Data\Host') `
    -ShouldSucceed $true `
    -ExpectedInstallDirectory $spaceProgramPath `
    -ExpectedDataRoot 'D:\Development\Ligase Data\Host'
  Invoke-Harness `
    -Name "unsafe-start-process-split-path" `
    -LaunchMode "PowerShellStartProcessUnsafe" `
    -Arguments @(
      "/InstallDirectory=$spaceProgramPath",
      '/DataRoot=D:\Development\Ligase Data\Host') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "malformed-zero-write" `
    -Arguments @(
      "/InstallDirectory=$(Join-Path $root 'must-not-exist-install')",
      "/DataRoot=$(Join-Path $root 'must-not-exist-data')",
      "stray-token") `
    -ShouldSucceed $false `
    -ForbiddenPaths @(
      (Join-Path $root "must-not-exist-install"),
      (Join-Path $root "must-not-exist-data"))
  Invoke-Harness `
    -Name "duplicate-install" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/InstallDirectory=E:\Ligase Host') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "duplicate-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot=D:\Ligase Data',
      '/DataRoot=E:\Ligase Data') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "cross-duplicate" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/InstallDirectory=E:\Ligase Host',
      '/DataRoot=D:\Ligase Data',
      '/DataRoot=E:\Ligase Data') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "malformed-install" `
    -Arguments @('/InstallDirectory') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "malformed-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "empty-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot=') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "relative-install" `
    -Arguments @('/InstallDirectory=relative\Ligase Host') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "relative-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot=relative\Ligase Data') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "drive-root-install" `
    -Arguments @('/InstallDirectory=D:\') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "drive-root-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot=D:\') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "unc-install" `
    -Arguments @('/InstallDirectory=\\server\share\Ligase Host') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "unc-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot=\\server\share\Ligase Data') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "device-install" `
    -Arguments @('/InstallDirectory=\\?\D:\Ligase Host') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "device-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot=\\?\D:\Ligase Data') `
    -ShouldSucceed $false
  Invoke-Harness `
    -Name "noncanonical-data" `
    -Arguments @(
      '/InstallDirectory=D:\Ligase Host',
      '/DataRoot=D:\Development\..\Ligase Data') `
    -ShouldSucceed $false
)

$standardDriftInstall = Join-Path $root "standard-drift-install"
$standardDriftData = Join-Path $root "ProgramDataFixture\Ligase Host\Instances\00000000-0000-0000-0000-000000000001"
New-Item -ItemType Directory -Path $standardDriftInstall, $standardDriftData -Force |
  Out-Null
[IO.File]::WriteAllText(
  (Join-Path $standardDriftInstall "ligase-bootstrap.json"),
  (@{ schemaVersion = 1; dataRoot = $standardDriftData } |
    ConvertTo-Json -Compress),
  [Text.UTF8Encoding]::new($false))
$results += Invoke-Harness `
  -Name "standard-programdata-inherited-acl-drift-rejected" `
  -Arguments @("/InstallDirectory=$standardDriftInstall") `
  -ShouldSucceed $false

$missingInstall = Join-Path $root "missing-existing-install"
New-Item -ItemType Directory -Path $missingInstall -Force | Out-Null
[IO.File]::WriteAllText(
  (Join-Path $missingInstall "ligase-bootstrap.json"),
  (@{
      schemaVersion = 1
      dataRoot = (Join-Path $root "missing-existing-data")
    } | ConvertTo-Json -Compress),
  [Text.UTF8Encoding]::new($false))
$results += Invoke-Harness `
  -Name "missing-existing-data-root-rejected" `
  -Arguments @("/InstallDirectory=$missingInstall") `
  -ShouldSucceed $false

$reparseInstall = Join-Path $root "reparse-existing-install"
$reparseTarget = Join-Path $root "reparse-target"
$reparseData = Join-Path $root "reparse-existing-data"
New-Item -ItemType Directory -Path $reparseInstall, $reparseTarget -Force |
  Out-Null
New-Item -ItemType Junction -Path $reparseData -Target $reparseTarget |
  Out-Null
[IO.File]::WriteAllText(
  (Join-Path $reparseInstall "ligase-bootstrap.json"),
  (@{ schemaVersion = 1; dataRoot = $reparseData } |
    ConvertTo-Json -Compress),
  [Text.UTF8Encoding]::new($false))
$results += Invoke-Harness `
  -Name "reparse-existing-data-root-rejected" `
  -Arguments @("/InstallDirectory=$reparseInstall") `
  -ShouldSucceed $false

$failureFlowResults = @(
  Invoke-FailureFlowHarness `
    -FailureMode "helperFailure" `
    -ExpectedCode "installationIntegrationFailed" `
    -ExpectedRollback "completed"
  Invoke-FailureFlowHarness `
    -FailureMode "migrationFailure" `
    -ExpectedCode "dataRootMigrationReadbackFailed" `
    -ExpectedRollback "completed"
  Invoke-FailureFlowHarness `
    -FailureMode "integrationFailure" `
    -ExpectedCode "installationFinalReadbackFailed" `
    -ExpectedRollback "completed"
  Invoke-FailureFlowHarness `
    -FailureMode "rollbackFailure" `
    -ExpectedCode "installationActionFailed" `
    -ExpectedRollback "failed"
  Invoke-FailureFlowHarness `
    -FailureMode "silentProvisional" `
    -ExpectedCode "installationFinalReadbackRequired" `
    -ExpectedRollback "notRequired"
)

[ordered]@{
  code = "installDirectoryRuntimeHarnessPassed"
  cases = $results
  failureFlows = $failureFlowResults
} | ConvertTo-Json -Depth 4 -Compress
