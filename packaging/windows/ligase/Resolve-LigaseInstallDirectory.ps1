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
        var pointer = CommandLineToArgvW("ligase-installer.exe " + raw, out count);
        if (pointer == IntPtr.Zero)
            throw new InvalidOperationException("installArgumentsInvalid");
        try
        {
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
  [Console]::Error.Write("installDirectoryInvalid")
  exit $ExitCode
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

  $explicit = @()
  if ($null -eq $raw) { $raw = "" }
  foreach ($argument in [LigaseCommandLine]::Parse($raw)) {
    if ($argument.StartsWith(
        "/InstallDirectory=",
        [StringComparison]::OrdinalIgnoreCase)) {
      $explicit += $argument.Substring($argument.IndexOf("=") + 1)
    } elseif ($argument.Equals(
        "/InstallDirectory",
        [StringComparison]::OrdinalIgnoreCase) -or
      $argument.StartsWith(
        "/InstallDirectory",
        [StringComparison]::OrdinalIgnoreCase)) {
      Fail 12
    }
  }
  if ($explicit.Count -gt 1) { Fail 13 }

  $candidate = if ($explicit.Count -eq 1) {
    $explicit[0]
  } elseif (-not [string]::IsNullOrWhiteSpace($registered)) {
    $registered
  } else {
    $default
  }
  if ([string]::IsNullOrWhiteSpace($candidate)) { Fail 14 }
  if ($candidate -notmatch '^[A-Za-z]:\\') { Fail 15 }
  if ($candidate.StartsWith("\\", [StringComparison]::Ordinal) -or
      $candidate.StartsWith("\\?\", [StringComparison]::Ordinal)) {
    Fail 15
  }

  $full = [IO.Path]::GetFullPath($candidate)
  $root = [IO.Path]::GetPathRoot($full)
  if ([string]::IsNullOrWhiteSpace($root) -or
      $full.TrimEnd('\') -eq $root.TrimEnd('\')) {
    Fail 16
  }
  if (-not $candidate.Equals($full, [StringComparison]::OrdinalIgnoreCase)) {
    Fail 17
  }

  [Console]::Out.Write($full)
  exit 0
} catch {
  Fail 18
}
