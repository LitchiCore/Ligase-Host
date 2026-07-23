using System.Text.Json;
using Ligase.Host.Core.Models;
using Microsoft.Win32;

namespace Ligase.Host.Core.Services;

public sealed class HostPreferencesService(LigasePaths paths)
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Ligase Host";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public HostPreferences Current { get; private set; } = new();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(paths.PreferencesFile))
            {
                await using var stream = File.OpenRead(paths.PreferencesFile);
                Current = await JsonSerializer.DeserializeAsync<HostPreferences>(
                    stream,
                    JsonOptions,
                    cancellationToken) ?? new HostPreferences();
            }

            // The registry is the effective source of truth for Windows startup.
            Current.StartWithWindows = IsRegisteredForStartup();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetCloseToTrayAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Current.CloseToTray = enabled;
            await WriteCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetStartWithWindowsAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                               ?? throw new InvalidOperationException("无法打开 Windows 启动项注册表。");
            if (enabled)
            {
                runKey.SetValue(
                    RunValueName,
                    BuildStartupCommand(Environment.ProcessPath
                        ?? throw new InvalidOperationException("无法确定 Ligase Host 程序路径。")),
                    RegistryValueKind.String);
            }
            else
            {
                runKey.DeleteValue(RunValueName, throwOnMissingValue: false);
            }

            Current.StartWithWindows = enabled;
            await WriteCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static string BuildStartupCommand(string executablePath) =>
        $"\"{executablePath}\" --minimized";

    private static bool IsRegisteredForStartup()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return runKey?.GetValue(RunValueName) is string value &&
               !string.IsNullOrWhiteSpace(value);
    }

    private async Task WriteCoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.RootDirectory);
        var temporaryPath = paths.PreferencesFile + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                Current,
                JsonOptions,
                cancellationToken);
        }

        File.Move(temporaryPath, paths.PreferencesFile, true);
    }
}
