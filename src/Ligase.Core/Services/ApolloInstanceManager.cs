using System.Diagnostics;
using System.Text.Json;

namespace Ligase.Host.Core.Services;

public sealed class ApolloInstanceManager(
    LigasePaths paths,
    ApolloPortAllocator portAllocator)
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private Process? _process;
    private bool _intentionalStop;

    public event Action? StatusChanged;
    public ushort BasePort { get; private set; }
    public IReadOnlyList<int> PortFamily =>
        BasePort == 0 ? [] : ApolloPortAllocator.ExpandPortFamily(BasePort);
    public bool IsRunning => _process is { HasExited: false };
    public int? ProcessId => IsRunning ? _process!.Id : null;
    public bool HasStableProcessIdentity
    {
        get
        {
            if (!IsRunning || _process is null || ProcessStartedAt is null) return false;
            try
            {
                return _process.StartTime.ToUniversalTime() == ProcessStartedAt.Value.UtcDateTime;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
    public DateTimeOffset? ProcessStartedAt { get; private set; }
    public string StartNonce { get; private set; } = string.Empty;
    public string? ExpectedUniqueId { get; internal set; }
    public string AuthorityToken { get; } = Convert.ToHexString(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public string DataRoot => paths.RootDirectory;
    public string RootFingerprint => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                Path.GetFullPath(paths.RootDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar)
                    .ToLowerInvariant()))).ToLowerInvariant();
    public string? ExecutablePath => FindBundledExecutable();
    public string? StartupError { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.ApolloDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.ApolloPrivateKeyFile)!);
        StartNonce = Convert.ToHexString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
            .ToLowerInvariant();
        var authorityJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            token = AuthorityToken,
            startNonce = StartNonce,
            rootFingerprint = RootFingerprint
        });
        var authorityTemporary = paths.AuthorityFile + ".tmp";
        await File.WriteAllTextAsync(
            authorityTemporary,
            authorityJson,
            cancellationToken);
        File.Move(authorityTemporary, paths.AuthorityFile, true);
        BasePort = portAllocator.FindAvailableBasePort();
        if (BasePort != 48989)
            throw new InvalidOperationException(
                "Ligase Host 的标准端口 48989 当前不可用。请关闭占用该端口的其他实例后重试。");

        var configuration = BuildManagedConfiguration(paths, BasePort);
        await File.WriteAllTextAsync(paths.ApolloConfigFile, configuration, cancellationToken);
    }

    internal static string BuildManagedConfiguration(LigasePaths paths, ushort basePort)
    {
        return string.Join(Environment.NewLine,
            "sunshine_name = Ligase Host",
            "system_tray = disabled",
            $"port = {basePort}",
            "address_family = both",
            "upnp = disabled",
            "origin_web_ui_allowed = pc",
            $"file_apps = {paths.ApolloAppsFile}",
            $"file_state = {paths.ApolloStateFile}",
            $"log_path = {paths.ApolloLogFile}",
            $"pkey = {paths.ApolloPrivateKeyFile}",
            $"cert = {paths.ApolloCertificateFile}",
            string.Empty);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning) return;
            DisposeExitedProcess();
            _intentionalStop = false;
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
            _process.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;
            ProcessStartedAt = _process.StartTime.ToUniversalTime();
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
        finally
        {
            _lifecycleGate.Release();
            StatusChanged?.Invoke();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (!IsRunning || _process is null)
            {
                DisposeExitedProcess();
                return;
            }

            _intentionalStop = true;
            _process.Kill(true);
            await _process.WaitForExitAsync();
            DisposeExitedProcess();
            StartupError = null;
        }
        finally
        {
            _lifecycleGate.Release();
            StatusChanged?.Invoke();
        }
    }

    private void OnProcessExited(object? sender, EventArgs eventArgs)
    {
        if (sender is not Process exited || !ReferenceEquals(exited, _process)) return;
        if (!_intentionalStop)
        {
            try
            {
                StartupError =
                    $"串流核心意外退出（代码 {exited.ExitCode}）。请点击“重新启动”；如果仍失败，请关闭其他 Ligase Host 实例。";
            }
            catch (InvalidOperationException)
            {
                StartupError =
                    "串流核心意外退出。请点击“重新启动”；如果仍失败，请关闭其他 Ligase Host 实例。";
            }
        }

        StatusChanged?.Invoke();
    }

    private void DisposeExitedProcess()
    {
        if (_process is not null)
        {
            _process.Exited -= OnProcessExited;
            _process.Dispose();
            _process = null;
        }

        ProcessStartedAt = null;
        StartNonce = string.Empty;
        ExpectedUniqueId = null;
        BasePort = 0;
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
