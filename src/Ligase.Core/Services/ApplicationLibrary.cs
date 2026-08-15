using System.Text.Json;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public sealed class ApplicationLibrary(
    LigasePaths paths,
    IApolloAppsWriter apolloAppsWriter,
    StreamingSettingsService? streamingSettings = null,
    LigaseSyncDocumentWriter? syncWriter = null) : IApplicationLibrary
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
        string? coverImagePath = null,
        CancellationToken cancellationToken = default) =>
        MutateAsync(state =>
        {
            var existing = state.Items.FirstOrDefault(item =>
                item.Kind == LibraryItemKind.Steam && item.SteamAppId == game.AppId);
            if (existing is not null) return existing;

            var coverAuthority = CoverArtService.TryReadAuthority(paths, coverImagePath);
            if (!string.IsNullOrWhiteSpace(coverImagePath) &&
                (coverAuthority is null ||
                 coverAuthority.SteamAppId != game.AppId ||
                 !string.Equals(coverAuthority.SourceKind,
                     "steamClientLibraryCache", StringComparison.Ordinal) ||
                 !string.Equals(coverAuthority.SourceId,
                     game.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    "所选封面没有绑定当前 Steam App ID 的可验证来源，未修改游戏库。");
            var item = new LibraryItem
            {
                Kind = LibraryItemKind.Steam,
                Name = game.Name,
                SteamAppId = game.AppId,
                SteamInstallPath = game.InstallPath,
                PortableIdentity = new PortableGameIdentityV1(
                    "steam",
                    game.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                CoverImagePath = coverImagePath,
                CoverContentSha256 = coverAuthority?.ContentSha256,
                CoverSourceKind = coverAuthority?.SourceKind,
                CoverSourceId = coverAuthority?.SourceId,
                CoverUsageRights = coverAuthority?.UsageRights
            };
            state.Items.Add(item);
            return item;
        }, cancellationToken);

    public Task<LibraryItem> AddExecutableAsync(
        string name,
        string executablePath,
        string? arguments,
        string? workingDirectory,
        string? coverImagePath = null,
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

            var coverAuthority = CoverArtService.TryReadAuthority(paths, coverImagePath);
            var item = new LibraryItem
            {
                Kind = LibraryItemKind.Executable,
                Name = name.Trim(),
                ExecutablePath = fullExecutablePath,
                Arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim(),
                CoverImagePath = coverImagePath,
                CoverContentSha256 = coverAuthority?.ContentSha256,
                CoverSourceKind = coverAuthority?.SourceKind,
                CoverSourceId = coverAuthority?.SourceId,
                CoverUsageRights = coverAuthority?.UsageRights,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                    ? Path.GetDirectoryName(fullExecutablePath)
                    : Path.GetFullPath(workingDirectory)
            };
            state.Items.Add(item);
            return item;
        }, cancellationToken);

    public Task<LibraryState> SetManualOrderAsync(
        long baseRevision,
        IReadOnlyList<Guid> orderedPublishedAppIds,
        CancellationToken cancellationToken = default) =>
        MutateAsync(state =>
        {
            if (baseRevision is < 1 or > 9_007_199_254_740_991)
                throw new InvalidLibraryRevisionException(baseRevision);
            if (state.Revision != baseRevision)
                throw new LibraryRevisionConflictException(baseRevision, state.Revision);

            var published = state.Items
                .Where(item => item.PublishedToClients)
                .ToDictionary(item => item.Id);
            if (orderedPublishedAppIds.Count != published.Count ||
                orderedPublishedAppIds.Distinct().Count() != orderedPublishedAppIds.Count ||
                orderedPublishedAppIds.Any(id => !published.ContainsKey(id)))
                throw new InvalidManualLibraryOrderException();

            var hidden = state.Items
                .Where(item => !item.PublishedToClients)
                .ToArray();
            state.SortMode = LibrarySortMode.Manual;
            var ordered = orderedPublishedAppIds.Select(id => published[id]).Concat(hidden).ToArray();
            state.Items.Clear();
            state.Items.AddRange(ordered);
            return state;
        }, cancellationToken);

    public async Task SetPublishedToClientsAsync(
        Guid id,
        bool published,
        CancellationToken cancellationToken = default)
    {
        await MutateAsync<object?>(state =>
        {
            var item = state.Items.SingleOrDefault(candidate => candidate.Id == id)
                       ?? throw new LibraryItemNotFoundException(id);
            if (item.IsSystemEntry)
                throw new SystemLibraryItemMutationException(id, "系统桌面入口不能隐藏。");

            item.PublishedToClients = published;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            return null;
        }, cancellationToken);
    }

    public Task<LibraryItem> SetLayoutBindingAsync(
        Guid id,
        LayoutBindingV1? binding,
        CancellationToken cancellationToken = default) =>
        MutateAsync(state =>
        {
            var item = state.Items.SingleOrDefault(candidate => candidate.Id == id)
                       ?? throw new LibraryItemNotFoundException(id);
            if (item.IsSystemEntry)
                throw new SystemLibraryItemMutationException(
                    id,
                    "系统桌面入口不能绑定触控布局。");
            if (binding is not null &&
                (!LayoutContractV1Validator.TryNormalizeUuid(
                     binding.LayoutId,
                     out var layoutId) ||
                 !string.Equals(layoutId, binding.LayoutId, StringComparison.Ordinal) ||
                 !LayoutContractV1Validator.IsValidRevision(binding.Revision)))
                throw new ArgumentException("布局绑定不是 canonical layout ID/revision。", nameof(binding));

            item.LayoutBinding = binding;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            return item;
        }, cancellationToken);

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await MutateAsync<object?>(state =>
        {
            var item = state.Items.SingleOrDefault(candidate => candidate.Id == id)
                       ?? throw new LibraryItemNotFoundException(id);
            if (item.IsSystemEntry)
                throw new SystemLibraryItemMutationException(id, "系统桌面入口不能删除。");

            state.Items.Remove(item);
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
            ApplyCanonicalOrder(state);
            state.Revision++;
            state.UpdatedAt = DateTimeOffset.UtcNow;
            await WriteCoreAsync(state, cancellationToken);
            await apolloAppsWriter.WriteAsync(state.Items, cancellationToken);
            if (syncWriter is not null)
            {
                var streaming = streamingSettings is null
                    ? new StreamingSettingsState()
                    : await streamingSettings.LoadAsync(cancellationToken);
                await syncWriter.WriteAsync(state, streaming, cancellationToken);
            }
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
            await WriteCoreAsync(initialState, cancellationToken);
            return initialState;
        }

        await using var stream = File.OpenRead(paths.LibraryFile);
        var state = await JsonSerializer.DeserializeAsync<LibraryState>(stream, JsonOptions, cancellationToken)
                    ?? new LibraryState();
        ValidateAndPopulateLayoutMetadata(state);
        EnsureSystemEntries(state);
        ApplyCanonicalOrder(state);
        return state;
    }

    private static void ValidateAndPopulateLayoutMetadata(LibraryState state)
    {
        foreach (var item in state.Items)
        {
            if (item.Kind == LibraryItemKind.Steam && item.SteamAppId is > 0)
            {
                var expected = new PortableGameIdentityV1(
                    "steam",
                    item.SteamAppId.GetValueOrDefault().ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                if (item.PortableIdentity is null)
                    item.PortableIdentity = expected;
                else if (!LayoutContractV1Validator.TryNormalizePortableIdentity(
                             item.PortableIdentity,
                             out var normalized) ||
                         normalized != expected)
                    throw new InvalidDataException(
                        $"游戏 {item.Id:D} 的可移植身份与已验证 Steam App ID 不一致。");
            }
            else if (item.PortableIdentity is not null)
            {
                throw new InvalidDataException(
                    $"游戏 {item.Id:D} 不能声明可移植 Steam 身份。");
            }

            if (item.LayoutBinding is { } binding &&
                (!LayoutContractV1Validator.TryNormalizeUuid(
                     binding.LayoutId,
                     out var layoutId) ||
                 !string.Equals(layoutId, binding.LayoutId, StringComparison.Ordinal) ||
                 !LayoutContractV1Validator.IsValidRevision(binding.Revision)))
                throw new InvalidDataException($"游戏 {item.Id:D} 的布局绑定无效。");
        }
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

    internal static void ApplyCanonicalOrder(LibraryState state)
    {
        var ordered = state.Items
            .Where(item => item.PublishedToClients)
            .Concat(state.Items.Where(item => !item.PublishedToClients))
            .ToList();
        state.Items.Clear();
        state.Items.AddRange(ordered);
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
                LastPlayedAt = existing.LastPlayedAt,
                PublishedToClients = true
            };
            return;
        }

        state.Items.Add(new LibraryItem
        {
            Id = id,
            Kind = kind,
            Name = name,
            AddedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        });
    }
}

public sealed class LibraryItemNotFoundException(Guid id)
    : InvalidOperationException($"找不到游戏库项目 {id}。");

public sealed class SystemLibraryItemMutationException(Guid id, string message)
    : InvalidOperationException(message)
{
    public Guid Id { get; } = id;
}

public sealed class InvalidLibraryRevisionException(long revision)
    : InvalidOperationException($"游戏库版本 {revision} 超出安全整数范围。");

public sealed class LibraryRevisionConflictException(long expected, long actual)
    : InvalidOperationException("游戏库已在其他位置更新，请刷新后重新排序。")
{
    public long ExpectedRevision { get; } = expected;
    public long ActualRevision { get; } = actual;
}

public sealed class InvalidManualLibraryOrderException()
    : InvalidOperationException("手动顺序必须完整包含当前所有已发布项目，且不能重复。");
