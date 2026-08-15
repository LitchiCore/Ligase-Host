using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ligase.Host.Core.Domain.Installation;

namespace Ligase.Host.Desktop.Services;

public sealed record InstallerShutdownOutcome(
    bool ExitCommitted,
    string Code,
    string CleanupState,
    string CoreStopCode,
    bool CoreProcessStillAlive);

public sealed class InstallerShutdownService(
    InstallationLayout installationLayout) : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _listener;

    internal string PipeName => GetPipeName(installationLayout.RootDirectory);

    public void Start(
        Func<Task<InstallerShutdownOutcome>> shutdown,
        Action exitAfterAcknowledgement)
    {
        if (_listener is not null) return;
        _listener = ListenAsync(shutdown, exitAfterAcknowledgement, _lifetime.Token);
    }

    private async Task ListenAsync(
        Func<Task<InstallerShutdownOutcome>> shutdown,
        Action exitAfterAcknowledgement,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(
                    pipe, new UTF8Encoding(false, true), false, 1024, leaveOpen: true);
                using var writer = new StreamWriter(
                    pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
                { AutoFlush = true, NewLine = "\n" };
                var requestLine = await reader.ReadLineAsync(cancellationToken)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                if (!TryReadRequest(requestLine, out var requestId))
                {
                    await writer.WriteLineAsync(
                        "{\"schemaVersion\":1,\"state\":\"rejected\",\"code\":\"requestInvalid\"}");
                    continue;
                }

                await writer.WriteLineAsync(
                    $"{{\"schemaVersion\":1,\"requestId\":\"{requestId}\",\"state\":\"accepted\"}}");
                InstallerShutdownOutcome outcome;
                try
                {
                    outcome = await shutdown();
                }
                catch
                {
                    // The installer must receive a typed terminal even when a
                    // product-specific cleanup callback fails. EOF is not a
                    // successful Desktop terminal.
                    outcome = new InstallerShutdownOutcome(
                        false, "exitCommitFailed", "faulted",
                        "notObserved", true);
                }
                var state = outcome.ExitCommitted ? "completed" : "failed";
                await writer.WriteLineAsync(
                    $"{{\"schemaVersion\":3,\"requestId\":\"{requestId}\",\"state\":\"{state}\",\"code\":\"{outcome.Code}\",\"cleanupState\":\"{outcome.CleanupState}\",\"coreStopCode\":\"{outcome.CoreStopCode}\",\"coreProcessStillAlive\":{outcome.CoreProcessStillAlive.ToString().ToLowerInvariant()},\"shutdownProtocolVersion\":3}}");
                await writer.FlushAsync(cancellationToken);
                if (outcome.ExitCommitted)
                {
                    exitAfterAcknowledgement();
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // A disconnected caller gets no shutdown credit. Keep serving.
            }
            catch (TimeoutException)
            {
                // An incomplete request is rejected by closing this connection.
            }
        }
    }

    internal static string GetPipeName(string installRoot)
    {
        var normalized = Path.GetFullPath(installRoot)
            .TrimEnd(Path.DirectorySeparatorChar)
            .ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return "ligase-host-shutdown-v1-" + hash;
    }

    private static bool TryReadRequest(string? json, out string requestId)
    {
        requestId = string.Empty;
        if (json is null || Encoding.UTF8.GetByteCount(json) > 1024) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != 3 ||
                !root.TryGetProperty("schemaVersion", out var schema) ||
                !root.TryGetProperty("command", out var command) ||
                !root.TryGetProperty("requestId", out var request) ||
                schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != 1 ||
                command.ValueKind != JsonValueKind.String ||
                command.GetString() != "shutdown" ||
                request.ValueKind != JsonValueKind.String)
                return false;
            requestId = request.GetString() ?? string.Empty;
            return requestId.Length == 32 && requestId.All(Uri.IsHexDigit) &&
                requestId.All(value => !char.IsLetter(value) || char.IsLower(value));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_listener is not null)
        {
            try { await _listener; }
            catch (OperationCanceledException) { }
        }
        _lifetime.Dispose();
    }
}
