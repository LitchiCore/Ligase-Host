using System.Diagnostics;

namespace Ligase.Host.Core.Services;

public sealed class ApolloInstanceManager(
    LigasePaths paths,
    ApolloPortAllocator portAllocator)
{
    private Process? _process;

    public ushort BasePort { get; private set; }
    public IReadOnlyList<int> PortFamily =>
        BasePort == 0 ? [] : ApolloPortAllocator.ExpandPortFamily(BasePort);
    public bool IsRunning => _process is { HasExited: false };
    public string? ExecutablePath => FindBundledExecutable();
    public string? StartupError { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.ApolloDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.ApolloPrivateKeyFile)!);
        BasePort = portAllocator.FindAvailableBasePort();

        var configuration = string.Join(Environment.NewLine,
            "sunshine_name = Ligase Host",
            $"port = {BasePort}",
            "upnp = disabled",
            "origin_web_ui_allowed = pc",
            $"file_apps = {paths.ApolloAppsFile}",
            $"file_state = {paths.ApolloStateFile}",
            $"log_path = {paths.ApolloLogFile}",
            $"pkey = {paths.ApolloPrivateKeyFile}",
            $"cert = {paths.ApolloCertificateFile}",
            string.Empty);
        await File.WriteAllTextAsync(paths.ApolloConfigFile, configuration, cancellationToken);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;
        try
        {
            await InitializeAsync(cancellationToken);
            var executable = ExecutablePath
                ?? throw new FileNotFoundException("Ligase 串流核心尚未随应用提供或从本仓库构建。");

            _process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"\"{paths.ApolloConfigFile}\"",
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Apollo 核心未能启动。");
            await Task.Delay(500, cancellationToken);
            if (_process.HasExited)
            {
                throw new InvalidOperationException($"Apollo 核心启动后立即退出，代码 {_process.ExitCode}。");
            }
            StartupError = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StartupError = exception.Message;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning || _process is null) return;
        _process.Kill(true);
        await _process.WaitForExitAsync();
        _process.Dispose();
        _process = null;
    }

    private static string? FindBundledExecutable()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "Apollo", "sunshine.exe"),
            Path.Combine(baseDirectory, "sunshine.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "build", "sunshine.exe"))
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
