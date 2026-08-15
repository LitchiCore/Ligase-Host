using System.Diagnostics;
using System.Text.Json;
using Ligase.Host.Core.Application.WindowsFirewall;
using Ligase.Host.Core.Domain.Installation;
using Ligase.Host.Core.Domain.WindowsFirewall;

namespace Ligase.Host.Core.Services;

public sealed record ApolloLifecycleTimeouts(
    TimeSpan GateAcquire,
    TimeSpan GracefulExit,
    TimeSpan Kill,
    TimeSpan PostKillExit)
{
    public static ApolloLifecycleTimeouts Default { get; } = new(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(750),
        TimeSpan.FromMilliseconds(1500));
}

public sealed record ApolloStopOutcome(
    string Code,
    string Stage,
    int? ProcessId,
    long Generation,
    bool ProcessStillAlive,
    string NextAction);

public static class ApolloStopCodes
{
    public const string Stopped = "stopped";
    public const string AlreadyStopped = "alreadyStopped";
    public const string CallerCancelled = "callerCancelled";
    public const string GateTimeout = "gateTimeout";
    public const string KillFailed = "killFailed";
    public const string KillTimeout = "killTimeout";
    public const string PostKillTimeout = "postKillTimeout";
    public const string GracefulSignalUnavailable = "gracefulSignalUnavailable";
    public const string GracefulTimeout = "gracefulTimeout";
}

public static class ApolloStopStages
{
    public const string Observe = "observe";
    public const string Gate = "gate";
    public const string GracefulWait = "gracefulWait";
    public const string Kill = "kill";
    public const string PostKillWait = "postKillWait";
    public const string Complete = "complete";
}

public static class ApolloStopNextActions
{
    public const string None = "none";
    public const string RetryStop = "retryStop";
    public const string WaitForActiveStop = "waitForActiveStop";
}

public sealed class ApolloInstanceManager
{
    private readonly LigasePaths _paths;
    private readonly ApolloPortAllocator _portAllocator;
    private readonly IManagedApolloProcessFactory _processFactory;
    private readonly ApolloLifecycleTimeouts _timeouts;
    private readonly InstallationLayout _installationLayout;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _stopSync = new();
    private IManagedApolloProcess? _process;
    private Task<ApolloStopOutcome>? _activeStop;
    private long _generation;
    private bool _intentionalStop;

    public ApolloInstanceManager(
        LigasePaths paths,
        ApolloPortAllocator portAllocator)
        : this(
            paths,
            portAllocator,
            new SystemManagedApolloProcessFactory(),
            ApolloLifecycleTimeouts.Default,
            InstallationLayoutResolver.ResolveFromDesktopBase(
                AppContext.BaseDirectory))
    {
    }

    public ApolloInstanceManager(
        LigasePaths paths,
        ApolloPortAllocator portAllocator,
        InstallationLayout installationLayout)
        : this(
            paths,
            portAllocator,
            new SystemManagedApolloProcessFactory(),
            ApolloLifecycleTimeouts.Default,
            installationLayout)
    {
    }

    internal ApolloInstanceManager(
        LigasePaths paths,
        ApolloPortAllocator portAllocator,
        IManagedApolloProcessFactory processFactory,
        ApolloLifecycleTimeouts timeouts,
        InstallationLayout? installationLayout = null)
    {
        _paths = paths;
        _portAllocator = portAllocator;
        _processFactory = processFactory;
        _timeouts = timeouts;
        _installationLayout = installationLayout ??
            InstallationLayoutResolver.ResolveFromDesktopBase(
                AppContext.BaseDirectory);
    }

    public event Action? StatusChanged;
    public ushort BasePort { get; private set; }
    public IReadOnlyList<int> PortFamily =>
        BasePort == 0 ? [] : ApolloPortAllocator.ExpandPortFamily(BasePort);
    public bool IsRunning => _process is { HasExited: false };
    public int? ProcessId => IsRunning ? _process!.Id : null;
    public bool HasStableProcessIdentity =>
        IsRunning &&
        _process is not null &&
        ProcessStartedAt is not null &&
        _process.StartedAtUtc == ProcessStartedAt.Value;
    public DateTimeOffset? ProcessStartedAt { get; private set; }
    public long Generation => Interlocked.Read(ref _generation);
    public string StartNonce { get; private set; } = string.Empty;
    public string? ExpectedUniqueId { get; internal set; }
    public string AuthorityToken { get; } = Convert.ToHexString(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public string DataRoot => _paths.RootDirectory;
    public string RootFingerprint => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                Path.GetFullPath(_paths.RootDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar)
                    .ToLowerInvariant()))).ToLowerInvariant();
    public string? ExecutablePath => FindBundledExecutable();
    public ManagedSunshineExecutable ManagedExecutable =>
        ManagedSunshineExecutableValidator.Validate(ExecutablePath);
    public string? StartupError { get; private set; }
    public ApolloStopOutcome? LastStopOutcome { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.ApolloDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.ApolloPrivateKeyFile)!);
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
        var authorityTemporary = _paths.AuthorityFile + ".tmp";
        await File.WriteAllTextAsync(
            authorityTemporary,
            authorityJson,
            cancellationToken);
        File.Move(authorityTemporary, _paths.AuthorityFile, true);
        BasePort = _portAllocator.FindAvailableBasePort();
        if (BasePort != 48989)
            throw new InvalidOperationException(
                "Ligase Host 的标准端口 48989 当前不可用。请关闭占用该端口的其他实例后重试。");

        var configuration = BuildManagedConfiguration(_paths, BasePort);
        await File.WriteAllTextAsync(
            _paths.ApolloConfigFile,
            configuration,
            cancellationToken);
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
        if (!await WaitForActiveStopBeforeStartAsync(cancellationToken))
        {
            StartupError = "串流核心仍在停止。请稍后重试。";
            StatusChanged?.Invoke();
            return;
        }

        if (!await _lifecycleGate.WaitAsync(_timeouts.GateAcquire, cancellationToken))
        {
            StartupError = "串流核心生命周期忙碌。请稍后重试。";
            StatusChanged?.Invoke();
            return;
        }

        try
        {
            if (IsRunning) return;
            DisposeExitedProcess();
            _intentionalStop = false;
            await InitializeAsync(cancellationToken);
            var executable = ExecutablePath
                ?? throw new FileNotFoundException("Ligase 串流核心尚未随应用提供或从本仓库构建。");
            var generation = Interlocked.Increment(ref _generation);
            var process = _processFactory.Start(
                executable,
                $"\"{_paths.ApolloConfigFile}\"",
                Path.GetDirectoryName(executable)!);
            AttachProcess(process, generation);
            await Task.Delay(500, cancellationToken);
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"Apollo 核心启动后立即退出，代码 {process.ExitCode}。");
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

    private async Task<bool> WaitForActiveStopBeforeStartAsync(
        CancellationToken cancellationToken)
    {
        Task<ApolloStopOutcome>? activeStop;
        lock (_stopSync)
            activeStop = _activeStop is { IsCompleted: false } ? _activeStop : null;
        if (activeStop is null) return true;
        try
        {
            await activeStop.WaitAsync(_timeouts.GateAcquire, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public Task<ApolloStopOutcome> StopAsync(
        CancellationToken cancellationToken = default)
    {
        Task<ApolloStopOutcome> stop;
        lock (_stopSync)
        {
            if (_activeStop is { IsCompleted: false })
            {
                stop = _activeStop;
            }
            else
            {
                _activeStop = stop = StopGenerationAsync();
            }
        }

        return ObserveStopAsync(stop, cancellationToken);
    }

    public async Task<ApolloStopOutcome> StopForInstallerAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _lifecycleGate.WaitAsync(_timeouts.GateAcquire, cancellationToken))
            return Outcome(
                ApolloStopCodes.GateTimeout,
                ApolloStopStages.Gate,
                _process,
                Generation,
                ApolloStopNextActions.RetryStop);

        try
        {
            var process = _process;
            var generation = Generation;
            _intentionalStop = true;
            if (process is null || process.HasExited)
                return new ApolloStopOutcome(
                    ApolloStopCodes.AlreadyStopped,
                    ApolloStopStages.Complete,
                    process?.Id,
                    generation,
                    false,
                    ApolloStopNextActions.None);
            if (!process.RequestGracefulExit())
                return Outcome(
                    ApolloStopCodes.GracefulSignalUnavailable,
                    ApolloStopStages.GracefulWait,
                    process,
                    generation,
                    ApolloStopNextActions.RetryStop);
            if (!await WaitForExitAsync(process, TimeSpan.FromSeconds(5)))
                return Outcome(
                    ApolloStopCodes.GracefulTimeout,
                    ApolloStopStages.GracefulWait,
                    process,
                    generation,
                    ApolloStopNextActions.RetryStop);

            var processId = process.Id;
            DisposeExitedProcess(process, generation);
            StartupError = null;
            return PublishStopOutcome(new ApolloStopOutcome(
                ApolloStopCodes.Stopped,
                ApolloStopStages.GracefulWait,
                processId,
                generation,
                false,
                ApolloStopNextActions.None));
        }
        finally
        {
            _lifecycleGate.Release();
            StatusChanged?.Invoke();
        }
    }

    private async Task<ApolloStopOutcome> StopGenerationAsync()
    {
        if (!await _lifecycleGate.WaitAsync(_timeouts.GateAcquire))
        {
            var timeout = PublishStopOutcome(Outcome(
                ApolloStopCodes.GateTimeout,
                ApolloStopStages.Gate,
                _process,
                Generation,
                ApolloStopNextActions.RetryStop));
            StatusChanged?.Invoke();
            return timeout;
        }

        try
        {
            var requestedProcess = _process;
            var requestedGeneration = Generation;
            if (requestedProcess is null ||
                requestedProcess.HasExited ||
                !ReferenceEquals(requestedProcess, _process) ||
                requestedGeneration != Generation)
            {
                if (ReferenceEquals(requestedProcess, _process) &&
                    requestedProcess is { HasExited: true })
                {
                    DisposeExitedProcess(requestedProcess, requestedGeneration);
                }
                return PublishStopOutcome(new ApolloStopOutcome(
                    ApolloStopCodes.AlreadyStopped,
                    ApolloStopStages.Complete,
                    requestedProcess?.Id,
                    requestedGeneration,
                    false,
                    ApolloStopNextActions.None));
            }

            _intentionalStop = true;
            if (await WaitForExitAsync(requestedProcess, _timeouts.GracefulExit))
            {
                var gracefulProcessId = requestedProcess.Id;
                DisposeExitedProcess(requestedProcess, requestedGeneration);
                StartupError = null;
                return PublishStopOutcome(new ApolloStopOutcome(
                    ApolloStopCodes.Stopped,
                    ApolloStopStages.GracefulWait,
                    gracefulProcessId,
                    requestedGeneration,
                    false,
                    ApolloStopNextActions.None));
            }

            Task kill;
            try
            {
                kill = requestedProcess.KillAsync();
            }
            catch
            {
                return PublishStopOutcome(Outcome(
                    ApolloStopCodes.KillFailed,
                    ApolloStopStages.Kill,
                    requestedProcess,
                    requestedGeneration,
                    ApolloStopNextActions.RetryStop));
            }
            var killCompleted = await WaitForCompletionAsync(kill, _timeouts.Kill);
            if (!killCompleted)
            {
                ObserveLateTask(kill);
                return PublishStopOutcome(Outcome(
                    ApolloStopCodes.KillTimeout,
                    ApolloStopStages.Kill,
                    requestedProcess,
                    requestedGeneration,
                    ApolloStopNextActions.RetryStop));
            }
            if (kill.IsFaulted)
            {
                _ = kill.Exception;
                return PublishStopOutcome(Outcome(
                    ApolloStopCodes.KillFailed,
                    ApolloStopStages.Kill,
                    requestedProcess,
                    requestedGeneration,
                    ApolloStopNextActions.RetryStop));
            }

            if (!await WaitForExitAsync(requestedProcess, _timeouts.PostKillExit))
            {
                return PublishStopOutcome(Outcome(
                    ApolloStopCodes.PostKillTimeout,
                    ApolloStopStages.PostKillWait,
                    requestedProcess,
                    requestedGeneration,
                    ApolloStopNextActions.RetryStop));
            }

            var killedProcessId = requestedProcess.Id;
            DisposeExitedProcess(requestedProcess, requestedGeneration);
            StartupError = null;
            return PublishStopOutcome(new ApolloStopOutcome(
                ApolloStopCodes.Stopped,
                ApolloStopStages.PostKillWait,
                killedProcessId,
                requestedGeneration,
                false,
                ApolloStopNextActions.None));
        }
        finally
        {
            _lifecycleGate.Release();
            StatusChanged?.Invoke();
        }
    }

    private async Task<ApolloStopOutcome> ObserveStopAsync(
        Task<ApolloStopOutcome> stop,
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
            return await stop;
        try
        {
            return await stop.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            var process = _process;
            return new ApolloStopOutcome(
                ApolloStopCodes.CallerCancelled,
                ApolloStopStages.Observe,
                process?.Id,
                Generation,
                process is { HasExited: false },
                ApolloStopNextActions.WaitForActiveStop);
        }
    }

    private ApolloStopOutcome PublishStopOutcome(ApolloStopOutcome outcome)
    {
        LastStopOutcome = outcome;
        if (outcome.ProcessStillAlive)
        {
            StartupError =
                $"串流核心停止未完成（{outcome.Code}/{outcome.Stage}）。请重试停止；若仍失败，请结束 Ligase Host。";
        }
        return outcome;
    }

    private static ApolloStopOutcome Outcome(
        string code,
        string stage,
        IManagedApolloProcess? process,
        long generation,
        string nextAction) =>
        new(
            code,
            stage,
            process?.Id,
            generation,
            process is { HasExited: false },
            nextAction);

    private static async Task<bool> WaitForExitAsync(
        IManagedApolloProcess process,
        TimeSpan timeout)
    {
        if (process.HasExited) return true;
        var wait = process.WaitForExitAsync(CancellationToken.None);
        if (!await WaitForCompletionAsync(wait, timeout))
        {
            ObserveLateTask(wait);
            return process.HasExited;
        }
        if (wait.IsFaulted)
        {
            _ = wait.Exception;
            return false;
        }
        return process.HasExited;
    }

    private static async Task<bool> WaitForCompletionAsync(
        Task task,
        TimeSpan timeout)
    {
        var delay = Task.Delay(timeout);
        return await Task.WhenAny(task, delay) == task;
    }

    private static void ObserveLateTask(Task task)
    {
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted |
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnProcessExited(IManagedApolloProcess exited, long generation)
    {
        if (!ReferenceEquals(exited, _process) || generation != Generation) return;
        if (_intentionalStop)
        {
            Task<ApolloStopOutcome>? activeStop;
            lock (_stopSync)
                activeStop = _activeStop;
            if (activeStop is { IsCompleted: true })
            {
                var processId = exited.Id;
                DisposeExitedProcess(exited, generation);
                LastStopOutcome = new ApolloStopOutcome(
                    ApolloStopCodes.Stopped,
                    ApolloStopStages.Complete,
                    processId,
                    generation,
                    false,
                    ApolloStopNextActions.None);
                StartupError = null;
            }
        }
        else
        {
            StartupError =
                $"串流核心意外退出（代码 {exited.ExitCode}）。请点击“重新启动”；如果仍失败，请关闭其他 Ligase Host 实例。";
        }
        StatusChanged?.Invoke();
    }

    private void AttachProcess(IManagedApolloProcess process, long generation)
    {
        _process = process;
        Interlocked.Exchange(ref _generation, generation);
        process.Exited += (_, _) => OnProcessExited(process, generation);
        ProcessStartedAt = process.StartedAtUtc;
    }

    internal void AttachProcessForTesting(
        IManagedApolloProcess process,
        long generation)
    {
        AttachProcess(process, generation);
        BasePort = 48989;
        StartupError = null;
    }

    internal async Task<bool> StartProcessForTestingAsync(
        IManagedApolloProcess process,
        CancellationToken cancellationToken = default)
    {
        if (!await WaitForActiveStopBeforeStartAsync(cancellationToken))
            return false;
        if (!await _lifecycleGate.WaitAsync(_timeouts.GateAcquire, cancellationToken))
            return false;
        try
        {
            if (IsRunning) return false;
            DisposeExitedProcess();
            _intentionalStop = false;
            AttachProcess(process, Generation + 1);
            return true;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    internal async Task HoldLifecycleGateForTestingAsync(
        TaskCompletionSource entered,
        CancellationToken release)
    {
        await _lifecycleGate.WaitAsync();
        entered.TrySetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, release);
        }
        catch (OperationCanceledException) when (release.IsCancellationRequested)
        {
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void DisposeExitedProcess(
        IManagedApolloProcess? expected = null,
        long? generation = null)
    {
        if (_process is null) return;
        if (expected is not null && !ReferenceEquals(expected, _process)) return;
        if (generation is not null && generation.Value != Generation) return;
        if (!_process.HasExited) return;
        _process.Dispose();
        _process = null;
        ProcessStartedAt = null;
        StartNonce = string.Empty;
        ExpectedUniqueId = null;
        BasePort = 0;
    }

    private string? FindBundledExecutable()
    {
        if (_installationLayout.Kind is not InstallationLayoutKind.Development)
            return File.Exists(_installationLayout.CoreExecutable)
                ? _installationLayout.CoreExecutable
                : null;

        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            _installationLayout.CoreExecutable,
            Path.Combine(baseDirectory, "Apollo", "sunshine.exe"),
            Path.Combine(baseDirectory, "sunshine.exe"),
            Path.GetFullPath(Path.Combine(
                baseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "build",
                "sunshine.exe"))
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}

internal interface IManagedApolloProcess : IDisposable
{
    event EventHandler? Exited;
    int Id { get; }
    int ExitCode { get; }
    bool HasExited { get; }
    DateTimeOffset StartedAtUtc { get; }
    bool RequestGracefulExit();
    Task KillAsync();
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

internal interface IManagedApolloProcessFactory
{
    IManagedApolloProcess Start(
        string executable,
        string arguments,
        string workingDirectory);
}

internal sealed class SystemManagedApolloProcessFactory : IManagedApolloProcessFactory
{
    public IManagedApolloProcess Start(
        string executable,
        string arguments,
        string workingDirectory)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Apollo 核心未能启动。");
        return new SystemManagedApolloProcess(process);
    }
}

internal sealed class SystemManagedApolloProcess : IManagedApolloProcess
{
    private readonly Process _process;

    public SystemManagedApolloProcess(Process process)
    {
        _process = process;
        _process.EnableRaisingEvents = true;
        _process.Exited += ForwardExited;
        StartedAtUtc = process.StartTime.ToUniversalTime();
    }

    public event EventHandler? Exited;
    public int Id => _process.Id;
    public int ExitCode => _process.ExitCode;
    public bool HasExited => _process.HasExited;
    public DateTimeOffset StartedAtUtc { get; }

    public bool RequestGracefulExit() =>
        LigaseManagedCoreExitSignal.Request(_process.Id) > 0;

    public Task KillAsync() =>
        Task.Run(() => _process.Kill(true));

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        _process.WaitForExitAsync(cancellationToken);

    public void Dispose()
    {
        _process.Exited -= ForwardExited;
        _process.Dispose();
    }

    private void ForwardExited(object? sender, EventArgs eventArgs) =>
        Exited?.Invoke(this, EventArgs.Empty);
}

internal static class LigaseManagedCoreExitSignal
{
    private const uint ManagedShutdownMessage = 0x8000 + 0x4C;
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr state);

    public static int Request(int processId)
    {
        var sent = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == unchecked((uint)processId) &&
                PostMessageW(window, ManagedShutdownMessage, IntPtr.Zero, IntPtr.Zero))
                sent++;
            return true;
        }, IntPtr.Zero);
        return sent;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(
        IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
