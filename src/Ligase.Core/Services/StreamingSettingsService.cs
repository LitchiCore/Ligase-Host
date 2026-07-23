using System.Text.Json;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public sealed class StreamingSettingsService(
    LigasePaths paths,
    LigaseSyncDocumentWriter? syncWriter = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<StreamingSettingsState> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<StreamingSettingsState> SetGlobalResolutionAsync(
        StreamResolution resolution,
        long? baseRevision = null,
        CancellationToken cancellationToken = default) =>
        MutateAsync(state => state.GlobalResolution = resolution, baseRevision, cancellationToken);

    public Task<StreamingSettingsState> SetAppResolutionAsync(
        Guid appId,
        StreamResolution? resolution,
        long? baseRevision = null,
        CancellationToken cancellationToken = default) =>
        MutateAsync(state =>
        {
            if (resolution is null)
            {
                state.Apps.Remove(appId);
                return;
            }

            state.Apps[appId] = new AppStreamingSettings { Resolution = resolution };
        }, baseRevision, cancellationToken);

    private async Task<StreamingSettingsState> MutateAsync(
        Action<StreamingSettingsState> mutation,
        long? baseRevision,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadCoreAsync(cancellationToken);
            if (baseRevision is not null && state.Revision != baseRevision)
                throw new RevisionConflictException(baseRevision.Value, state.Revision);

            mutation(state);
            state.Revision++;
            state.UpdatedAt = DateTimeOffset.UtcNow;
            await WriteCoreAsync(state, cancellationToken);
            if (syncWriter is not null)
                await syncWriter.WriteStreamingAsync(state, cancellationToken);
            return state;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StreamingSettingsState> ReadCoreAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.StreamingSettingsFile))
        {
            var initial = new StreamingSettingsState();
            await WriteCoreAsync(initial, cancellationToken);
            return initial;
        }

        await using var stream = File.OpenRead(paths.StreamingSettingsFile);
        return await JsonSerializer.DeserializeAsync<StreamingSettingsState>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? new StreamingSettingsState();
    }

    private async Task WriteCoreAsync(
        StreamingSettingsState state,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.RootDirectory);
        var temporaryPath = paths.StreamingSettingsFile + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, paths.StreamingSettingsFile, true);
    }
}

public sealed class RevisionConflictException(long expectedRevision, long actualRevision)
    : InvalidOperationException(
        $"配置修订号冲突：客户端基于 {expectedRevision}，Host 当前为 {actualRevision}。")
{
    public long ExpectedRevision { get; } = expectedRevision;
    public long ActualRevision { get; } = actualRevision;
}
