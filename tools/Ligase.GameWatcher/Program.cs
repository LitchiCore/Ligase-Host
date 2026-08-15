using Ligase.Host.Core.Services;

if (Environment.GetEnvironmentVariable("LIGASE_SHUTDOWN_VALIDATION_HARNESS") == "1" &&
    TryReadShutdownValidationOwner(args, out var ownerProcessId))
{
    try
    {
        using var owner = System.Diagnostics.Process.GetProcessById(ownerProcessId);
        await owner.WaitForExitAsync();
        return 0;
    }
    catch (ArgumentException)
    {
        return 0;
    }
}

if (!TryReadArguments(args, out var appId, out var installPath))
{
    Console.Error.WriteLine("Usage: Ligase.GameWatcher --steam-app-id <id> --install-path <path>");
    return 64;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    var monitor = new SteamGameProcessMonitor();
    return await monitor.LaunchAndWaitAsync(appId, installPath, cancellationToken: cancellation.Token);
}
catch (OperationCanceledException)
{
    return 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 70;
}

static bool TryReadArguments(string[] args, out uint appId, out string installPath)
{
    appId = 0;
    installPath = string.Empty;
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (args[index] == "--steam-app-id")
        {
            _ = uint.TryParse(args[++index], out appId);
        }
        else if (args[index] == "--install-path")
        {
            installPath = args[++index];
        }
    }

    return appId > 0 && !string.IsNullOrWhiteSpace(installPath);
}

static bool TryReadShutdownValidationOwner(string[] args, out int processId)
{
    processId = 0;
    return args.Length == 2 &&
        args[0] == "--shutdown-validation-owner-pid" &&
        int.TryParse(args[1], out processId) &&
        processId > 0;
}
