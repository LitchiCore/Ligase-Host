$ErrorActionPreference = "Stop"

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class LigaseCommandLine
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string commandLine,
        out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static string[] Parse(string raw)
    {
        int count;
        var pointer = CommandLineToArgvW(raw, out count);
        if (pointer == IntPtr.Zero)
            throw new InvalidOperationException("installArgumentsInvalid");
        try
        {
            if (count < 1)
                throw new InvalidOperationException("installArgumentsInvalid");
            var result = new string[count - 1];
            for (var index = 1; index < count; index++)
            {
                var value = Marshal.ReadIntPtr(pointer, index * IntPtr.Size);
                var argument = Marshal.PtrToStringUni(value);
                if (argument == null)
                    throw new InvalidOperationException("installArgumentsInvalid");
                result[index - 1] = argument;
            }
            return result;
        }
        finally
        {
            LocalFree(pointer);
        }
    }
}
"@

function Fail([int] $ExitCode) {
  [Console]::Error.Write("installerArgumentsInvalid")
  exit $ExitCode
}

function Resolve-LocalPath([string] $Candidate) {
  if ([string]::IsNullOrWhiteSpace($Candidate)) { Fail 14 }
  if ($Candidate -notmatch '^[A-Za-z]:\\') { Fail 15 }
  if ($Candidate.StartsWith("\\", [StringComparison]::Ordinal) -or
      $Candidate.StartsWith("\\?\", [StringComparison]::Ordinal) -or
      $Candidate.StartsWith("\\.\", [StringComparison]::Ordinal)) {
    Fail 15
  }

  $full = [IO.Path]::GetFullPath($Candidate)
  $root = [IO.Path]::GetPathRoot($full)
  if ([string]::IsNullOrWhiteSpace($root) -or
      $full.TrimEnd('\') -eq $root.TrimEnd('\')) {
    Fail 16
  }
  if (-not $Candidate.Equals($full, [StringComparison]::OrdinalIgnoreCase)) {
    Fail 17
  }
  return $full
}

function Read-Option(
  [string[]] $Arguments,
  [string] $Name
) {
  $prefix = "/$Name="
  $matches = @()
  foreach ($argument in $Arguments) {
    if ($argument.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
      $matches += $argument.Substring($prefix.Length)
    } elseif ($argument.Equals(
        "/$Name",
        [StringComparison]::OrdinalIgnoreCase) -or
      $argument.StartsWith(
        "/$Name",
        [StringComparison]::OrdinalIgnoreCase)) {
      Fail 12
    }
  }
  if ($matches.Count -gt 1) { Fail 13 }
  return $matches
}

function Assert-KnownArguments([string[]] $Arguments) {
  foreach ($argument in $Arguments) {
    if ($argument.StartsWith(
        "/InstallDirectory=",
        [StringComparison]::OrdinalIgnoreCase) -or
      $argument.StartsWith(
        "/DataRoot=",
        [StringComparison]::OrdinalIgnoreCase) -or
      $argument.StartsWith(
        "/ResultFile=",
        [StringComparison]::OrdinalIgnoreCase) -or
      $argument.Equals("/S", [StringComparison]::OrdinalIgnoreCase) -or
      $argument.Equals("/NCRC", [StringComparison]::OrdinalIgnoreCase) -or
      $argument.StartsWith("/D=", [StringComparison]::OrdinalIgnoreCase) -or
      $argument.StartsWith("_?=", [StringComparison]::OrdinalIgnoreCase)) {
      continue
    }
    Fail 12
  }
}

try {
  $raw = [Environment]::GetEnvironmentVariable(
    "LIGASE_INSTALL_RAW_PARAMETERS",
    [EnvironmentVariableTarget]::Process)
  $registered = [Environment]::GetEnvironmentVariable(
    "LIGASE_INSTALL_REGISTERED_LOCATION",
    [EnvironmentVariableTarget]::Process)
  $default = [Environment]::GetEnvironmentVariable(
    "LIGASE_INSTALL_DEFAULT_LOCATION",
    [EnvironmentVariableTarget]::Process)
  $resultPath = [Environment]::GetEnvironmentVariable(
    "LIGASE_INSTALL_ARGUMENT_RESULT",
    [EnvironmentVariableTarget]::Process)
  if ([string]::IsNullOrWhiteSpace($raw)) { $raw = "" }
  if ([string]::IsNullOrWhiteSpace($resultPath)) { Fail 18 }

  $arguments = [LigaseCommandLine]::Parse($raw)
  Assert-KnownArguments $arguments
  $installOptions = @(Read-Option $arguments "InstallDirectory")
  $dataOptions = @(Read-Option $arguments "DataRoot")

  $installCandidate = if ($installOptions.Count -eq 1) {
    $installOptions[0]
  } elseif (-not [string]::IsNullOrWhiteSpace($registered)) {
    $registered
  } else {
    $default
  }
  $installDirectory = Resolve-LocalPath $installCandidate
  $dataRoot = if ($dataOptions.Count -eq 1) {
    Resolve-LocalPath $dataOptions[0]
  } else {
    ""
  }

  $parent = Split-Path -Parent $resultPath
  if (-not (Test-Path -LiteralPath $parent -PathType Container)) { Fail 18 }
  $pending = "$resultPath.pending"
  [IO.File]::WriteAllLines(
    $pending,
    @(
      $installDirectory,
      $dataRoot,
      ($installOptions.Count -eq 1).ToString().ToLowerInvariant(),
      ($dataOptions.Count -eq 1).ToString().ToLowerInvariant()
    ),
    [Text.Encoding]::Unicode)
  Move-Item -LiteralPath $pending -Destination $resultPath -Force
  [Console]::Out.Write("installerArgumentsResolved")
  exit 0
} catch {
  Fail 18
}
