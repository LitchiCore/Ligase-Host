using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ligase.Host.Desktop.Tests;

[TestClass]
public sealed class DevelopmentRootScriptsTests
{
    private static readonly string RepositoryRoot = ResolveRepositoryRoot();

    [TestMethod]
    public void DevelopmentScriptsUseSharedRootResolver()
    {
        var expected = new[]
        {
            "scripts/ligase/build-windows-core.ps1",
            "scripts/ligase/test-authority-readback.ps1",
            "scripts/ligase/start-ipv6-acceptance-core.ps1",
            "packaging/windows/ligase/Build-LigaseInstaller.ps1",
        };

        foreach (var relativePath in expected)
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot, relativePath));
            StringAssert.Contains(text, "Resolve-DevelopmentRoot.ps1", relativePath);
            Assert.IsFalse(
                text.Contains("$env:LOCALAPPDATA\\LigaseBuild", StringComparison.OrdinalIgnoreCase),
                relativePath);
        }
    }

    [TestMethod]
    public void ExplicitRootOverridesEnvironmentAndDoesNotCreateDirectories()
    {
        var resolver = Path.Combine(
            RepositoryRoot, "scripts", "ligase", "Resolve-DevelopmentRoot.ps1");
        var environmentRoot = Path.Combine(
            Path.GetPathRoot(RepositoryRoot)!, "unused-ligase-build-root");
        var explicitRoot = Path.Combine(
            Path.GetPathRoot(RepositoryRoot)!, "explicit-ligase-build-root");
        var command =
            $". '{EscapePowerShell(resolver)}'; " +
            $"$env:LIGASE_BUILD_ROOT='{EscapePowerShell(environmentRoot)}'; " +
            $"Resolve-LigaseBuildRoot -ExplicitRoot '{EscapePowerShell(explicitRoot)}'";

        var result = RunPowerShell(command);

        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.AreEqual(
            Path.GetFullPath(explicitRoot),
            result.StandardOutput.Trim(),
            ignoreCase: true);
        Assert.IsFalse(Directory.Exists(environmentRoot));
        Assert.IsFalse(Directory.Exists(explicitRoot));
    }

    [TestMethod]
    public void EnvironmentRootAndBuildRelativeTempAreResolvedWithoutMutation()
    {
        var resolver = Path.Combine(
            RepositoryRoot, "scripts", "ligase", "Resolve-DevelopmentRoot.ps1");
        var buildRoot = Path.Combine(
            Path.GetPathRoot(RepositoryRoot)!, "development-ligase-build-root");
        var expectedTemp = Path.Combine(buildRoot, "temp");
        var command =
            $". '{EscapePowerShell(resolver)}'; " +
            $"$env:LIGASE_BUILD_ROOT='{EscapePowerShell(buildRoot)}'; " +
            "Remove-Item Env:LIGASE_TEMP_ROOT -ErrorAction SilentlyContinue; " +
            "$root=Resolve-LigaseBuildRoot; " +
            "$temp=Resolve-LigaseTempRoot -BuildRoot $root; " +
            "Write-Output \"$root`n$temp\"";

        var result = RunPowerShell(command);
        var lines = result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.AreEqual(2, lines.Length);
        Assert.AreEqual(Path.GetFullPath(buildRoot), lines[0], ignoreCase: true);
        Assert.AreEqual(Path.GetFullPath(expectedTemp), lines[1], ignoreCase: true);
        Assert.IsFalse(Directory.Exists(buildRoot));
    }

    private static (int ExitCode, string StandardOutput, string StandardError)
        RunPowerShell(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private static string EscapePowerShell(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private static string ResolveRepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("LIGASE_SOURCE_ROOT");
        return !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..", "..", "..", "..", "..", ".."));
    }
}
