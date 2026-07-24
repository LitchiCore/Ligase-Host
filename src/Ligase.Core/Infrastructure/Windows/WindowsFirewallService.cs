using System.Diagnostics;
using System.Text.Json;
using Ligase.Host.Core.Application.WindowsFirewall;
using Ligase.Host.Core.Domain.WindowsFirewall;

namespace Ligase.Host.Core.Infrastructure.Windows;

public enum FirewallOperation
{
    Readback,
    Apply,
    Remove,
    DryRun
}

public sealed class WindowsFirewallService(
    WindowsFirewallPlanner planner,
    string manifestFile,
    string deploymentScript,
    IFirewallCommandExecutor? executor = null)
{
    private readonly IFirewallCommandExecutor _executor =
        executor ?? new PowerShellFirewallCommandExecutor();

    public async Task<FirewallOperationResult> ExecuteAsync(
        FirewallOperation operation,
        string managedSunshineExecutable,
        ushort basePort,
        CancellationToken cancellationToken = default)
    {
        FirewallPlan plan;
        try
        {
            plan = planner.Build(
                planner.LoadManifest(manifestFile),
                managedSunshineExecutable,
                basePort);
        }
        catch (FirewallPlanException exception)
        {
            return new FirewallOperationResult(
                exception.Code,
                false,
                false);
        }

        var result = await _executor.ExecuteAsync(
            new FirewallCommand(
                operation,
                Path.GetFullPath(deploymentScript),
                Path.GetFullPath(manifestFile),
                plan.Program,
                basePort),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            return new FirewallOperationResult(
                result.RequiresElevation
                    ? FirewallOutcomeCodes.RequiresElevation
                    : FirewallOutcomeCodes.CommandFailed,
                false,
                result.RequiresElevation,
                result.MachineDetail);
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var code = document.RootElement.GetProperty("code").GetString()
                ?? FirewallOutcomeCodes.CommandFailed;
            var configured = document.RootElement.TryGetProperty(
                    "configured",
                    out var configuredNode) &&
                configuredNode.ValueKind == JsonValueKind.True;
            return new FirewallOperationResult(code, configured, false);
        }
        catch (JsonException)
        {
            return new FirewallOperationResult(
                FirewallOutcomeCodes.CommandFailed,
                false,
                false,
                "invalidCommandResponse");
        }
    }
}

public sealed record FirewallCommand(
    FirewallOperation Operation,
    string Script,
    string Manifest,
    string Program,
    ushort BasePort);

public sealed record FirewallCommandResult(
    int ExitCode,
    string StandardOutput,
    bool RequiresElevation,
    string? MachineDetail = null);

public interface IFirewallCommandExecutor
{
    Task<FirewallCommandResult> ExecuteAsync(
        FirewallCommand command,
        CancellationToken cancellationToken);
}

internal sealed class PowerShellFirewallCommandExecutor : IFirewallCommandExecutor
{
    public async Task<FirewallCommandResult> ExecuteAsync(
        FirewallCommand command,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(command.Script);
        start.ArgumentList.Add("-Action");
        start.ArgumentList.Add(command.Operation.ToString());
        start.ArgumentList.Add("-Manifest");
        start.ArgumentList.Add(command.Manifest);
        start.ArgumentList.Add("-Program");
        start.ArgumentList.Add(command.Program);
        start.ArgumentList.Add("-BasePort");
        start.ArgumentList.Add(command.BasePort.ToString());

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("firewallCommandStartFailed");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stderr = await error;
        return new FirewallCommandResult(
            process.ExitCode,
            await output,
            stderr.Contains("requiresElevation", StringComparison.Ordinal),
            string.IsNullOrWhiteSpace(stderr) ? null : "firewallCommandFailed");
    }
}
