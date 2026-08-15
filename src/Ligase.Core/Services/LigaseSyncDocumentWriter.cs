using System.Text.Json;
using System.Text.Json.Serialization;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public sealed class LigaseSyncDocumentWriter(LigasePaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task WriteLibraryAsync(
        LibraryState library,
        CancellationToken cancellationToken = default)
    {
        var streaming = await ReadAsync(
            paths.StreamingSettingsFile,
            new StreamingSettingsState(),
            cancellationToken);
        await WriteAsync(library, streaming, cancellationToken);
    }

    public async Task WriteStreamingAsync(
        StreamingSettingsState streaming,
        CancellationToken cancellationToken = default)
    {
        var library = await ReadAsync(
            paths.LibraryFile,
            new LibraryState(),
            cancellationToken);
        await WriteAsync(library, streaming, cancellationToken);
    }

    public async Task WriteAsync(
        LibraryState library,
        StreamingSettingsState streaming,
        CancellationToken cancellationToken = default)
    {
        var document = new LigaseSyncDocument
        {
            Library = new LibrarySyncState
            {
                Revision = library.Revision,
                UpdatedAt = library.UpdatedAt,
                SortMode = library.SortMode,
                Items = library.Items.Select(item => new LibrarySyncItem
                {
                    Id = item.Id,
                    Kind = item.Kind,
                    Name = item.Name,
                    SteamAppId = item.SteamAppId,
                    PortableIdentity = item.PortableIdentity,
                    LayoutBinding = item.LayoutBinding,
                    CoverSha256 = item.CoverContentSha256,
                    CoverSourceKind = item.CoverSourceKind,
                    CoverSourceId = item.CoverSourceId,
                    CoverUsageRights = item.CoverUsageRights,
                    System = item.IsSystemEntry,
                    PublishedToClients = item.PublishedToClients,
                    AddedAt = item.AddedAt,
                    UpdatedAt = item.UpdatedAt,
                    LastPlayedAt = item.LastPlayedAt
                }).ToArray()
            },
            Streaming = streaming
        };

        AndroidSyncContractV1Validator.Validate(document);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(paths.RootDirectory);
            var temporaryPath = paths.SyncFile + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    JsonOptions,
                    cancellationToken);
            }

            File.Move(temporaryPath, paths.SyncFile, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<T> ReadAsync<T>(
        string path,
        T fallback,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return fallback;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? fallback;
    }
}
