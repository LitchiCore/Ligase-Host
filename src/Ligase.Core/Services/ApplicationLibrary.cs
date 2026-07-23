using System.Text.Json;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public sealed class ApplicationLibrary(
    LigasePaths paths,
    IApolloAppsWriter apolloAppsWriter) : IApplicationLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<LibraryState> LoadAsync(CancellationToken cancellationToken = default)
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

    public Task<LibraryItem> AddSteamAsync(
        SteamGame game,
        CancellationToken cancellationToken = default) =>
        MutateAsync(state =>
        {
            var existing = state.Items.FirstOrDefault(item =>
                item.Kind == LibraryItemKind.Steam && item.SteamAppId == game.AppId);
            if (existing is not null) return existing;

            var item = new LibraryItem
            {
                Kind = LibraryItemKind.Steam,
                Name = game.Name,
                SteamAppId = game.AppId,
                SteamInstallPath = game.InstallPath
            };
            state.Items.Add(item);
            return item;
        }, cancellationToken);

    public Task<LibraryItem> AddExecutableAsync(
        string name,
        string executablePath,
        string? arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default) =>
        MutateAsync(state =>
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("应用名称不能为空。", nameof(name));
            var fullExecutablePath = Path.GetFullPath(executablePath);
            if (!File.Exists(fullExecutablePath)) throw new FileNotFoundException("找不到要添加的程序。", fullExecutablePath);

            var existing = state.Items.FirstOrDefault(item =>
                item.Kind == LibraryItemKind.Executable &&
                string.Equals(item.ExecutablePath, fullExecutablePath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;

            var item = new LibraryItem
            {
                Kind = LibraryItemKind.Executable,
                Name = name.Trim(),
                ExecutablePath = fullExecutablePath,
                Arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim(),
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                    ? Path.GetDirectoryName(fullExecutablePath)
                    : Path.GetFullPath(workingDirectory)
            };
            state.Items.Add(item);
            return item;
        }, cancellationToken);

    public async Task SetSortModeAsync(
        LibrarySortMode sortMode,
        CancellationToken cancellationToken = default)
    {
        await MutateAsync<object?>(state =>
        {
            state.SortMode = sortMode;
            return null;
        }, cancellationToken);
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await MutateAsync<object?>(state =>
        {
            state.Items.RemoveAll(item => item.Id == id);
            return null;
        }, cancellationToken);
    }

    private async Task<T> MutateAsync<T>(
        Func<LibraryState, T> mutation,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadCoreAsync(cancellationToken);
            var result = mutation(state);
            state.Revision++;
            state.UpdatedAt = DateTimeOffset.UtcNow;
            await WriteCoreAsync(state, cancellationToken);
            await apolloAppsWriter.WriteAsync(state.Items, cancellationToken);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<LibraryState> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.LibraryFile))
        {
            var initialState = new LibraryState();
            EnsureSystemEntries(initialState);
            return initialState;
        }

        await using var stream = File.OpenRead(paths.LibraryFile);
        var state = await JsonSerializer.DeserializeAsync<LibraryState>(stream, JsonOptions, cancellationToken)
                    ?? new LibraryState();
        EnsureSystemEntries(state);
        return state;
    }

    private async Task WriteCoreAsync(LibraryState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.RootDirectory);
        var temporaryPath = paths.LibraryFile + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, paths.LibraryFile, true);
    }

    private static void EnsureSystemEntries(LibraryState state)
    {
        AddSystemEntryIfMissing(
            state,
            SystemLibraryIds.Desktop,
            LibraryItemKind.Desktop,
            "监控桌面");
        AddSystemEntryIfMissing(
            state,
            SystemLibraryIds.VirtualDesktop,
            LibraryItemKind.VirtualDesktop,
            "虚拟桌面");
    }

    private static void AddSystemEntryIfMissing(
        LibraryState state,
        Guid id,
        LibraryItemKind kind,
        string name)
    {
        var existingIndex = state.Items.FindIndex(item => item.Kind == kind);
        if (existingIndex >= 0)
        {
            var existing = state.Items[existingIndex];
            if (existing.Id == id) return;

            state.Items[existingIndex] = new LibraryItem
            {
                Id = id,
                Kind = kind,
                Name = name,
                AddedAt = existing.AddedAt,
                UpdatedAt = existing.UpdatedAt,
                LastPlayedAt = existing.LastPlayedAt
            };
            return;
        }

        state.Items.Insert(0, new LibraryItem
        {
            Id = id,
            Kind = kind,
            Name = name,
            AddedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        });
    }
}
