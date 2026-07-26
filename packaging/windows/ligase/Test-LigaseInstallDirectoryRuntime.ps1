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
if (Test-Path -LiteralPath $root) {
  if (@(Get-ChildItem -LiteralPath $root -Force).Count -ne 0) {
    throw "harnessOutputRootNotClean"
  }
} else {
  New-Item -ItemType Directory -Path $root | Out-Null
}
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
$managementScript = Join-Path $PSScriptRoot "Manage-LigaseInstallation.ps1"

function New-TestShortcut(
  [string]$Path,
  [string]$Target,
  [string]$WorkingDirectory,
  [string]$Arguments = ""
) {
  New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force |
    Out-Null
  $shell = New-Object -ComObject WScript.Shell
  try {
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Arguments = $Arguments
    $shortcut.Save()
  } finally {
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
  }
}

function Invoke-ShortcutFixture(
  [string]$InstallRoot,
  [string]$CommonPrograms,
  [string]$CommonDesktop,
  [string]$LegacyPrograms,
  [string]$LegacyDesktop,
  [ValidateSet("", "write", "readback")]
  [string]$FailureStage = "",
  [string]$TransactionRoot = "",
  [switch]$ConfigureFirewall,
  [switch]$ValidateTransaction,
  [switch]$DesktopSelected
) {
  $arguments = @(
    "-NoProfile",
    "-NonInteractive",
    "-ExecutionPolicy", "Bypass",
    "-File", $managementScript,
    "-Action", $(if ($ValidateTransaction) {
      "ValidateInstallTransaction"
    } else {
      "ReconcileShortcuts"
    }),
    "-InstallDirectory", $InstallRoot,
    "-ShortcutCommonProgramsRoot", $CommonPrograms,
    "-ShortcutCommonDesktopRoot", $CommonDesktop,
    "-ShortcutLegacyProgramsRoot", $LegacyPrograms,
    "-ShortcutLegacyDesktopRoot", $LegacyDesktop)
  if (-not [string]::IsNullOrWhiteSpace($TransactionRoot)) {
    $arguments += @("-InstallTransactionRoot", $TransactionRoot)
  }
  if ($ConfigureFirewall) { $arguments += "-ConfigureFirewall" }
  if ($DesktopSelected) { $arguments += "-DesktopShortcutSelected" }
  $previous = $env:LIGASE_INSTALL_VALIDATION_HARNESS
  $previousFailure = $env:LIGASE_SHORTCUT_FAILURE_STAGE
  try {
    $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
    $env:LIGASE_SHORTCUT_FAILURE_STAGE = $FailureStage
    $output = @(& powershell.exe @arguments 2>&1)
    return [ordered]@{
      exitCode = $LASTEXITCODE
      output = if ($output.Count -gt 0) { [string]$output[-1] } else { "" }
    }
  } finally {
    $env:LIGASE_INSTALL_VALIDATION_HARNESS = $previous
    $env:LIGASE_SHORTCUT_FAILURE_STAGE = $previousFailure
  }
}

function Get-ShortcutFixtureSnapshot([string[]]$Paths) {
  $result = [ordered]@{}
  foreach ($path in $Paths) {
    $result[$path] = if (Test-Path -LiteralPath $path -PathType Leaf) {
      [Convert]::ToBase64String([IO.File]::ReadAllBytes($path))
    } else { $null }
  }
  return ($result | ConvertTo-Json -Compress)
}

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

public static class LigaseReadinessProbe
{
    public static System.Threading.Thread ScheduleAppend(
        string path,
        int delayMilliseconds,
        string value)
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(delayMilliseconds);
            System.IO.File.AppendAllText(
                path,
                value,
                new System.Text.UTF8Encoding(false));
        });
        thread.IsBackground = true;
        thread.Start();
        return thread;
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

function Read-ClosedTextLines([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    return [ordered]@{ closed = $false; lines = @(); identity = "" }
  }
  try {
    $stream = [IO.File]::Open(
      $Path,
      [IO.FileMode]::Open,
      [IO.FileAccess]::Read,
      [IO.FileShare]::None)
    try {
      $raw = [byte[]]::new($stream.Length)
      $offset = 0
      while ($offset -lt $raw.Length) {
        $read = $stream.Read($raw, $offset, $raw.Length - $offset)
        if ($read -eq 0) { throw [IO.EndOfStreamException]::new() }
        $offset += $read
      }
      $hasher = [Security.Cryptography.SHA256]::Create()
      try {
        $contentHash = [BitConverter]::ToString(
          $hasher.ComputeHash($raw)).Replace("-", "")
      } finally {
        $hasher.Dispose()
      }
      [void]$stream.Seek(0, [IO.SeekOrigin]::Begin)
      $reader = [IO.StreamReader]::new(
        $stream,
        [Text.Encoding]::Default,
        $true)
      try {
        $lines = @()
        while (-not $reader.EndOfStream) {
          $lines += $reader.ReadLine()
        }
        return [ordered]@{
          closed = $true
          lines = @($lines)
          identity = "$($raw.Length):$contentHash"
        }
      } finally {
        $reader.Dispose()
      }
    } finally {
      $stream.Dispose()
    }
  } catch [IO.IOException] {
    return [ordered]@{ closed = $false; lines = @(); identity = "" }
  }
}

function Read-SharedTextLines([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    return @()
  }
  try {
    $stream = [IO.File]::Open(
      $Path,
      [IO.FileMode]::Open,
      [IO.FileAccess]::Read,
      [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
      $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::Default, $true)
      try {
        $lines = @()
        while (-not $reader.EndOfStream) {
          $lines += $reader.ReadLine()
        }
        return @($lines)
      } finally {
        $reader.Dispose()
      }
    } finally {
      $stream.Dispose()
    }
  } catch [IO.IOException] {
    return @()
  }
}

function Read-ClosedResultState([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    return [ordered]@{
      state = "absent"
      closed = $true
      lines = @()
      identity = "absent"
    }
  }
  $snapshot = Read-ClosedTextLines $Path
  if (-not $snapshot.closed) {
    return [ordered]@{
      state = "busy"
      closed = $false
      lines = @()
      identity = "busy"
    }
  }
  return [ordered]@{
    state = "present"
    closed = $true
    lines = @($snapshot.lines)
    identity = "present:$($snapshot.identity)"
  }
}

function Wait-HarnessReadiness(
  [string]$DiagnosticPath,
  [string]$ResultPath,
  [bool]$ShouldSucceed,
  [int]$NativeExitCode,
  [int]$TimeoutMilliseconds = 20000,
  [int]$QuietMilliseconds = 250
) {
  $timer = [Diagnostics.Stopwatch]::StartNew()
  $diagnosticLines = @()
  $resultLines = @()
  $resultClosed = $false
  $progressCount = 0
  $diagnosticClosed = $false
  $readySnapshot = $null
  $resultState = "absent"
  do {
    $progressLines = @(Read-SharedTextLines $DiagnosticPath)
    $progressCount = @($progressLines).Count
    $expectedCountReached = if ($ShouldSucceed) {
      @($progressLines).Count -ge 4
    } else {
      @($progressLines).Count -ge 1
    }
    if ($expectedCountReached) {
      $closedDiagnostic = Read-ClosedTextLines $DiagnosticPath
      $closedResult = Read-ClosedResultState $ResultPath
      $resultState = $closedResult.state
      $closedCountReached = if ($ShouldSucceed) {
        @($closedDiagnostic.lines).Count -ge 4
      } else {
        @($closedDiagnostic.lines).Count -ge 1
      }
      $diagnosticClosed = $closedDiagnostic.closed
      $resultClosed = $closedResult.closed
      $resultStateAllowed = if ($ShouldSucceed) {
        $closedResult.state -ceq "present"
      } else {
        $closedResult.state -ceq "absent"
      }
      if ($closedDiagnostic.closed -and
          $closedResult.closed -and
          $resultStateAllowed -and
          $closedCountReached) {
        $firstFingerprint =
          "$($closedDiagnostic.identity)|$($closedResult.identity)"
        Start-Sleep -Milliseconds $QuietMilliseconds
        $secondDiagnostic = Read-ClosedTextLines $DiagnosticPath
        $secondResult = Read-ClosedResultState $ResultPath
        $secondResultStateAllowed = if ($ShouldSucceed) {
          $secondResult.state -ceq "present"
        } else {
          $secondResult.state -ceq "absent"
        }
        if ($secondDiagnostic.closed -and
            $secondResult.closed -and
            $secondResultStateAllowed) {
          $diagnosticLines = @($secondDiagnostic.lines)
          $resultLines = @($secondResult.lines)
          $secondFingerprint =
            "$($secondDiagnostic.identity)|$($secondResult.identity)"
          if ([string]::Equals(
              [string]$secondFingerprint,
              [string]$firstFingerprint,
              [StringComparison]::Ordinal)) {
          $readySnapshot = [ordered]@{
            timedOut = $false
            diagnosticLines = @($diagnosticLines)
            resultLines = @($resultLines)
            resultClosed = $resultClosed
            resultState = $secondResult.state
            elapsedMilliseconds = $timer.ElapsedMilliseconds
          }
          break
          }
        }
      }
    }
    if ($null -ne $readySnapshot) { break }
    Start-Sleep -Milliseconds 50
  } while ($timer.ElapsedMilliseconds -lt $TimeoutMilliseconds)
  if ($null -ne $readySnapshot) { return $readySnapshot }
  $resultExists = Test-Path -LiteralPath $ResultPath -PathType Leaf
  return [ordered]@{
    timedOut = $true
    diagnosticLines = @($diagnosticLines)
    resultLines = @($resultLines)
    progressCount = $progressCount
    resultClosed = $resultClosed
    resultState = $resultState
    elapsedMilliseconds = $timer.ElapsedMilliseconds
    safeDiagnostic = "phase=readiness;count=$progressCount;" +
      "nativeExit=$NativeExitCode;resultExists=" +
      $resultExists.ToString().ToLowerInvariant() +
      ";diagnosticClosed=$($diagnosticClosed.ToString().ToLowerInvariant())" +
      ";resultClosed=$($resultClosed.ToString().ToLowerInvariant())"
  }
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
  [string] $ExpectedDataRootMode = "",
  [string] $ExpectedDataRootSource = "",
  [int] $ExpectedNativeExitCode = -1,
  [string[]] $ForbiddenPaths = @()
) {
  $caseRoot = Join-Path $root (
    "cases\$Name-$([Guid]::NewGuid().ToString('N'))")
  if (Test-Path -LiteralPath $caseRoot) {
    throw "harnessCaseRootAlreadyExists:$Name"
  }
  New-Item -ItemType Directory -Path $caseRoot | Out-Null
  $result = Join-Path $caseRoot "result.txt"
  if (Test-Path -LiteralPath $harnessDiagnostic) {
    Remove-Item -LiteralPath $harnessDiagnostic -Force
  }
  $hasTestOperator = @($Arguments | Where-Object {
    $_.StartsWith("/TestOperatorLocalAppData=", [StringComparison]::OrdinalIgnoreCase)
  }).Count -gt 0
  $nativeArguments = @($Arguments)
  if (-not $hasTestOperator) {
    $defaultOperatorLocal = Join-Path $caseRoot "operator-local"
    New-Item -ItemType Directory -Path $defaultOperatorLocal | Out-Null
    $nativeArguments += "/TestOperatorLocalAppData=$defaultOperatorLocal"
  }
  $nativeArguments += "/ResultFile=$result"
  $nativeExitCode = 255
  switch ($LaunchMode) {
    "PowerShellDirect" {
      & $harness @nativeArguments
      $nativeExitCode = $LASTEXITCODE
    }
    "PowerShellStartProcess" {
      $serialized = ($nativeArguments |
        ForEach-Object { ConvertTo-WindowsCommandLineArgument $_ }) -join " "
      $process = Start-Process -FilePath $harness `
        -ArgumentList $serialized -PassThru -Wait -WindowStyle Hidden
      $nativeExitCode = $process.ExitCode
    }
    "PowerShellStartProcessUnsafe" {
      $process = Start-Process -FilePath $harness `
        -ArgumentList $nativeArguments -PassThru -Wait -WindowStyle Hidden
      $nativeExitCode = $process.ExitCode
    }
    "ProcessStartInfo" {
      & $DotNet $argumentListRunner $harness @nativeArguments
      $nativeExitCode = $LASTEXITCODE
      if ($LASTEXITCODE -notin 0,12,13,14,15,16,17,18) {
        throw "argumentListRunnerFailed:$LASTEXITCODE"
      }
    }
    "RawWin32" {
      $allArguments = @($harness) + $nativeArguments
      $commandLine = ($allArguments |
        ForEach-Object { ConvertTo-WindowsCommandLineArgument $_ }) -join " "
      $nativeExitCode = [LigaseRawProcess]::Run($harness, $commandLine)
    }
  }
  $readiness = Wait-HarnessReadiness `
    -DiagnosticPath $harnessDiagnostic `
    -ResultPath $result `
    -ShouldSucceed $ShouldSucceed `
    -NativeExitCode $nativeExitCode
  $diagnosticLines = @($readiness.diagnosticLines)
  if ($readiness.timedOut) {
    throw "harnessReadinessTimeout:${Name}:$($readiness.safeDiagnostic)"
  }
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
  $exists = $readiness.resultState -ceq "present"
  if ($ShouldSucceed) {
    if (@($resolverCodes).Count -ne 4 -or
        @($resolverCodes.Where({ $_ -ne 0 })).Count -ne 0 -or
        -not $exists) {
      $detail = if (Test-Path -LiteralPath $harnessDiagnostic) {
        "phase=validation;count=$(@($diagnosticLines).Count);" +
          "nativeExit=$nativeExitCode;resultExists=$($exists.ToString().ToLowerInvariant())"
      } else {
        "phase=validation;count=0;nativeExit=$nativeExitCode;resultExists=false"
      }
      throw "harnessPositiveFailed:${Name}:$detail"
    }
    $actual = @($readiness.resultLines)
    if (@($actual).Count -ne 4 -or
        -not $actual[0].Equals(
          $ExpectedInstallDirectory,
          [StringComparison]::OrdinalIgnoreCase) -or
        (($ExpectedDataRootPattern.Length -eq 0 -and
          -not $actual[1].Equals(
            $ExpectedDataRoot,
            [StringComparison]::OrdinalIgnoreCase)) -or
         ($ExpectedDataRootPattern.Length -gt 0 -and
          $actual[1] -notmatch $ExpectedDataRootPattern)) -or
        ($ExpectedDataRootMode.Length -gt 0 -and
          $actual[2] -cne $ExpectedDataRootMode) -or
        ($ExpectedDataRootSource.Length -gt 0 -and
          -not $actual[3].Equals(
            $ExpectedDataRootSource,
            [StringComparison]::OrdinalIgnoreCase))) {
      throw "harnessResultMismatch:$Name"
    }
  } elseif (@($resolverCodes).Count -lt 1 -or
      $resolverCodes[0] -eq 0 -or
      $exists) {
    throw "harnessNegativeAccepted:$Name"
  }
  if ($ExpectedNativeExitCode -ge 0 -and
      $nativeExitCode -ne $ExpectedNativeExitCode) {
    throw "harnessNativeExitMismatch:${Name}:$nativeExitCode"
  }
  foreach ($forbiddenPath in $ForbiddenPaths) {
    if (Test-Path -LiteralPath $forbiddenPath) {
      throw "harnessNegativeWroteTarget:$Name"
    }
  }
  [ordered]@{
    name = $Name
    exitCode = $exitCode
    nativeExitCode = $nativeExitCode
    resultCreated = $exists
  }
}

$readinessNegativeRoot = Join-Path $root "readiness-negative-self-test"
New-Item -ItemType Directory -Path $readinessNegativeRoot | Out-Null
$readinessNegativeDiagnostic = Join-Path $readinessNegativeRoot "diagnostic.txt"
$readinessNegativeResult = Join-Path $readinessNegativeRoot "result.txt"
[IO.File]::WriteAllLines(
  $readinessNegativeDiagnostic,
  @("one", "two", "three"),
  [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines(
  $readinessNegativeResult,
  @("one", "two", "three", "four"),
  [Text.UTF8Encoding]::new($false))
$readinessNegative = Wait-HarnessReadiness `
  -DiagnosticPath $readinessNegativeDiagnostic `
  -ResultPath $readinessNegativeResult `
  -ShouldSucceed $true `
  -NativeExitCode 0 `
  -TimeoutMilliseconds 250
if (-not $readinessNegative.timedOut -or
    $readinessNegative.progressCount -ne 3) {
  throw "harnessReadinessNegativeAccepted"
}

$readinessLateWriterRoot = Join-Path $root "readiness-late-writer-self-test"
New-Item -ItemType Directory -Path $readinessLateWriterRoot | Out-Null
$readinessLateDiagnostic = Join-Path $readinessLateWriterRoot "diagnostic.txt"
$readinessLateResult = Join-Path $readinessLateWriterRoot "result.txt"
[IO.File]::WriteAllLines(
  $readinessLateDiagnostic,
  @("one", "two", "three", "four"),
  [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines(
  $readinessLateResult,
  @("one", "two", "three", "four"),
  [Text.UTF8Encoding]::new($false))
$lateWriter = [LigaseReadinessProbe]::ScheduleAppend(
  $readinessLateDiagnostic,
  100,
  "five`n")
$readinessLate = Wait-HarnessReadiness `
  -DiagnosticPath $readinessLateDiagnostic `
  -ResultPath $readinessLateResult `
  -ShouldSucceed $true `
  -NativeExitCode 0 `
  -TimeoutMilliseconds 2000
$lateWriter.Join()
if ($readinessLate.timedOut -or
    @($readinessLate.diagnosticLines).Count -ne 5) {
  throw "harnessReadinessLateWriterAccepted"
}

$readinessLateResultRoot = Join-Path $root "readiness-late-result-self-test"
New-Item -ItemType Directory -Path $readinessLateResultRoot | Out-Null
$readinessLateResultDiagnostic = Join-Path $readinessLateResultRoot "diagnostic.txt"
$readinessLateResultPath = Join-Path $readinessLateResultRoot "result.txt"
[IO.File]::WriteAllLines(
  $readinessLateResultDiagnostic,
  @("negative"),
  [Text.UTF8Encoding]::new($false))
$lateResultWriter = [LigaseReadinessProbe]::ScheduleAppend(
  $readinessLateResultPath,
  100,
  "unexpected`n")
$readinessLateResult = Wait-HarnessReadiness `
  -DiagnosticPath $readinessLateResultDiagnostic `
  -ResultPath $readinessLateResultPath `
  -ShouldSucceed $false `
  -NativeExitCode 18 `
  -TimeoutMilliseconds 500
$lateResultWriter.Join()
if (-not $readinessLateResult.timedOut -or
    $readinessLateResult.resultState -cne "present") {
  throw "harnessReadinessLateResultAccepted"
}

$readinessAbsentRoot = Join-Path $root "readiness-absent-result-self-test"
New-Item -ItemType Directory -Path $readinessAbsentRoot | Out-Null
$readinessAbsentDiagnostic = Join-Path $readinessAbsentRoot "diagnostic.txt"
$readinessAbsentResult = Join-Path $readinessAbsentRoot "result.txt"
[IO.File]::WriteAllLines(
  $readinessAbsentDiagnostic,
  @("negative"),
  [Text.UTF8Encoding]::new($false))
$readinessAbsent = Wait-HarnessReadiness `
  -DiagnosticPath $readinessAbsentDiagnostic `
  -ResultPath $readinessAbsentResult `
  -ShouldSucceed $false `
  -NativeExitCode 18 `
  -TimeoutMilliseconds 1000
if ($readinessAbsent.timedOut -or
    $readinessAbsent.resultState -cne "absent") {
  throw "harnessReadinessAbsentResultRejected"
}

$readinessPresentRoot = Join-Path $root "readiness-present-result-self-test"
New-Item -ItemType Directory -Path $readinessPresentRoot | Out-Null
$readinessPresentDiagnostic = Join-Path $readinessPresentRoot "diagnostic.txt"
$readinessPresentResult = Join-Path $readinessPresentRoot "result.txt"
[IO.File]::WriteAllLines(
  $readinessPresentDiagnostic,
  @("negative"),
  [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines(
  $readinessPresentResult,
  @("unexpected"),
  [Text.UTF8Encoding]::new($false))
$readinessPresent = Wait-HarnessReadiness `
  -DiagnosticPath $readinessPresentDiagnostic `
  -ResultPath $readinessPresentResult `
  -ShouldSucceed $false `
  -NativeExitCode 18 `
  -TimeoutMilliseconds 500
if (-not $readinessPresent.timedOut -or
    $readinessPresent.resultState -cne "present") {
  throw "harnessReadinessPresentResultAccepted"
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
  [Parameter(Mandatory)][string] $ExpectedRollback,
  [Parameter(Mandatory)][string] $ExpectedFailedField
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
    $document.failedField -cne $ExpectedFailedField -or
    $document.helper.exitCode -ne 10 -or
    $document.rollback.state -cne $ExpectedRollback -or
    $document.firewall.state -cne "notChecked" -or
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
    failedField = $ExpectedFailedField
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

$orphanOperatorLocal = Join-Path $root "orphan-operator-local"
$orphanInstances = Join-Path $orphanOperatorLocal "Ligase Host\Instances"
New-Item -ItemType Directory -Path $orphanInstances -Force | Out-Null
$results += Invoke-Harness `
  -Name "orphan-zero-candidates-remains-fresh" `
  -Arguments @(
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal") `
  -ShouldSucceed $true `
  -ExpectedInstallDirectory $spaceProgramPath `
  -ExpectedDataRootPattern ('^' +
    [regex]::Escape((Join-Path $env:ProgramData 'Ligase Host\Instances\')) +
    '[0-9a-f-]{36}$') `
  -ExpectedDataRootMode "freshDefault"

$orphanId = "00000000-0000-0000-0000-000000000101"
$orphanSource = Join-Path $orphanInstances $orphanId
New-Item -ItemType Directory -Path $orphanSource -Force | Out-Null
$orphanAcl = Get-Acl -LiteralPath $orphanSource
$orphanAcl.AddAccessRule(
  [Security.AccessControl.FileSystemAccessRule]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent().User,
    [Security.AccessControl.FileSystemRights]::Modify,
    [Security.AccessControl.InheritanceFlags](
      [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
      [Security.AccessControl.InheritanceFlags]::ObjectInherit),
    [Security.AccessControl.PropagationFlags]::None,
    [Security.AccessControl.AccessControlType]::Allow))
Set-Acl -LiteralPath $orphanSource -AclObject $orphanAcl
foreach ($name in @("ligase-authority.json", "library.json", "ligase-sync.json")) {
  [IO.File]::WriteAllText(
    (Join-Path $orphanSource $name),
    "{}",
    [Text.UTF8Encoding]::new($false))
}
$orphanEntriesBeforeSilent = @(
  Get-ChildItem -LiteralPath $orphanInstances -Force -Recurse |
    ForEach-Object { $_.FullName }
)
$results += Invoke-Harness `
  -Name "orphan-one-candidate-silent-without-action-fails-zero-write" `
  -Arguments @(
    "/S",
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal") `
  -ShouldSucceed $false `
  -LaunchMode "PowerShellStartProcess" `
  -ExpectedNativeExitCode 18
$orphanEntriesAfterSilent = @(
  Get-ChildItem -LiteralPath $orphanInstances -Force -Recurse |
    ForEach-Object { $_.FullName }
)
if (Compare-Object $orphanEntriesBeforeSilent $orphanEntriesAfterSilent) {
  throw "orphanSilentWithoutActionMutatedSource"
}
$results += Invoke-Harness `
  -Name "orphan-one-candidate-gui-proposal-has-no-confirmed-action" `
  -Arguments @(
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal") `
  -ShouldSucceed $true `
  -ExpectedInstallDirectory $spaceProgramPath `
  -ExpectedDataRootPattern ('^' +
    [regex]::Escape((Join-Path $env:ProgramData 'Ligase Host\Instances\')) +
    '[0-9a-f-]{36}$') `
  -ExpectedDataRootMode "orphanLegacyRecovery" `
  -ExpectedDataRootSource $orphanSource

$results += Invoke-Harness `
  -Name "orphan-one-candidate-recovery-next-back-next" `
  -Arguments @(
    "/S",
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal",
    "/OrphanLegacyAction=Recover") `
  -ShouldSucceed $true `
  -ExpectedInstallDirectory $spaceProgramPath `
  -ExpectedDataRootPattern ('^' +
    [regex]::Escape((Join-Path $env:ProgramData 'Ligase Host\Instances\')) +
    '[0-9a-f-]{36}$') `
  -ExpectedDataRootMode "orphanLegacyRecovery" `
  -ExpectedDataRootSource $orphanSource `
  -ExpectedNativeExitCode 0

$results += Invoke-Harness `
  -Name "orphan-one-candidate-gui-recover-consumes-confirmed-projection" `
  -Arguments @(
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal",
    "/OrphanLegacyAction=Recover") `
  -ShouldSucceed $true `
  -ExpectedInstallDirectory $spaceProgramPath `
  -ExpectedDataRootPattern ('^' +
    [regex]::Escape((Join-Path $env:ProgramData 'Ligase Host\Instances\')) +
    '[0-9a-f-]{36}$') `
  -ExpectedDataRootMode "orphanLegacyRecovery" `
  -ExpectedDataRootSource $orphanSource `
  -ExpectedNativeExitCode 0

$results += Invoke-Harness `
  -Name "orphan-one-candidate-explicit-fresh-choice" `
  -Arguments @(
    "/S",
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal",
    "/OrphanLegacyAction=CreateFresh") `
  -ShouldSucceed $true `
  -ExpectedInstallDirectory $spaceProgramPath `
  -ExpectedDataRootPattern ('^' +
    [regex]::Escape((Join-Path $env:ProgramData 'Ligase Host\Instances\')) +
    '[0-9a-f-]{36}$') `
  -ExpectedDataRootMode "freshDefault" `
  -ExpectedNativeExitCode 0

$secondOrphan = Join-Path $orphanInstances "00000000-0000-0000-0000-000000000102"
New-Item -ItemType Directory -Path $secondOrphan -Force | Out-Null
$secondAcl = Get-Acl -LiteralPath $secondOrphan
$secondAcl.AddAccessRule(
  [Security.AccessControl.FileSystemAccessRule]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent().User,
    [Security.AccessControl.FileSystemRights]::Modify,
    [Security.AccessControl.InheritanceFlags](
      [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
      [Security.AccessControl.InheritanceFlags]::ObjectInherit),
    [Security.AccessControl.PropagationFlags]::None,
    [Security.AccessControl.AccessControlType]::Allow))
Set-Acl -LiteralPath $secondOrphan -AclObject $secondAcl
foreach ($name in @("ligase-authority.json", "library.json", "ligase-sync.json")) {
  [IO.File]::WriteAllText(
    (Join-Path $secondOrphan $name),
    "{}",
    [Text.UTF8Encoding]::new($false))
}
$results += Invoke-Harness `
  -Name "orphan-multiple-candidates-fail-closed" `
  -Arguments @(
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal") `
  -ShouldSucceed $false

Remove-Item -LiteralPath $secondOrphan -Recurse -Force
[IO.File]::WriteAllText(
  (Join-Path $orphanSource "ligase-sync.json"),
  "{invalid",
  [Text.UTF8Encoding]::new($false))
$results += Invoke-Harness `
  -Name "orphan-malformed-required-json-fail-closed" `
  -Arguments @(
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal") `
  -ShouldSucceed $false

Remove-Item -LiteralPath $orphanSource -Recurse -Force
$orphanReparseTarget = Join-Path $root "orphan-reparse-target"
New-Item -ItemType Directory -Path $orphanReparseTarget -Force | Out-Null
New-Item -ItemType Junction `
  -Path (Join-Path $orphanInstances "00000000-0000-0000-0000-000000000103") `
  -Target $orphanReparseTarget | Out-Null
$results += Invoke-Harness `
  -Name "orphan-reparse-candidate-fail-closed" `
  -Arguments @(
    "/InstallDirectory=$spaceProgramPath",
    "/TestOperatorLocalAppData=$orphanOperatorLocal") `
  -ShouldSucceed $false

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

$shortcutRoot = Join-Path $root "shortcut-fixture"
$shortcutInstall = Join-Path $shortcutRoot "install"
$shortcutCommonPrograms = Join-Path $shortcutRoot "common-programs"
$shortcutCommonDesktop = Join-Path $shortcutRoot "common-desktop"
$shortcutLegacyPrograms = Join-Path $shortcutRoot "legacy-programs"
$shortcutLegacyDesktop = Join-Path $shortcutRoot "legacy-desktop"
New-Item -ItemType Directory -Path $shortcutInstall -Force | Out-Null
$shortcutLauncher = Join-Path $shortcutInstall "Ligase Host.exe"
[IO.File]::WriteAllBytes($shortcutLauncher, [byte[]](0))
$legacyStart = Join-Path $shortcutLegacyPrograms (
  "Ligase Host\Ligase Host.lnk")
$legacyDesktop = Join-Path $shortcutLegacyDesktop "Ligase Host.lnk"
New-TestShortcut $legacyStart $shortcutLauncher $shortcutInstall
New-TestShortcut $legacyDesktop $shortcutLauncher $shortcutInstall
$selectedResult = Invoke-ShortcutFixture `
  -InstallRoot $shortcutInstall `
  -CommonPrograms $shortcutCommonPrograms `
  -CommonDesktop $shortcutCommonDesktop `
  -LegacyPrograms $shortcutLegacyPrograms `
  -LegacyDesktop $shortcutLegacyDesktop `
  -DesktopSelected
if ($selectedResult.exitCode -ne 0 -or
    $selectedResult.output -cne (
      '{"code":"shortcutsReconciled","success":true,"desktopSelected":true}') -or
    (Test-Path -LiteralPath $legacyStart) -or
    (Test-Path -LiteralPath $legacyDesktop) -or
    -not (Test-Path -LiteralPath (
      Join-Path $shortcutCommonPrograms "Ligase Host\Ligase Host.lnk")) -or
    -not (Test-Path -LiteralPath (
      Join-Path $shortcutCommonDesktop "Ligase Host.lnk"))) {
  throw "shortcutSelectedFixtureFailed"
}
$unselectedResult = Invoke-ShortcutFixture `
  -InstallRoot $shortcutInstall `
  -CommonPrograms $shortcutCommonPrograms `
  -CommonDesktop $shortcutCommonDesktop `
  -LegacyPrograms $shortcutLegacyPrograms `
  -LegacyDesktop $shortcutLegacyDesktop
if ($unselectedResult.exitCode -ne 0 -or
    $unselectedResult.output -cne (
      '{"code":"shortcutsReconciled","success":true,"desktopSelected":false}') -or
    (Test-Path -LiteralPath (
      Join-Path $shortcutCommonDesktop "Ligase Host.lnk"))) {
  throw "shortcutUnselectedFixtureFailed"
}
$unownedTarget = Join-Path $shortcutRoot "not-ligase.exe"
[IO.File]::WriteAllBytes($unownedTarget, [byte[]](0))
New-TestShortcut $legacyDesktop $unownedTarget $shortcutRoot "--not-owned"
$preserveResult = Invoke-ShortcutFixture `
  -InstallRoot $shortcutInstall `
  -CommonPrograms $shortcutCommonPrograms `
  -CommonDesktop $shortcutCommonDesktop `
  -LegacyPrograms $shortcutLegacyPrograms `
  -LegacyDesktop $shortcutLegacyDesktop
if ($preserveResult.exitCode -ne 0 -or
    -not (Test-Path -LiteralPath $legacyDesktop)) {
  throw "shortcutNonOwnedPreservationFailed"
}
$commonDesktopShortcut = Join-Path $shortcutCommonDesktop "Ligase Host.lnk"
New-TestShortcut $commonDesktopShortcut $unownedTarget $shortcutRoot "--not-owned"
$conflictResult = Invoke-ShortcutFixture `
  -InstallRoot $shortcutInstall `
  -CommonPrograms $shortcutCommonPrograms `
  -CommonDesktop $shortcutCommonDesktop `
  -LegacyPrograms $shortcutLegacyPrograms `
  -LegacyDesktop $shortcutLegacyDesktop `
  -DesktopSelected
if ($conflictResult.exitCode -eq 0 -or
    -not (Test-Path -LiteralPath $commonDesktopShortcut)) {
  throw "shortcutConflictFailClosedFixtureFailed"
}

$combinationRoot = Join-Path $root "shortcut-combination-fixture"
$combinationInstall = Join-Path $combinationRoot "install"
$combinationCommonPrograms = Join-Path $combinationRoot "common-programs"
$combinationCommonDesktop = Join-Path $combinationRoot "common-desktop"
$combinationLegacyPrograms = Join-Path $combinationRoot "legacy-programs"
$combinationLegacyDesktop = Join-Path $combinationRoot "legacy-desktop"
New-Item -ItemType Directory -Path $combinationInstall -Force | Out-Null
$combinationLauncher = Join-Path $combinationInstall "Ligase Host.exe"
[IO.File]::WriteAllBytes($combinationLauncher, [byte[]](0))
$combinationStart = Join-Path $combinationCommonPrograms (
  "Ligase Host\Ligase Host.lnk")
$combinationDesktop = Join-Path $combinationCommonDesktop "Ligase Host.lnk"
$combinationLegacyStart = Join-Path $combinationLegacyPrograms (
  "Ligase Host\Ligase Host.lnk")
$combinationLegacyDesk = Join-Path $combinationLegacyDesktop "Ligase Host.lnk"
$combinationPaths = @(
  $combinationStart,
  $combinationDesktop,
  $combinationLegacyStart,
  $combinationLegacyDesk)
$firewallSentinel = Join-Path $combinationRoot "owned-firewall-state.txt"
[IO.File]::WriteAllText($firewallSentinel, "unchanged")
New-TestShortcut $combinationLegacyStart $combinationLauncher `
  $combinationInstall
New-TestShortcut $combinationStart $unownedTarget $shortcutRoot "--not-owned"
$beforeStartConflict = Get-ShortcutFixtureSnapshot $combinationPaths
$beforeFirewall = [IO.File]::ReadAllText($firewallSentinel)
$startConflict = Invoke-ShortcutFixture `
  -InstallRoot $combinationInstall `
  -CommonPrograms $combinationCommonPrograms `
  -CommonDesktop $combinationCommonDesktop `
  -LegacyPrograms $combinationLegacyPrograms `
  -LegacyDesktop $combinationLegacyDesktop
if ($startConflict.exitCode -eq 0 -or
    $startConflict.output -notmatch '"code":"startMenuShortcutConflict"' -or
    (Get-ShortcutFixtureSnapshot $combinationPaths) -cne $beforeStartConflict -or
    [IO.File]::ReadAllText($firewallSentinel) -cne $beforeFirewall) {
  throw "shortcutStartConflictTransactionFixtureFailed"
}

Remove-Item -LiteralPath $combinationStart -Force
Remove-Item -LiteralPath $combinationLegacyStart -Force
New-TestShortcut $combinationDesktop $unownedTarget $shortcutRoot "--not-owned"
$beforeDesktopConflict = Get-ShortcutFixtureSnapshot $combinationPaths
$desktopConflict = Invoke-ShortcutFixture `
  -InstallRoot $combinationInstall `
  -CommonPrograms $combinationCommonPrograms `
  -CommonDesktop $combinationCommonDesktop `
  -LegacyPrograms $combinationLegacyPrograms `
  -LegacyDesktop $combinationLegacyDesktop `
  -DesktopSelected
if ($desktopConflict.exitCode -eq 0 -or
    $desktopConflict.output -notmatch '"code":"desktopShortcutConflict"' -or
    (Get-ShortcutFixtureSnapshot $combinationPaths) -cne $beforeDesktopConflict -or
    [IO.File]::ReadAllText($firewallSentinel) -cne $beforeFirewall) {
  throw "shortcutDesktopConflictTransactionFixtureFailed"
}

Remove-Item -LiteralPath $combinationDesktop -Force
$beforeWriteFailure = Get-ShortcutFixtureSnapshot $combinationPaths
$writeFailure = Invoke-ShortcutFixture `
  -InstallRoot $combinationInstall `
  -CommonPrograms $combinationCommonPrograms `
  -CommonDesktop $combinationCommonDesktop `
  -LegacyPrograms $combinationLegacyPrograms `
  -LegacyDesktop $combinationLegacyDesktop `
  -DesktopSelected `
  -FailureStage "write"
if ($writeFailure.exitCode -eq 0 -or
    (Get-ShortcutFixtureSnapshot $combinationPaths) -cne $beforeWriteFailure) {
  throw "shortcutWriteFailureRollbackFixtureFailed"
}
$readbackFailure = Invoke-ShortcutFixture `
  -InstallRoot $combinationInstall `
  -CommonPrograms $combinationCommonPrograms `
  -CommonDesktop $combinationCommonDesktop `
  -LegacyPrograms $combinationLegacyPrograms `
  -LegacyDesktop $combinationLegacyDesktop `
  -DesktopSelected `
  -FailureStage "readback"
if ($readbackFailure.exitCode -eq 0 -or
    (Get-ShortcutFixtureSnapshot $combinationPaths) -cne $beforeWriteFailure) {
  throw "shortcutReadbackFailureRollbackFixtureFailed"
}

$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
$transactionPublish = Join-Path $OutputRoot "transaction-helper"
& $DotNet publish (Join-Path $sourceRoot (
    "tools/Ligase.Installation.TransactionHelper/" +
    "Ligase.Installation.TransactionHelper.csproj")) `
  -c Release -p:Platform=x64 -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:UseSharedCompilation=false `
  -o $transactionPublish | Out-Null
if ($LASTEXITCODE -ne 0) {
  throw "installTransactionHelperPublishFailed"
}
$transactionHelper = Join-Path $transactionPublish (
  "Ligase.Installation.TransactionHelper.exe")
if (-not (Test-Path -LiteralPath $transactionHelper -PathType Leaf)) {
  throw "installTransactionHelperMissing"
}
$boundedInstallRoot = Join-Path $combinationRoot "bounded-helper-install"
$boundedDeployment = Join-Path $boundedInstallRoot "Deployment"
New-Item -ItemType Directory -Path $boundedDeployment -Force | Out-Null
$boundedHelper = Join-Path $boundedDeployment (
  "Ligase.Installation.TransactionHelper.exe")
Copy-Item -LiteralPath $transactionHelper -Destination $boundedHelper
$boundedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $boundedHelper).Hash
$boundedManifest = [ordered]@{
  privilegedHelpers = @([ordered]@{
    relativePath = "Deployment/Ligase.Installation.TransactionHelper.exe"
    signedArtifactSha256 = $boundedHash.ToLowerInvariant()
  })
} | ConvertTo-Json -Depth 4 -Compress
Set-Content -LiteralPath (
  Join-Path $boundedInstallRoot "ligase-install-manifest.json") `
  -Value $boundedManifest -Encoding UTF8 -NoNewline
$boundedBefore = @(Get-ChildItem -LiteralPath $boundedInstallRoot -Recurse -File |
  ForEach-Object {
    [ordered]@{
      relative = $_.FullName.Substring(
        $boundedInstallRoot.TrimEnd('\').Length + 1)
      hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
    }
  } | ConvertTo-Json -Depth 3 -Compress) -join ""
$boundedResults = @()
$boundedSavedErrorAction = $ErrorActionPreference
foreach ($behavior in @(
    "hang", "delayedPipe", "oversizeStdout", "oversizeStderr", "killTree")) {
  $behaviorRoot = Join-Path $combinationRoot ("bounded-" + $behavior)
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_TRANSACTION_TEST_BEHAVIOR = $behavior
  $clock = [Diagnostics.Stopwatch]::StartNew()
  $ErrorActionPreference = "Continue"
  $behaviorOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File $managementScript `
    -Action PreflightInstallTransaction `
    -InstallDirectory $boundedInstallRoot `
    -InstallTransactionRoot $behaviorRoot 2>&1)
  $behaviorExit = $LASTEXITCODE
  $ErrorActionPreference = $boundedSavedErrorAction
  $clock.Stop()
  Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR -ErrorAction SilentlyContinue
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS -ErrorAction SilentlyContinue
  if ($behaviorExit -ne 10 -or $clock.Elapsed.TotalSeconds -gt 20) {
    throw "installTransactionBoundedInvocationFailed"
  }
  $behaviorResult = ($behaviorOutput[-1] | Out-String).Trim() |
    ConvertFrom-Json
  if ([string]$behaviorResult.code -notin @(
      "installTransactionUnavailable", "installTransactionInvalid") -or
      [bool]$behaviorResult.success -or
      [string]$behaviorResult.failedField -cne "installTransaction" -or
      [int]$behaviorResult.transactionHelperNativeExit -ne 18 -or
      [string]$behaviorResult.transactionHelperStage -cne "processTimeout") {
    throw "installTransactionBoundedEvidenceInvalid"
  }
  if (Test-Path -LiteralPath $behaviorRoot) {
    throw "installTransactionBoundedInvocationMutatedJournal"
  }
  $boundedResults += [ordered]@{
    name = "transaction-bounded-$behavior"
    passed = $true
  }
}
foreach ($writeCase in @(
    [ordered]@{
      behavior = "hangBeforeStdinRead"
      expectedExit = 10
      expectedStage = "processTimeout"
      maxSeconds = 20
      rootMayExist = $false
    },
    [ordered]@{
      behavior = "delayedStdinRead"
      expectedExit = 0
      expectedStage = "delete"
      maxSeconds = 5
      rootMayExist = $true
    },
    [ordered]@{
      behavior = "oversizeInput"
      expectedExit = 10
      expectedStage = "inputValidation"
      maxSeconds = 2
      rootMayExist = $false
    })) {
  $writeRoot = Join-Path $combinationRoot (
    "bounded-" + [string]$writeCase.behavior)
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_TRANSACTION_TEST_BEHAVIOR = [string]$writeCase.behavior
  $clock = [Diagnostics.Stopwatch]::StartNew()
  $ErrorActionPreference = "Continue"
  $writeOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File $managementScript `
    -Action PreflightInstallTransaction `
    -InstallDirectory $boundedInstallRoot `
    -InstallTransactionRoot $writeRoot 2>&1)
  $writeExit = $LASTEXITCODE
  $ErrorActionPreference = $boundedSavedErrorAction
  $clock.Stop()
  Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR -ErrorAction SilentlyContinue
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS -ErrorAction SilentlyContinue
  if ($writeExit -ne [int]$writeCase.expectedExit -or
      $clock.Elapsed.TotalSeconds -gt [int]$writeCase.maxSeconds) {
    throw ("installTransactionBoundedWriteInvocationFailed:" +
      [string]$writeCase.behavior + ":exit=" + $writeExit +
      ":seconds=" + [Math]::Round($clock.Elapsed.TotalSeconds, 2))
  }
  $writeResult = ($writeOutput[-1] | Out-String).Trim() | ConvertFrom-Json
  if ($writeExit -eq 0) {
    if (-not [bool]$writeResult.success -or
        [string]$writeResult.code -cne
          "installTransactionPreflightReady") {
      throw "installTransactionBoundedWriteSuccessInvalid"
    }
  } elseif ([bool]$writeResult.success -or
      [string]$writeResult.failedField -cne "installTransaction" -or
      [int]$writeResult.transactionHelperNativeExit -ne 18 -or
      [string]$writeResult.transactionHelperStage -cne
        [string]$writeCase.expectedStage) {
    throw "installTransactionBoundedWriteEvidenceInvalid"
  }
  $pending = Join-Path $writeRoot "pending-install-transaction.json"
  if (Test-Path -LiteralPath $pending) {
    throw "installTransactionBoundedWriteLeftJournal"
  }
  if (-not [bool]$writeCase.rootMayExist -and
      (Test-Path -LiteralPath $writeRoot)) {
    throw "installTransactionBoundedWriteMutatedRoot"
  }
  $boundedResults += [ordered]@{
    name = "transaction-bounded-" + [string]$writeCase.behavior
    passed = $true
  }
}
$boundedAfter = @(Get-ChildItem -LiteralPath $boundedInstallRoot -Recurse -File |
  ForEach-Object {
    [ordered]@{
      relative = $_.FullName.Substring(
        $boundedInstallRoot.TrimEnd('\').Length + 1)
      hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
    }
  } | ConvertTo-Json -Depth 3 -Compress) -join ""
if ($boundedAfter -cne $boundedBefore) {
  throw "installTransactionBoundedInvocationMutatedInstallRoot"
}
$transactionRoot = Join-Path $combinationRoot "admin-transaction"
$env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
$savedErrorAction = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$preflightOutput = @(& $transactionHelper preflight --test-root $transactionRoot 2>&1)
$ErrorActionPreference = $savedErrorAction
$transactionProbeExit = $LASTEXITCODE
Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS -ErrorAction SilentlyContinue
$transactionElevatedAvailable = $transactionProbeExit -eq 0 -and
  ($preflightOutput -join "") -ceq (
    '{"code":"installTransactionPreflightReady","stage":"finalReadback",' +
    '"recoveryAction":"none"}') -and
  -not (Test-Path -LiteralPath (
    Join-Path $transactionRoot "pending-install-transaction.json"))
$emptyRecoveryRoot = Join-Path $combinationRoot "empty-admin-residue"
New-Item -ItemType Directory -Path $emptyRecoveryRoot | Out-Null
$emptyRecoveryAcl = Get-Acl -LiteralPath $emptyRecoveryRoot
$emptyRecoveryAcl.SetOwner(
  [Security.Principal.SecurityIdentifier]::new(
    [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null))
$emptyRecoveryAvailable = $true
try {
  Set-Acl -LiteralPath $emptyRecoveryRoot -AclObject $emptyRecoveryAcl
} catch {
  $emptyRecoveryAvailable = $false
}
if ($emptyRecoveryAvailable) {
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $ErrorActionPreference = "Continue"
  $emptyRecoveryOutput = @(
    & $transactionHelper preflight --test-root $emptyRecoveryRoot 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $emptyRecoveryExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  if ($emptyRecoveryExit -ne 0 -or
      ($emptyRecoveryOutput -join "") -cne (
        '{"code":"installTransactionPreflightReady","stage":"finalReadback",' +
        '"recoveryAction":"recoverEmptyAdminRoot"}')) {
    throw "installTransactionEmptyAdminRootRecoveryFailed"
  }
}
$concurrentRecoveryResults = @()
if ($emptyRecoveryAvailable) {
  $busyRoot = Join-Path $combinationRoot "race-open-handle"
  New-Item -ItemType Directory -Path $busyRoot | Out-Null
  $busyAcl = Get-Acl -LiteralPath $busyRoot
  $busyAcl.SetOwner(
    [Security.Principal.SecurityIdentifier]::new(
      [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null))
  Set-Acl -LiteralPath $busyRoot -AclObject $busyAcl
  $busySddlBefore = (Get-Acl -LiteralPath $busyRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_TRANSACTION_TEST_BEHAVIOR = "holdRecoveryHandle"
  $ErrorActionPreference = "Continue"
  $busyOutput = @(
    & $transactionHelper preflight --test-root $busyRoot 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $busyExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR `
    -ErrorAction SilentlyContinue
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  $busyFailure = (($busyOutput -join "") | ConvertFrom-Json)
  $busySddlAfter = (Get-Acl -LiteralPath $busyRoot).
    GetSecurityDescriptorSddlForm(
      [Security.AccessControl.AccessControlSections]::All)
  if ($busyExit -ne 18 -or
      [string]$busyFailure.code -cne "installTransactionUnavailable" -or
      [string]$busyFailure.stage -cne "openSegment" -or
      [string]$busyFailure.nativeCategory -cne "busy" -or
      [int]$busyFailure.nativeCode -ne 32 -or
      [bool]$busyFailure.aclMutationOccurred -or
      [string]$busyFailure.aclRollback -cne "notRequired" -or
      $busySddlAfter -cne $busySddlBefore -or
      @(Get-ChildItem -LiteralPath $busyRoot -Force).Count -ne 0) {
    throw "installTransactionExclusiveOpenBusyGateInvalid"
  }
  $concurrentRecoveryResults += [ordered]@{
    name = "transaction-existing-open-handle-busy-zero-acl-mutation"
    passed = $true
  }
  foreach ($behavior in @("injectResidueChild", "injectResidueAds")) {
    $raceRoot = Join-Path $combinationRoot ("race-" + $behavior)
    New-Item -ItemType Directory -Path $raceRoot | Out-Null
    $raceAcl = Get-Acl -LiteralPath $raceRoot
    $raceAcl.SetOwner(
      [Security.Principal.SecurityIdentifier]::new(
        [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid,
        $null))
    Set-Acl -LiteralPath $raceRoot -AclObject $raceAcl
    $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
    $env:LIGASE_TRANSACTION_TEST_BEHAVIOR = $behavior
    $ErrorActionPreference = "Continue"
    $raceOutput = @(
      & $transactionHelper preflight --test-root $raceRoot 2>&1)
    $ErrorActionPreference = $savedErrorAction
    $raceExit = $LASTEXITCODE
    Remove-Item Env:\LIGASE_TRANSACTION_TEST_BEHAVIOR `
      -ErrorAction SilentlyContinue
    Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
      -ErrorAction SilentlyContinue
    $inserted = if ($behavior -eq "injectResidueChild") {
      Test-Path -LiteralPath (Join-Path $raceRoot "injected.bin")
    } else {
      $streams = @(Get-Item -LiteralPath $raceRoot -Stream * `
        -ErrorAction SilentlyContinue)
      @($streams | Where-Object { $_.Stream -ne ':$DATA' }).Count -ne 0
    }
    if ($raceExit -eq 0) {
      if ($inserted -or ($raceOutput -join "") -cne (
          '{"code":"installTransactionPreflightReady",' +
          '"stage":"finalReadback",' +
          '"recoveryAction":"recoverEmptyAdminRoot"}')) {
        throw "installTransactionConcurrentResidueAccepted"
      }
    } elseif ($raceExit -ne 18 -or -not $inserted) {
      throw "installTransactionConcurrentResidueGateInvalid"
    }
    $concurrentRecoveryResults += [ordered]@{
      name = "transaction-$behavior-exclusive-gate"
      passed = $true
    }
  }
}
$nonEmptyRecoveryRoot = Join-Path $combinationRoot "nonempty-admin-residue"
New-Item -ItemType Directory -Path $nonEmptyRecoveryRoot | Out-Null
$nonEmptyAcl = Get-Acl -LiteralPath $nonEmptyRecoveryRoot
$nonEmptyAcl.SetOwner(
  [Security.Principal.SecurityIdentifier]::new(
    [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null))
Set-Content -LiteralPath (Join-Path $nonEmptyRecoveryRoot "unknown.bin") `
  -Value "unchanged" -NoNewline
$nonEmptyBefore = (Get-FileHash -LiteralPath (
  Join-Path $nonEmptyRecoveryRoot "unknown.bin") -Algorithm SHA256).Hash
if ($emptyRecoveryAvailable) {
  Set-Acl -LiteralPath $nonEmptyRecoveryRoot -AclObject $nonEmptyAcl
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $ErrorActionPreference = "Continue"
  $null = @(
    & $transactionHelper preflight --test-root $nonEmptyRecoveryRoot 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $nonEmptyExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  if ($nonEmptyExit -ne 18 -or
      (Get-FileHash -LiteralPath (
        Join-Path $nonEmptyRecoveryRoot "unknown.bin") -Algorithm SHA256).Hash `
        -cne $nonEmptyBefore) {
    throw "installTransactionNonemptyAdminRootAccepted"
  }
}
$wrongOwnerRoot = Join-Path $combinationRoot "wrong-owner-admin-residue"
New-Item -ItemType Directory -Path $wrongOwnerRoot | Out-Null
$env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
$ErrorActionPreference = "Continue"
$null = @(& $transactionHelper preflight --test-root $wrongOwnerRoot 2>&1)
$ErrorActionPreference = $savedErrorAction
$wrongOwnerExit = $LASTEXITCODE
Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS -ErrorAction SilentlyContinue
if ($wrongOwnerExit -ne 18 -or
    @(Get-ChildItem -LiteralPath $wrongOwnerRoot -Force).Count -ne 0) {
  throw "installTransactionWrongOwnerAdminRootAccepted"
}
$adsRecoveryRoot = Join-Path $combinationRoot "ads-admin-residue"
New-Item -ItemType Directory -Path $adsRecoveryRoot | Out-Null
$adsAcl = Get-Acl -LiteralPath $adsRecoveryRoot
$adsAcl.SetOwner(
  [Security.Principal.SecurityIdentifier]::new(
    [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null))
Set-Content -LiteralPath "${adsRecoveryRoot}:unknown" `
  -Value "unchanged" -NoNewline
if ($emptyRecoveryAvailable) {
  Set-Acl -LiteralPath $adsRecoveryRoot -AclObject $adsAcl
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $ErrorActionPreference = "Continue"
  $null = @(& $transactionHelper preflight --test-root $adsRecoveryRoot 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $adsExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
  if ($adsExit -ne 18 -or
      (Get-Content -LiteralPath "${adsRecoveryRoot}:unknown" -Raw) -cne
        "unchanged") {
    throw "installTransactionAdsAdminRootAccepted"
  }
}
$stageDiagnostics = @()
foreach ($stage in @(
    "resolveProgramData", "rejectReparse", "createSegment", "openSegment",
    "applyAcl", "assertAcl", "createTemp", "atomicReplace",
    "finalReadback", "read", "delete")) {
  $stageRoot = Join-Path $combinationRoot ("stage-" + $stage)
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  $env:LIGASE_TRANSACTION_FAILURE_STAGE = $stage
  $ErrorActionPreference = "Continue"
  $stageOutput = @(
    & $transactionHelper preflight --test-root $stageRoot 2>&1)
  $ErrorActionPreference = $savedErrorAction
  $stageExit = $LASTEXITCODE
  Remove-Item Env:\LIGASE_TRANSACTION_FAILURE_STAGE -ErrorAction SilentlyContinue
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS -ErrorAction SilentlyContinue
  $stageRaw = $stageOutput -join ""
  $expectedStage = (
    '{"code":"installTransactionUnavailable","stage":"' + $stage +
    '","nativeCategory":"none","nativeCode":0,' +
    '"aclMutationOccurred":false,"aclRollback":"notRequired"}')
  $exactStage = $stageExit -eq 18 -and $stageRaw -ceq $expectedStage
  $nonElevatedOpen = (
    '{"code":"installTransactionUnavailable","stage":"openSegment",' +
    '"nativeCategory":"none","nativeCode":0,' +
    '"aclMutationOccurred":false,"aclRollback":"notRequired"}')
  $nonElevatedOwner = (
    '{"code":"installTransactionAclInvalid","stage":"createSegment",' +
    '"nativeCategory":"invalidOwner","nativeCode":1307,' +
    '"aclMutationOccurred":false,"aclRollback":"notRequired"}')
  $blockedByNonElevatedAcl = $stageExit -eq 18 -and
    $stageRaw -in @($nonElevatedOpen, $nonElevatedOwner)
  if (-not $exactStage -and -not $blockedByNonElevatedAcl) {
    throw "installTransactionStageDiagnosticFixtureFailed"
  }
  if ($stage -in @("resolveProgramData", "rejectReparse", "createSegment") -and
      (Test-Path -LiteralPath $stageRoot)) {
    throw "installTransactionStageDiagnosticMutatedEarly"
  }
  $stageDiagnostics += [ordered]@{
    name = "transaction-stage-$stage"
    passed = $exactStage
    inconclusive = -not $exactStage
  }
}
$junctionRoot = Join-Path $combinationRoot "transaction-junction"
$junctionTarget = Join-Path $combinationRoot "outside-target"
New-Item -ItemType Directory -Path $junctionTarget -Force | Out-Null
$sentinel = Join-Path $junctionTarget "sentinel.txt"
[IO.File]::WriteAllText(
  $sentinel, "unchanged", [Text.UTF8Encoding]::new($false))
$junctionCreated = $false
try {
  New-Item -ItemType Junction -Path $junctionRoot -Target $junctionTarget `
    -ErrorAction Stop | Out-Null
  $junctionCreated = $true
} catch {}
$junctionRejected = $false
if ($junctionCreated) {
  $env:LIGASE_INSTALL_VALIDATION_HARNESS = "1"
  "{}" | & $transactionHelper write --test-root $junctionRoot | Out-Null
  $junctionRejected = $LASTEXITCODE -ne 0 -and
    [IO.File]::ReadAllText($sentinel) -ceq "unchanged" -and
    -not (Test-Path -LiteralPath (
      Join-Path $junctionTarget "pending-install-transaction.json"))
  Remove-Item Env:\LIGASE_INSTALL_VALIDATION_HARNESS `
    -ErrorAction SilentlyContinue
}
if ($junctionCreated -and -not $junctionRejected) {
  throw "installTransactionJunctionFollowed"
}
$shortcutResults = @(
  $boundedResults
  $stageDiagnostics
  [ordered]@{ name = "current-to-all-owned-selected"; passed = $true },
  [ordered]@{ name = "all-users-desktop-unselected"; passed = $true },
  [ordered]@{ name = "nonowned-current-preserved"; passed = $true },
  [ordered]@{ name = "nonowned-common-conflict-fail-closed"; passed = $true },
  [ordered]@{ name = "legacy-owned-start-conflict-zero-mutation"; passed = $true },
  [ordered]@{ name = "desktop-conflict-zero-mutation"; passed = $true },
  [ordered]@{ name = "shortcut-write-failure-exact-rollback"; passed = $true },
  [ordered]@{ name = "shortcut-readback-failure-exact-rollback"; passed = $true },
  [ordered]@{
    name = "admin-only-handle-transaction-boundary"
    passed = $transactionElevatedAvailable
    inconclusive = -not $transactionElevatedAvailable
  },
  [ordered]@{
    name = "transaction-junction-rejected-zero-external-mutation"
    passed = $junctionRejected
    inconclusive = -not $junctionCreated
  },
  [ordered]@{
    name = "transaction-empty-admin-root-recovered-by-file-identity"
    passed = $emptyRecoveryAvailable
    inconclusive = -not $emptyRecoveryAvailable
  },
  [ordered]@{
    name = "transaction-nonempty-admin-root-rejected-without-mutation"
    passed = $emptyRecoveryAvailable
    inconclusive = -not $emptyRecoveryAvailable
  },
  [ordered]@{
    name = "transaction-wrong-owner-admin-root-rejected"
    passed = $true
  },
  [ordered]@{
    name = "transaction-ads-admin-root-rejected"
    passed = $emptyRecoveryAvailable
    inconclusive = -not $emptyRecoveryAvailable
  }
  $concurrentRecoveryResults)

$failureFlowResults = @(
  Invoke-FailureFlowHarness `
    -FailureMode "helperFailure" `
    -ExpectedCode "installationIntegrationFailed" `
    -ExpectedRollback "completed" `
    -ExpectedFailedField "artifacts"
  Invoke-FailureFlowHarness `
    -FailureMode "migrationFailure" `
    -ExpectedCode "dataRootMigrationReadbackFailed" `
    -ExpectedRollback "completed" `
    -ExpectedFailedField "dataRoot"
  Invoke-FailureFlowHarness `
    -FailureMode "integrationFailure" `
    -ExpectedCode "installationFinalReadbackFailed" `
    -ExpectedRollback "completed" `
    -ExpectedFailedField "startMenu"
  Invoke-FailureFlowHarness `
    -FailureMode "rollbackFailure" `
    -ExpectedCode "installationActionFailed" `
    -ExpectedRollback "failed" `
    -ExpectedFailedField "dataRoot"
  Invoke-FailureFlowHarness `
    -FailureMode "silentProvisional" `
    -ExpectedCode "installationFinalReadbackRequired" `
    -ExpectedRollback "notRequired" `
    -ExpectedFailedField "none"
)

[ordered]@{
  code = "installDirectoryRuntimeHarnessPassed"
  cases = $results
  shortcutCases = $shortcutResults
  failureFlows = $failureFlowResults
} | ConvertTo-Json -Depth 4 -Compress
$global:LASTEXITCODE = 0
