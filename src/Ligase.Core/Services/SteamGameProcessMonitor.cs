using System.Diagnostics;

namespace Ligase.Host.Core.Services;

public sealed class SteamGameProcessMonitor
{
    public async Task<int> LaunchAndWaitAsync(
        uint appId,
        string installPath,
        TimeSpan? launchTimeout = null,
        CancellationToken cancellationToken = default)
    {
        installPath = Path.GetFullPath(installPath)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        Process.Start(new ProcessStartInfo
        {
            FileName = $"steam://rungameid/{appId}",
            UseShellExecute = true
        });

        var deadline = DateTimeOffset.UtcNow + (launchTimeout ?? TimeSpan.FromMinutes(2));
        var hasObservedGame = false;
        var emptySamples = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var running = FindProcessesInside(installPath);
            if (running.Count > 0)
            {
                hasObservedGame = true;
                emptySamples = 0;
            }
            else if (hasObservedGame && ++emptySamples >= 3)
            {
                return 0;
            }
            else if (!hasObservedGame && DateTimeOffset.UtcNow >= deadline)
            {
                return 2;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return 1;
    }

    internal static IReadOnlyList<int> FindProcessesInside(string installPath)
    {
        var matches = new List<int>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var executablePath = process.MainModule?.FileName;
                if (executablePath is not null &&
                    executablePath.StartsWith(installPath, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(process.Id);
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Protected and short-lived processes are expected during a system-wide scan.
            }
            finally
            {
                process.Dispose();
            }
        }

        return matches;
    }
}
