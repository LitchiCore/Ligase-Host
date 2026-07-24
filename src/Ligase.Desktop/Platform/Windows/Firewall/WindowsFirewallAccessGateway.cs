using System.ComponentModel;
using Ligase.Host.Core.Application.WindowsFirewall;
using Ligase.Host.Core.Domain.WindowsFirewall;
using Ligase.Host.Core.Infrastructure.Windows;
using Ligase.Host.Core.Services;
using Ligase.Host.Desktop.Presentation.Settings.Firewall;

namespace Ligase.Host.Desktop.Platform.Windows.Firewall;

public sealed class WindowsFirewallAccessGateway(
    WindowsFirewallPlanner planner,
    ApolloInstanceManager managedCore,
    WindowsFirewallEnvironmentReader environmentReader,
    IFirewallElevatedProcessLauncher elevatedProcessLauncher) : IFirewallAccessGateway
{
    public async Task<FirewallAccessReadback> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var environmentTask = ReadEnvironmentSafelyAsync(cancellationToken);
        var result = await ExecuteSafelyAsync(
            FirewallOperation.Readback,
            elevated: false,
            cancellationToken);
        return new FirewallAccessReadback(
            result,
            await environmentTask,
            PortSummary());
    }

    public Task<FirewallOperationResult> ApplyElevatedAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteSafelyAsync(
            FirewallOperation.Apply,
            elevated: true,
            cancellationToken);

    private async Task<FirewallOperationResult> ExecuteSafelyAsync(
        FirewallOperation operation,
        bool elevated,
        CancellationToken cancellationToken)
    {
        var assets = FirewallDeploymentConvention.Resolve(AppContext.BaseDirectory);
        if (!assets.IsAvailable)
            return new FirewallOperationResult(
                FirewallOutcomeCodes.InvalidManifest,
                false,
                false,
                assets.Status.ToString());

        var executable = managedCore.ManagedExecutable;
        if (!executable.IsAvailable)
            return new FirewallOperationResult(
                FirewallOutcomeCodes.InvalidProgram,
                false,
                false,
                executable.Status.ToString());

        try
        {
            var service = new WindowsFirewallService(
                planner,
                assets.ManifestPath!,
                assets.ScriptPath!,
                elevated
                    ? new RunAsFirewallCommandExecutor(elevatedProcessLauncher)
                    : null);
            return await service.ExecuteAsync(
                operation,
                executable.ExecutablePath!,
                managedCore.BasePort,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return new FirewallOperationResult(
                FirewallOutcomeCodes.CommandFailed,
                false,
                false,
                exception.GetType().Name);
        }
    }

    private async Task<FirewallEnvironmentSnapshot?> ReadEnvironmentSafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await environmentReader.ReadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private string PortSummary()
    {
        try
        {
            var assets = FirewallDeploymentConvention.Resolve(AppContext.BaseDirectory);
            var executable = managedCore.ManagedExecutable;
            if (!assets.IsAvailable || !executable.IsAvailable)
                return "端口摘要不可用；未扩大任何防火墙范围。";
            var plan = planner.Build(
                planner.LoadManifest(assets.ManifestPath!),
                executable.ExecutablePath!,
                managedCore.BasePort);
            return string.Join(
                "；",
                plan.Rules.Select(rule =>
                    $"{rule.Transport.ToString().ToUpperInvariant()} {string.Join(", ", rule.LocalPorts)}"));
        }
        catch (FirewallPlanException)
        {
            return "端口摘要不可用；未扩大任何防火墙范围。";
        }
    }
}

public interface IFirewallElevatedProcessLauncher
{
    Task<int> LaunchAsync(
        System.Diagnostics.ProcessStartInfo startInfo,
        CancellationToken cancellationToken);
}

public sealed class SystemFirewallElevatedProcessLauncher : IFirewallElevatedProcessLauncher
{
    public async Task<int> LaunchAsync(
        System.Diagnostics.ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("firewallElevationStartFailed");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}

public sealed class RunAsFirewallCommandExecutor(
    IFirewallElevatedProcessLauncher processLauncher) : IFirewallCommandExecutor
{
    public async Task<FirewallCommandResult> ExecuteAsync(
        FirewallCommand command,
        CancellationToken cancellationToken)
    {
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
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
        try
        {
            var exitCode = await processLauncher.LaunchAsync(start, cancellationToken);
            return new FirewallCommandResult(
                exitCode,
                exitCode == 0
                    ? """{"code":"configured","configured":true}"""
                    : string.Empty,
                exitCode != 0,
                exitCode == 0 ? null : "elevatedHelperFailed");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new FirewallCommandResult(1223, string.Empty, true, "uacCancelled");
        }
    }
}
