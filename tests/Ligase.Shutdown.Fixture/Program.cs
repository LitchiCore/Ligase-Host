using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ligase.Shutdown.Fixture;

internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        var values = Parse(arguments);
        return values["mode"] switch
        {
            "core" => await RunCoreAsync(values),
            "desktop" => await RunDesktopAsync(values),
            "launcher" => await RunLauncherAsync(values),
            "orchestrate" => await RunOrchestratorAsync(values),
            _ => 64
        };
    }

    private static async Task<int> RunCoreAsync(IReadOnlyDictionary<string, string> values)
    {
        if (values.TryGetValue("log", out var log))
            await AppendLogAsync(log, "core-start\n");
        var deadline = Stopwatch.StartNew();
        var lifetime = values.TryGetValue("hold", out var hold) && hold == "true"
            ? TimeSpan.FromMinutes(2)
            : TimeSpan.FromSeconds(30);
        while (!File.Exists(values["stopFile"]) &&
               deadline.Elapsed < lifetime)
            await Task.Delay(25);
        var signaled = File.Exists(values["stopFile"]);
        if (log is not null)
            await AppendLogAsync(log, "core-signal-observed:" + signaled + "\n");
        return 0;
    }

    private static async Task<int> RunDesktopAsync(
        IReadOnlyDictionary<string, string> values)
    {
        await AppendLogAsync(values["log"], "desktop-start\n");
        await using var pipe = new NamedPipeServerStream(
            PipeName(values["root"]), PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await AppendLogAsync(values["log"], "pipe-connected\n");
        using var reader = new StreamReader(
            pipe, new UTF8Encoding(false, true), false, 1024, true);
        await using var writer = new StreamWriter(
            pipe, new UTF8Encoding(false), 1024, true)
        { AutoFlush = true, NewLine = "\n" };
        var request = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
        using var requestDocument = JsonDocument.Parse(request!);
        var requestId = requestDocument.RootElement.GetProperty("requestId").GetString();
        if (values["behavior"] == "ackUnavailable")
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            return 0;
        }
        await writer.WriteLineAsync(
            $"{{\"schemaVersion\":1,\"requestId\":\"{requestId}\",\"state\":\"accepted\"}}");
        await AppendLogAsync(values["log"], "accepted\n");
        if (values["behavior"] == "timeout")
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            return 0;
        }
        if (values["behavior"] == "terminalFailure")
        {
            await writer.WriteLineAsync(
                $"{{\"schemaVersion\":1,\"requestId\":\"{requestId}\",\"state\":\"failed\",\"code\":\"shutdownTimeout\",\"coreProcessStillAlive\":true}}");
            await AppendLogAsync(values["log"], "terminal-failed\n");
            return 0;
        }
        if (values["behavior"] == "terminalEof")
        {
            await AppendLogAsync(values["log"], "terminal-eof\n");
            return 0;
        }

        if (values["behavior"] == "legacyCleanupFault")
        {
            using var legacyCore = Process.GetProcessById(int.Parse(values["corePid"]));
            await File.WriteAllTextAsync(values["stopFile"], "stop");
            await AppendLogAsync(values["log"], "legacy-core-signal\n");
            await legacyCore.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await AppendLogAsync(values["log"], "legacy-terminal-eof\n");
            writer.Dispose();
            pipe.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(20));
            return 0;
        }

        using var core = Process.GetProcessById(int.Parse(values["corePid"]));
        await File.WriteAllTextAsync(values["stopFile"], "stop");
        await AppendLogAsync(values["log"], "core-signal\n");
        var coreDeadline = Stopwatch.StartNew();
        while (!core.HasExited && coreDeadline.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(25);
        if (!core.HasExited)
            throw new TimeoutException("coreExitUnavailable");
        await writer.WriteLineAsync(values["behavior"] == "legacyReordered"
            ? $"{{\"code\":\"coreStopped\",\"coreProcessStillAlive\":false,\"state\":\"completed\",\"requestId\":\"{requestId}\",\"schemaVersion\":1}}"
            : $"{{\"schemaVersion\":3,\"requestId\":\"{requestId}\",\"state\":\"completed\",\"code\":\"exitCommitted\",\"cleanupState\":\"completed\",\"coreStopCode\":\"stopped\",\"coreProcessStillAlive\":false,\"shutdownProtocolVersion\":3}}");
        await AppendLogAsync(values["log"], "terminal\n");
        return 0;
    }

    private static async Task<int> RunLauncherAsync(
        IReadOnlyDictionary<string, string> values)
    {
        await File.AppendAllTextAsync(values["launchCount"], "launch\n");
        var desktop = Start(
            Path.Combine(values["root"], "Desktop", "Ligase.Host.Desktop.exe"),
            "mode", "desktop",
            "root", values["root"],
            "stopFile", values["stopFile"],
            "corePid", values["corePid"],
            "behavior", values["behavior"],
            "log", values["log"]);
        await desktop.WaitForExitAsync();
        return desktop.ExitCode;
    }

    private static async Task<int> RunOrchestratorAsync(
        IReadOnlyDictionary<string, string> values)
    {
        var sourceRoot = Path.GetFullPath(values["sourceRoot"]);
        var fixtureRoot = Path.Combine(
            @"D:\Development\Ligase\Build",
            "shutdown-protocol-" + Guid.NewGuid().ToString("N"));
        var installRoot = Path.Combine(fixtureRoot, "Ligase Host");
        var stopFile = Path.Combine(fixtureRoot, "core-stop.signal");
        var launchCount = Path.Combine(fixtureRoot, "launch-count.txt");
        var fixtureLog = Path.Combine(fixtureRoot, "fixture.log");
        var evidenceRoot = Path.Combine(fixtureRoot, "shutdown-evidence");
        var owned = new List<Process>();
        Directory.CreateDirectory(fixtureRoot);
        try
        {
            CopyRuntime(installRoot, "Ligase Host.exe");
            CopyRuntime(Path.Combine(installRoot, "Desktop"),
                "Ligase.Host.Desktop.exe");
            CopyRuntime(Path.Combine(installRoot, "Core"), "sunshine.exe");
            CopyRuntime(Path.Combine(installRoot, "Tools", "GameWatcher"),
                "Ligase.GameWatcher.exe");
            WriteInstallManifest(installRoot);
            if (values["case"] == "stale")
            {
                var staleRoot = Path.Combine(fixtureRoot, "Foreign");
                CopyRuntime(staleRoot, "Ligase.Host.Desktop.exe");
                var stale = Start(
                    Path.Combine(staleRoot, "Ligase.Host.Desktop.exe"),
                    "mode", "core", "stopFile", stopFile);
                owned.Add(stale);
                await Task.Delay(150);
                var query = await RunManagementAsync(
                    sourceRoot, installRoot, "QueryRunningProduct", evidenceRoot);
                Require(query.ExitCode == 0 &&
                    query.Stdout == "{\"code\":\"productNotRunning\",\"success\":true}",
                    "stalePathWasCounted");
                var queryTerminals = Directory.GetFiles(evidenceRoot,
                    "shutdown-terminal-*.json", SearchOption.TopDirectoryOnly);
                Require(queryTerminals.Length == 1, "queryTerminalCountInvalid");
                using var queryDocument = JsonDocument.Parse(
                    await File.ReadAllBytesAsync(queryTerminals[0]));
                var queryTerminal = queryDocument.RootElement;
                Require(queryTerminal.GetProperty("operation").GetString() == "query" &&
                    queryTerminal.GetProperty("code").GetString() == "productNotRunning" &&
                    queryTerminal.GetProperty("finalResidual").GetProperty("processes")
                        .GetArrayLength() == 0 &&
                    !queryTerminal.GetProperty("pipe").GetProperty("connected").GetBoolean(),
                    "queryTerminalInvalid");
                Console.WriteLine("{\"case\":\"stale\",\"staleIgnored\":true}");
                return 0;
            }

            var core = Start(
                Path.Combine(installRoot, "Core", "sunshine.exe"),
                "mode", "core", "stopFile", stopFile, "log", fixtureLog,
                "hold", (values["case"] == "forceResidue").ToString().ToLowerInvariant());
            owned.Add(core);
            if (values["case"] == "residual")
            {
                var watcher = Start(Path.Combine(installRoot, "Tools", "GameWatcher",
                    "Ligase.GameWatcher.exe"), "mode", "core", "stopFile",
                    Path.Combine(fixtureRoot, "watcher-never-stop.signal"));
                owned.Add(watcher);
            }
            var launcher = Start(
                Path.Combine(installRoot, "Ligase Host.exe"),
                "mode", "launcher",
                "root", installRoot,
                "stopFile", stopFile,
                "corePid", core.Id.ToString(),
                "behavior", values["case"].StartsWith("force", StringComparison.Ordinal)
                    ? "terminalFailure" : values["case"],
                "launchCount", launchCount,
                "log", fixtureLog);
            owned.Add(launcher);
            await WaitForProcessPathAsync(
                Path.Combine(installRoot, "Desktop", "Ligase.Host.Desktop.exe"),
                TimeSpan.FromSeconds(5));

            if (values["case"] == "forceNoManifest")
                File.Delete(Path.Combine(installRoot, "ligase-install-manifest.json"));
            if (values["case"] == "forceManifestDrift")
            {
                var manifestPath = Path.Combine(installRoot,
                    "ligase-install-manifest.json");
                var manifestText = await File.ReadAllTextAsync(manifestPath);
                await File.WriteAllTextAsync(manifestPath,
                    manifestText.Replace("\"size\":", "\"size\":-1,\"oldSize\":"));
            }
            await using var extraLocker = values["case"] == "forceExtraLocker"
                ? new FileStream(Path.Combine(installRoot, "Core", "sunshine.exe"),
                    FileMode.Open, FileAccess.Read, FileShare.Read)
                : null;
            var validationBehavior = values["case"] switch
            {
                "forceRequired" or "forceNoConsent" or "forceNoManifest" or
                    "forceManifestDrift" or "forceExtraLocker" =>
                    "simulateGraceful351",
                "forceNative351" => "simulateGraceful351Force351",
                "forceTimeout" => "simulateGraceful351ForceTimeout",
                "forcePermission" => "simulateGraceful351ForcePermission",
                "forceResidue" => "simulateGraceful351ForceCompleted",
                _ => "none"
            };
            if (values["case"] == "success")
            {
                var dryRunRoot = Path.Combine(fixtureRoot, "eligibility-evidence");
                var dryRun = await RunManagementAsync(sourceRoot, installRoot,
                    "EvaluateLegacyForceEligibility", dryRunRoot);
                Require(dryRun.ExitCode == 0 &&
                    dryRun.Stdout == "{\"code\":\"liveLegacyForceEligible\",\"success\":true}" &&
                    IsRunning(core) && IsRunning(launcher),
                    "eligibilityDryRunChangedProductState:" +
                    dryRun.ExitCode + ":" + dryRun.Stdout + ":" +
                    IsRunning(core) + ":" + IsRunning(launcher));
                var dryRunTerminals = Directory.GetFiles(dryRunRoot,
                    "shutdown-terminal-*.json", SearchOption.TopDirectoryOnly);
                Require(dryRunTerminals.Length == 1,
                    "eligibilityDryRunTerminalCountInvalid");
                using var dryRunDocument = JsonDocument.Parse(
                    await File.ReadAllBytesAsync(dryRunTerminals[0]));
                var dryRunTerminal = dryRunDocument.RootElement;
                Require(dryRunTerminal.GetProperty("wouldBeForcedEligible").GetBoolean() &&
                    !dryRunTerminal.GetProperty("forcedAttempted").GetBoolean() &&
                    dryRunTerminal.GetProperty("rmShutdownCalls").GetInt32() == 0 &&
                    dryRunTerminal.GetProperty("programWriteCalls").GetInt32() == 0,
                    "eligibilityDryRunTerminalInvalid");
            }
            var close = await RunManagementAsync(
                sourceRoot, installRoot, "CloseRunningProduct", evidenceRoot,
                validationBehavior, values["case"] != "forceNoConsent");
            Require(Directory.Exists(evidenceRoot),
                "shutdownEvidenceRootMissing:" + values["case"] + ":" +
                close.ExitCode + ":" + close.Stdout);
            var terminals = Directory.GetFiles(evidenceRoot,
                "shutdown-terminal-*.json", SearchOption.TopDirectoryOnly);
            Require(terminals.Length == 1, "shutdownTerminalCountInvalid:" +
                values["case"] + ":" + (File.Exists(Path.Combine(evidenceRoot,
                    "failure.txt")) ? File.ReadAllText(Path.Combine(evidenceRoot,
                    "failure.txt")) : "noFailureDetail"));
            using var terminalDocument = JsonDocument.Parse(
                await File.ReadAllBytesAsync(terminals[0]));
            var terminal = terminalDocument.RootElement;
            var expectedForceFailure = values["case"] is "forceNoConsent" or
                "forceNoManifest" or "forceManifestDrift" or "forceExtraLocker" or
                "forceNative351" or "forceTimeout" or "forcePermission" or
                "forceResidue";
            if (expectedForceFailure)
            {
                var forced = terminal.GetProperty("forcedRestartManagerShutdown");
                Require(close.ExitCode == 10 &&
                    terminal.GetProperty("state").GetString() == "failed" &&
                    terminal.GetProperty("programWriteCalls").GetInt32() == 0 &&
                    terminal.GetProperty("finalResidual").GetProperty("processes")
                        .GetArrayLength() > 0,
                    "forceNegativeTerminalInvalid:" + terminal.GetRawText());
                if (values["case"] == "forceNoConsent")
                    Require(!forced.GetProperty("attempted").GetBoolean(),
                        "forceAttemptedWithoutConsent");
                else if (values["case"] is "forceNative351" or "forceTimeout" or
                    "forcePermission" or "forceResidue")
                    Require(forced.GetProperty("eligible").GetBoolean() &&
                        forced.GetProperty("attempted").GetBoolean() &&
                        forced.GetProperty("forceUsed").GetBoolean() &&
                        (values["case"] == "forceResidue"
                            ? forced.GetProperty("code").GetString() == "completed"
                            : forced.GetProperty("code").GetString() != "completed"),
                        "forceFailureWasUpgraded");
                else
                    Require(!forced.GetProperty("attempted").GetBoolean() &&
                        !forced.GetProperty("eligible").GetBoolean(),
                        "ineligibleForceWasAttempted");
                Console.WriteLine($"{{\"case\":\"{values["case"]}\",\"forceRejected\":true,\"programWriteCalls\":0}}");
                return 0;
            }
            if (values["case"] is not "stale")
            {
                Require(close.ExitCode == 0 &&
                    close.Stdout == "{\"code\":\"productStopped\",\"success\":true}",
                    "shutdownDidNotComplete:" + close.ExitCode + ":" + close.Stdout +
                    ":" + terminal.GetRawText() +
                    ":" + (File.Exists(fixtureLog)
                        ? File.ReadAllText(fixtureLog).Replace('\n', '|')
                        : "noLog"));
                await launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                Require(!IsRunning(core), "coreStillRunning");
                Require(File.ReadAllLines(launchCount).Length == 1,
                    "launcherRestartedDesktop");
                Require(terminal.GetProperty("code").GetString() == "productStopped" &&
                    terminal.GetProperty("pipe").GetProperty("connected").GetBoolean() &&
                    terminal.GetProperty("pipe").GetProperty("requestSent").GetBoolean() &&
                    terminal.GetProperty("pipe").GetProperty("ackReceived").GetBoolean() ==
                        (values["case"] != "ackUnavailable") &&
                    terminal.GetProperty("pipe").GetProperty("desktopTerminalReceived").GetBoolean() ==
                        (values["case"] is not "legacyCleanupFault" and not "terminalEof" and not "ackUnavailable") &&
                    terminal.GetProperty("finalResidual").GetProperty("processes")
                        .GetArrayLength() == 0 &&
                    terminal.GetProperty("finalResidual").GetProperty("restartManager")
                        .GetProperty("code").GetString() == "completed" &&
                    terminal.GetProperty("finalResidual").GetProperty("restartManager")
                        .GetProperty("processes").GetArrayLength() == 0,
                    "successTerminalInvalid:" + terminal.GetRawText());
                var initialProcesses = terminal.GetProperty("initial")
                    .GetProperty("processes");
                Require(initialProcesses.GetArrayLength() >= 3 &&
                    initialProcesses.EnumerateArray().All(process =>
                        process.TryGetProperty("parentPid", out var parent) &&
                        parent.ValueKind == JsonValueKind.Number &&
                        process.TryGetProperty("startedUtc", out var started) &&
                        started.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(started.GetString())),
                    "processIdentityTimelineMissing");
                var compatibility = terminal.GetProperty(
                    "gracefulRestartManagerShutdown");
                var forced = terminal.GetProperty("forcedRestartManagerShutdown");
                if (values["case"] == "forceRequired")
                    Require(compatibility.GetProperty("nativeCode").GetInt32() == 351 &&
                        forced.GetProperty("eligible").GetBoolean() &&
                        forced.GetProperty("attempted").GetBoolean() &&
                        forced.GetProperty("forceUsed").GetBoolean() &&
                        forced.GetProperty("code").GetString() == "completed" &&
                        terminal.GetProperty("programWriteCalls").GetInt32() == 0,
                        "restartManagerForcedTerminalInvalid");
                if (values["case"] is "legacyCleanupFault" or "ackUnavailable" or
                    "terminalFailure" or "terminalEof" or "residual")
                    Require(compatibility.GetProperty("eligible").GetBoolean() &&
                        compatibility.GetProperty("attempted").GetBoolean() &&
                        !compatibility.GetProperty("forceUsed").GetBoolean() &&
                        compatibility.GetProperty("code").GetString() == "completed",
                        "restartManagerPrimaryTerminalInvalid");
                else if (values["case"] != "forceRequired")
                    Require(!compatibility.GetProperty("attempted").GetBoolean(),
                        "restartManagerShutdownUnexpected");
                if (values["case"] != "forceRequired")
                    Require(!forced.GetProperty("attempted").GetBoolean() &&
                        !forced.GetProperty("forceUsed").GetBoolean(),
                        "restartManagerForceUnexpected");
                Console.WriteLine(
                    $"{{\"case\":\"{values["case"]}\",\"terminalCode\":\"productStopped\",\"acknowledged\":true,\"launcherStarts\":1,\"remaining\":0,\"restartManagerCompatibility\":{compatibility.GetProperty("attempted").GetBoolean().ToString().ToLowerInvariant()},\"forceUsed\":{forced.GetProperty("forceUsed").GetBoolean().ToString().ToLowerInvariant()}}}");
                return 0;
            }
            throw new InvalidOperationException("unexpectedFixtureBranch");
        }
        finally
        {
            foreach (var process in owned.AsEnumerable().Reverse())
            {
                if (IsRunning(process))
                {
                    process.Kill(true);
                    process.WaitForExit(3000);
                }
                process.Dispose();
            }
            if (Directory.Exists(fixtureRoot))
            {
                var cleanupDeadline = DateTime.UtcNow.AddSeconds(3);
                while (true)
                {
                    try
                    {
                        Directory.Delete(fixtureRoot, true);
                        break;
                    }
                    catch (IOException) when (DateTime.UtcNow < cleanupDeadline)
                    {
                        await Task.Delay(50);
                    }
                    catch (IOException)
                    {
                        // The parent harness owns this unique D-only root and
                        // removes it after this fixture process has exited.
                        break;
                    }
                }
            }
        }

        void CopyRuntime(string destination, string executableName)
        {
            Directory.CreateDirectory(destination);
            var sourceDirectory = AppContext.BaseDirectory;
            foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("Ligase.Shutdown.Fixture.exe",
                        StringComparison.OrdinalIgnoreCase))
                    name = executableName;
                File.Copy(file, Path.Combine(destination, name), false);
            }
        }

        void WriteInstallManifest(string root)
        {
            var definitions = new[] {
                (Role: "launcher", RelativePath: "Ligase Host.exe"),
                (Role: "desktop", RelativePath: "Desktop/Ligase.Host.Desktop.exe"),
                (Role: "managedCore", RelativePath: "Core/sunshine.exe"),
                (Role: "gameWatcher", RelativePath: "Tools/GameWatcher/Ligase.GameWatcher.exe") };
            var artifacts = definitions.Select(definition =>
            {
                var path = Path.Combine(root,
                    definition.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var bytes = File.ReadAllBytes(path);
                var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                return new {
                    role = definition.Role,
                    relativePath = definition.RelativePath,
                    unsignedContentSha256 = sha,
                    signedArtifactSha256 = sha,
                    size = bytes.LongLength,
                    version = FileVersionInfo.GetVersionInfo(path).FileVersion,
                    signature = new {
                        status = "nonRelease", signerSubject = (string?)null,
                        signerThumbprint = (string?)null, timestamped = false } };
            }).ToArray();
            var manifest = new {
                schemaVersion = 1, installLayout = "structured-v1",
                platform = "x64", configuration = "Debug",
                installMode = "packaged", releaseKind = "UnsignedDev",
                artifacts };
            File.WriteAllText(Path.Combine(root, "ligase-install-manifest.json"),
                JsonSerializer.Serialize(manifest), new UTF8Encoding(false));
        }
    }

    private static async Task<(int ExitCode, string Stdout)> RunManagementAsync(
        string sourceRoot,
        string installRoot,
        string action,
        string? shutdownEvidenceRoot = null,
        string shutdownValidationBehavior = "none",
        bool userConfirmedClose = true)
    {
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var arguments = new List<string> {
            "-NoLogo", "-NoProfile", "-NonInteractive",
            "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(sourceRoot, "packaging", "windows", "ligase",
                "Manage-LigaseInstallation.ps1"),
            "-Action", action,
            "-InstallDirectory", installRoot };
        if (shutdownEvidenceRoot is not null)
        {
            arguments.Add("-ShutdownEvidenceRoot");
            arguments.Add(shutdownEvidenceRoot);
        }
        if (action == "CloseRunningProduct" && userConfirmedClose)
            arguments.Add("-UserConfirmedClose");
        if (shutdownValidationBehavior != "none")
        {
            arguments.Add("-ShutdownValidationBehavior");
            arguments.Add(shutdownValidationBehavior);
        }
        var start = new ProcessStartInfo(powershell) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["LIGASE_SHUTDOWN_VALIDATION_HARNESS"] = "1";
        var process = Process.Start(start) ??
            throw new InvalidOperationException("processStartFailed");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var managementTimeout = shutdownValidationBehavior != "none"
            ? TimeSpan.FromSeconds(65)
            : TimeSpan.FromSeconds(15);
        await process.WaitForExitAsync().WaitAsync(managementTimeout);
        var error = await stderr;
        Require(string.IsNullOrWhiteSpace(error), "managementStderr:" + error);
        return (process.ExitCode, (await stdout).Trim());
    }

    private static Process Start(string executable, params string[] nameValues)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        for (var index = 0; index < nameValues.Length; index += 2)
        {
            start.ArgumentList.Add("--" + nameValues[index]);
            start.ArgumentList.Add(nameValues[index + 1]);
        }
        return Process.Start(start) ?? throw new InvalidOperationException("processStartFailed");
    }

    private static Process StartExact(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("processStartFailed");
    }

    private static async Task WaitForProcessPathAsync(
        string expectedPath, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout)
        {
            if (Process.GetProcessesByName("Ligase.Host.Desktop").Any(process =>
            {
                using (process)
                {
                    try
                    {
                        return Path.GetFullPath(process.MainModule!.FileName)
                            .Equals(Path.GetFullPath(expectedPath),
                                StringComparison.OrdinalIgnoreCase);
                    }
                    catch { return false; }
                }
            })) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("desktopFixtureUnavailable");
    }

    private static string PipeName(string installRoot)
    {
        var normalized = Path.GetFullPath(installRoot)
            .TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        return "ligase-host-shutdown-v1-" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static Dictionary<string, string> Parse(string[] arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Length; index += 2)
        {
            if (index + 1 >= arguments.Length ||
                !arguments[index].StartsWith("--", StringComparison.Ordinal))
                throw new InvalidDataException("argumentsInvalid");
            values.Add(arguments[index][2..], arguments[index + 1]);
        }
        return values;
    }

    private static bool IsRunning(Process process)
    {
        try { return !process.HasExited; }
        catch { return false; }
    }

    private static async Task AppendLogAsync(string path, string value)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await File.AppendAllTextAsync(path, value);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                await Task.Delay(10);
            }
        }
    }

    private static void Require(bool condition, string code)
    {
        if (!condition) throw new InvalidOperationException(code);
    }
}
