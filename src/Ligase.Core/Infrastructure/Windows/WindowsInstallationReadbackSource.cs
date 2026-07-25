using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ligase.Host.Core.Application.Installation;
using Ligase.Host.Core.Domain.Installation;

namespace Ligase.Host.Core.Infrastructure.Windows;

public sealed class WindowsInstallationReadbackSource(
    string? installationDirectory = null) : IInstallationReadbackSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly string _installationDirectory = Path.GetFullPath(
        installationDirectory ?? AppContext.BaseDirectory);

    public async Task<InstallationReadbackData?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var script = Path.Combine(
            _installationDirectory,
            "Manage-LigaseInstallation.ps1");
        if (!ValidateDeployment(_installationDirectory))
            return null;

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-Action");
        startInfo.ArgumentList.Add("Readback");
        startInfo.ArgumentList.Add("-InstallDirectory");
        startInfo.ArgumentList.Add(_installationDirectory);

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
                return null;
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            await stderrTask;
            if (process.ExitCode != 0)
                return null;
            return Parse(stdout);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (process is { HasExited: false })
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(1000);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException
                        or System.ComponentModel.Win32Exception)
                {
                    // Fail closed; no background task retains this handle.
                }
            }
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }

    internal static bool ValidateDeployment(string installationDirectory)
    {
        try
        {
            var manifestPath = Path.Combine(
                installationDirectory,
                "ligase-install-manifest.json");
            var scriptPath = Path.Combine(
                installationDirectory,
                "Manage-LigaseInstallation.ps1");
            if (!File.Exists(manifestPath) || !File.Exists(scriptPath))
                return false;
            var json = File.ReadAllText(manifestPath);
            RejectDuplicateProperties(json);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var expectedRoot = new HashSet<string>(
                [
                    "schemaVersion", "sourceHead", "configuration", "platform",
                    "installMode", "releaseKind", "artifacts",
                    "privilegedHelpers", "virtualDisplay", "firewall", "encoder"
                ],
                StringComparer.Ordinal);
            if (root.ValueKind != JsonValueKind.Object ||
                !expectedRoot.SetEquals(
                    root.EnumerateObject().Select(property => property.Name)) ||
                root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("installMode").GetString() != "packaged" ||
                root.GetProperty("privilegedHelpers").ValueKind !=
                    JsonValueKind.Array)
            {
                return false;
            }
            var helper = root.GetProperty("privilegedHelpers")
                .EnumerateArray()
                .SingleOrDefault(item =>
                    item.TryGetProperty("relativePath", out var relativePath) &&
                    relativePath.GetString() ==
                        "Manage-LigaseInstallation.ps1");
            if (helper.ValueKind != JsonValueKind.Object)
                return false;
            var expectedHelper = new HashSet<string>(
                [
                    "relativePath", "unsignedContentSha256",
                    "signedArtifactSha256", "signerSubject",
                    "signerThumbprint", "timestamped"
                ],
                StringComparer.Ordinal);
            if (!expectedHelper.SetEquals(
                    helper.EnumerateObject().Select(property => property.Name)))
                return false;
            var expectedHash = helper.GetProperty(
                "signedArtifactSha256").GetString();
            if (expectedHash is null ||
                expectedHash.Length != 64 ||
                expectedHash.Any(character =>
                    !char.IsAsciiHexDigit(character) ||
                    char.IsAsciiLetterUpper(character)))
                return false;
            using var stream = File.OpenRead(scriptPath);
            var actualHash = Convert.ToHexString(
                SHA256.HashData(stream)).ToLowerInvariant();
            return string.Equals(
                actualHash,
                expectedHash,
                StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidOperationException)
        {
            return false;
        }
    }

    internal static InstallationReadbackData Parse(string json)
    {
        RejectDuplicateProperties(json);
        var document = JsonSerializer.Deserialize<ReadbackDocument>(
            json,
            JsonOptions) ?? throw new JsonException("installationReadbackInvalid");
        if (!document.Success ||
            !string.Equals(document.Code, "readbackComplete", StringComparison.Ordinal) ||
            !string.Equals(document.InstallMode, "packaged", StringComparison.Ordinal) ||
            document.Artifacts is null ||
            document.VirtualDisplay is null ||
            document.Firewall is null ||
            document.DriverTrust is null)
        {
            throw new JsonException("installationReadbackInvalid");
        }
        RequireAllowed(
            document.VirtualDisplay.State,
            "available", "notInstalled", "rebootRequired", "unsupported", "failed");
        RequireAllowed(
            document.Firewall.State,
            "configured", "notConfigured", "requiresElevation", "failed");
        RequireAllowed(
            document.DriverTrust.State,
            "caTrusted", "locallyTrustedSelfSigned", "untrusted", "missing", "expired");
        RequireAllowed(
            document.DataRootState,
            "fresh", "existing", "quarantined", "inaccessible");
        var roles = document.Artifacts.Select(item => item.Role).ToArray();
        if (roles.Length != 3 ||
            !new HashSet<string>(roles, StringComparer.Ordinal).SetEquals(
                ["desktop", "managedCore", "gameWatcher"]))
            throw new JsonException("installationReadbackInvalid");
        foreach (var artifact in document.Artifacts)
        {
            RequireAllowed(artifact.SignatureStatus, "valid", "nonRelease", "invalid");
            if (artifact.HashMatches !=
                (artifact.Available && artifact.MachineCode == "available"))
                throw new JsonException("installationReadbackInvalid");
        }
        return new(
            document.Artifacts.Select(item => new InstallationReadbackArtifact(
                item.Role,
                item.Available,
                item.Version,
                item.HashMatches,
                item.MachineCode,
                item.SignatureStatus)).ToArray(),
            new(
                document.VirtualDisplay.State,
                document.VirtualDisplay.MachineCode,
                document.VirtualDisplay.PhysicalDesktopAvailable),
            new(
                document.Firewall.State,
                document.Firewall.MachineCode,
                document.Firewall.LegacyBroadRuleCount,
                document.Firewall.LegacyCleanupRequiresExplicitConsent),
            new(
                document.DriverTrust.State,
                document.DriverTrust.MachineCode,
                document.DriverTrust.StoreCount,
                document.DriverTrust.SelfSigned,
                document.DriverTrust.Timestamped),
            document.DataRootState,
            document.RestartRequired);
    }

    private static void RequireAllowed(string value, params string[] allowed)
    {
        if (!allowed.Contains(value, StringComparer.Ordinal))
            throw new JsonException("installationReadbackInvalid");
    }

    private static void RejectDuplicateProperties(string json)
    {
        using var document = JsonDocument.Parse(json);
        Visit(document.RootElement);
        return;

        static void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new JsonException("installationReadbackDuplicateField");
                    Visit(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    Visit(item);
            }
        }
    }

    private sealed record ReadbackDocument(
        string Code,
        bool Success,
        string InstallMode,
        string SourceHead,
        IReadOnlyList<ArtifactDocument> Artifacts,
        VirtualDisplayDocument VirtualDisplay,
        DriverTrustDocument DriverTrust,
        FirewallDocument Firewall,
        EncoderDocument Encoder,
        string DataRootState,
        bool RestartRequired);

    private sealed record ArtifactDocument(
        string Role,
        bool Available,
        string Version,
        bool HashMatches,
        string MachineCode,
        string SignatureStatus);

    private sealed record VirtualDisplayDocument(
        string State,
        string MachineCode,
        bool PhysicalDesktopAvailable);

    private sealed record DriverTrustDocument(
        string State,
        string MachineCode,
        int StoreCount,
        bool SelfSigned,
        bool Timestamped);

    private sealed record FirewallDocument(
        string State,
        string MachineCode,
        int LegacyBroadRuleCount,
        IReadOnlyList<string> LegacyBroadRuleNames,
        bool LegacyCleanupRequiresExplicitConsent);

    private sealed record EncoderDocument(
        string State,
        string MachineCode,
        bool RequiredForInstall,
        bool RequiredForStreaming);
}

public sealed class UnavailableInstallationRecoveryLauncher
    : IInstallationRecoveryLauncher
{
    public Task<InstallationActionOutcome> OpenInstallerAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new InstallationActionOutcome(
                InstallationActionCode.Unavailable,
                false));
    }
}
