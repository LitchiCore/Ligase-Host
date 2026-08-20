using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.UI.Xaml;

namespace Ligase.Host.Desktop.ViewModels;

public partial class AddApplicationViewModel(
    ISteamLibraryService steamLibraryService,
    IApplicationLibrary applicationLibrary,
    ILibraryAuthorityService authorityService,
    LibraryMutationCoordinator mutationCoordinator,
    CoverArtService coverArtService,
    WindowsShortcutResolver shortcutResolver,
    IExistingItemCoverWorkflow? existingItemCoverWorkflow = null,
    WindowsShortcutLocalMatchService? shortcutMatchService = null) : ObservableObject
{
    private readonly WindowsShortcutLocalMatchService _shortcutMatchService =
        shortcutMatchService ?? new WindowsShortcutLocalMatchService();
    private (Guid LibraryItemId, uint SteamAppId)? _existingCoverTarget;
    private CoverCandidate? _pendingCoverCandidate;
    private bool _pendingDefaultCover;
    public SteamGameResultsViewModel SteamResults { get; } = new();
    public ObservableCollection<CoverCandidate> CoverCandidates { get; } = [];

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _steamSearchText = string.Empty;

    [ObservableProperty]
    private string _applicationName = string.Empty;

    [ObservableProperty]
    private string _executablePath = string.Empty;

    [ObservableProperty]
    private string _arguments = string.Empty;

    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    [ObservableProperty]
    private string _coverSearchText = string.Empty;

    [ObservableProperty]
    private string? _selectedCoverPreviewUrl;

    [ObservableProperty]
    private string? _selectedCoverPath;

    [ObservableProperty]
    private uint? _selectedCoverSteamAppId;

    [ObservableProperty]
    private bool _isSearchingCovers;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuthorityWarning))]
    private string? _authorityMessage;

    [ObservableProperty]
    private bool _canModifyLibrary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShortcutPreview))]
    [NotifyPropertyChangedFor(nameof(ShortcutPreviewVisibility))]
    [NotifyPropertyChangedFor(nameof(CanConfirmShortcut))]
    private WindowsShortcutPreview? _shortcutPreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutMatchVisibility))]
    [NotifyPropertyChangedFor(nameof(CanConfirmShortcut))]
    private WindowsShortcutLocalMatch? _shortcutMatch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmShortcut))]
    private bool _shortcutFallbackConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmShortcut))]
    private bool _isConfirmingShortcut;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool HasAuthorityWarning => !string.IsNullOrWhiteSpace(AuthorityMessage);
    public Visibility ScanningVisibility => IsScanning ? Visibility.Visible : Visibility.Collapsed;
    public bool HasShortcutPreview => ShortcutPreview is not null;
    public Visibility ShortcutPreviewVisibility => HasShortcutPreview
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility ShortcutMatchVisibility => ShortcutMatch is null
        ? Visibility.Collapsed
        : Visibility.Visible;
    public bool CanConfirmShortcut =>
        CanModifyLibrary && ShortcutPreview?.CanConfirmExecutable == true &&
        !IsConfirmingShortcut &&
        (ShortcutMatch?.RequiresExplicitFallbackConfirmation != true ||
         ShortcutFallbackConfirmed);
    public bool CanSaveCoverSelection =>
        (_pendingCoverCandidate is not null || _pendingDefaultCover) && !IsSearchingCovers;

    partial void OnSteamSearchTextChanged(string value) =>
        SteamResults.ApplyFilter(value);
    partial void OnApplicationNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(CoverSearchText) ||
            string.Equals(
                CoverSearchText,
                Path.GetFileNameWithoutExtension(ExecutablePath),
                StringComparison.OrdinalIgnoreCase))
            CoverSearchText = value;
    }
    partial void OnMessageChanged(string? value) => OnPropertyChanged(nameof(HasMessage));
    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(ScanningVisibility));
    partial void OnIsSearchingCoversChanged(bool value) =>
        OnPropertyChanged(nameof(CanSaveCoverSelection));
    partial void OnCanModifyLibraryChanged(bool value)
    {
        SteamResults.SetCanModifyLibrary(value);
        OnPropertyChanged(nameof(CanConfirmShortcut));
    }

    public async Task PreviewShortcutPathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        if (!PreviewShortcutCore(paths)) return;
        try
        {
            var games = await steamLibraryService.DiscoverGamesAsync(cancellationToken);
            ShortcutMatch = _shortcutMatchService.Inspect(ShortcutPreview!, games);
            Message = ShortcutMatch.Summary + " 请核对预览后明确确认添加。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShortcutMatch = null;
            Message = $"本机安装匹配暂不可用：{exception.Message} 未执行目标，也未修改游戏库。";
        }
    }

    private bool PreviewShortcutCore(IReadOnlyList<string> paths)
    {
        ShortcutPreview = null;
        ShortcutMatch = null;
        ShortcutFallbackConfirmed = false;
        if (paths.Count != 1)
        {
            Message = paths.Count == 0
                ? "请选择一个 Windows 快捷方式（.lnk）。"
                : "一次只能预览一个快捷方式；拖入不会自动修改游戏库。";
            return false;
        }

        var preview = shortcutResolver.Resolve(paths[0]);
        if (!preview.CanConfirmExecutable)
        {
            Message = preview.Kind == WindowsShortcutPreviewKind.SteamShortcut
                ? "Steam 快捷方式请使用 Steam 游戏页添加。"
                : DescribeShortcutFailure(preview.Code);
            return false;
        }

        ShortcutPreview = preview;
        Message = "快捷方式已安全解析。请核对目标、参数和工作目录，再明确确认添加。";
        return true;
    }

    public void CancelShortcutPreview()
    {
        ShortcutPreview = null;
        ShortcutMatch = null;
        ShortcutFallbackConfirmed = false;
        Message = "已取消快捷方式导入；游戏库未发生变化。";
    }

    public void SetShortcutFallbackConfirmation(bool confirmed) =>
        ShortcutFallbackConfirmed = confirmed;

    public async Task ConfirmShortcutAsync(CancellationToken cancellationToken = default)
    {
        if (ShortcutPreview is not { } pending || IsConfirmingShortcut) return;
        IsConfirmingShortcut = true;
        try
        {
            await EnsureWritableAsync(cancellationToken);
            var current = shortcutResolver.Revalidate(pending);
            if (!current.CanConfirmExecutable)
            {
                ShortcutPreview = null;
                throw new InvalidOperationException(
                    "快捷方式或目标在预览后发生变化。未写入游戏库，请重新选择并核对。");
            }

            var games = await steamLibraryService.DiscoverGamesAsync(cancellationToken);
            var currentMatch = _shortcutMatchService.Inspect(current, games);
            if (currentMatch != ShortcutMatch)
            {
                ShortcutPreview = null;
                ShortcutMatch = null;
                throw new InvalidOperationException(
                    "本机安装匹配在预览后发生变化。未写入游戏库，请重新选择并核对。");
            }

            if (currentMatch.Kind == WindowsShortcutMatchKind.ExactSteamInstall &&
                currentMatch.SteamAppId is uint appId)
            {
                var game = games.Single(value => value.AppId == appId);
                var coverPath = await ResolveAutomaticSteamCoverAsync(game, cancellationToken);
                await mutationCoordinator.AddSteamAsync(game, coverPath, cancellationToken);
            }
            else
            {
                if (currentMatch.RequiresExplicitFallbackConfirmation &&
                    !ShortcutFallbackConfirmed)
                    throw new InvalidOperationException(
                        "存在多个本机 Steam 候选；请先确认按普通本地应用添加。");
                await mutationCoordinator.AddExecutableAsync(
                    current.DisplayName,
                    current.TargetExecutable!,
                    current.Arguments,
                    current.WorkingDirectory,
                    SelectedCoverPath,
                    cancellationToken);
            }
            Message = $"已将“{current.DisplayName}”添加并同步到当前 Ligase 核心。";
            ShortcutPreview = null;
            ShortcutMatch = null;
            ShortcutFallbackConfirmed = false;
            ResetCoverSelection();
        }
        finally
        {
            IsConfirmingShortcut = false;
        }
    }

    private static string DescribeShortcutFailure(WindowsShortcutErrorCode code) => code switch
    {
        WindowsShortcutErrorCode.NotShortcut => "只接受单个 Windows 快捷方式（.lnk）。",
        WindowsShortcutErrorCode.ShortcutNotFound => "快捷方式不存在或已被移动。",
        WindowsShortcutErrorCode.NetworkLocation => "不接受网络位置中的快捷方式、目标或工作目录。",
        WindowsShortcutErrorCode.ReparsePoint => "快捷方式路径包含重解析点，无法建立安全的本机文件归属。",
        WindowsShortcutErrorCode.TargetMissing => "快捷方式指向的程序不存在。",
        WindowsShortcutErrorCode.CommandShellTarget or
        WindowsShortcutErrorCode.ScriptTarget or
        WindowsShortcutErrorCode.InstallerTarget => "该快捷方式指向脚本、命令解释器或安装器，不能加入游戏库。",
        _ => "无法安全解析该快捷方式；游戏库未发生变化。"
    };

    public async Task ScanSteamAsync(CancellationToken cancellationToken = default)
    {
        if (IsScanning) return;
        IsScanning = true;
        Message = null;
        try
        {
            var discovery = steamLibraryService.DiscoverGamesAsync(cancellationToken);
            var library = applicationLibrary.LoadAsync(cancellationToken);
            await Task.WhenAll(discovery, library);
            SteamResults.Reconcile(
                await discovery,
                (await library).Items);
            SteamResults.ApplyFilter(SteamSearchText);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Message = exception.Message;
        }
        finally
        {
            IsScanning = false;
        }
    }

    public async Task AddSteamAsync(
        SteamGameResultViewModel result,
        CancellationToken cancellationToken = default)
    {
        if (result.IsAdded || !result.TryBeginMutation()) return;
        try
        {
            await EnsureWritableAsync(cancellationToken);
            var coverPath = await ResolveAutomaticSteamCoverAsync(
                result.Game,
                cancellationToken);
            var item = await mutationCoordinator.AddSteamAsync(
                result.Game,
                coverPath,
                cancellationToken);
            SteamResults.MarkAdded(result, item.Id);
            Message = $"已将“{result.Name}”添加到游戏库。它现在位于下方“已添加”分组。";
            ResetCoverSelection();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await PruneCoverCacheAsync(CancellationToken.None);
            ResetCoverSelection();
            Message = $"未能添加“{result.Name}”：{exception.Message} 请确认 Host 核心正在运行后重试；其他搜索结果不受影响。";
        }
        finally
        {
            result.EndMutation();
        }
    }

    public async Task RemoveSteamAsync(
        SteamGameResultViewModel result,
        CancellationToken cancellationToken = default)
    {
        if (!result.IsAdded || result.LibraryItemId is not Guid libraryItemId ||
            !result.TryBeginMutation())
            return;

        try
        {
            await EnsureWritableAsync(cancellationToken);
            await mutationCoordinator.RemoveAsync(libraryItemId, cancellationToken);
            SteamResults.MarkRemoved(result);
            Message = $"已将“{result.Name}”从游戏库移除。Steam 游戏文件仍保留在电脑上。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Message = $"未能移除“{result.Name}”：{exception.Message} 项目仍保留在游戏库中，请刷新核心状态后重试。";
        }
        finally
        {
            result.EndMutation();
        }
    }

    public async Task AddExecutableAsync(CancellationToken cancellationToken = default)
    {
        await EnsureWritableAsync(cancellationToken);
        var coverPath = await ResolveAutomaticCoverAsync(ApplicationName, cancellationToken);
        await mutationCoordinator.AddExecutableAsync(
            ApplicationName,
            ExecutablePath,
            Arguments,
            WorkingDirectory,
            coverPath,
            cancellationToken);
        Message = $"已将“{ApplicationName.Trim()}”添加并同步到当前 Ligase 核心。";
        ResetCoverSelection();
    }

    public async Task SearchCoversAsync(CancellationToken cancellationToken = default)
    {
        var query = CoverSearchText.Trim();
        if (query.Length == 0) query = ApplicationName.Trim();
        if (query.Length == 0)
        {
            Message = "请先输入要搜索的游戏或应用名称。";
            return;
        }

        IsSearchingCovers = true;
        CoverCandidates.Clear();
        try
        {
            foreach (var candidate in await coverArtService.SearchAsync(query, cancellationToken))
                CoverCandidates.Add(candidate);
            Message = CoverCandidates.Count == 0
                ? "没有找到匹配封面；添加时会使用默认封面。"
                : $"找到 {CoverCandidates.Count} 张封面，请选择一张。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Message = $"暂时无法搜索封面：{exception.Message}";
        }
        finally
        {
            IsSearchingCovers = false;
        }
    }

    public async Task SearchCoversAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        CoverSearchText = name;
        await SearchCoversAsync(cancellationToken);
    }

    public async Task FindSteamCoverAsync(
        SteamGameResultViewModel result,
        CancellationToken cancellationToken = default)
    {
        IsSearchingCovers = true;
        CoverCandidates.Clear();
        SelectedCoverSteamAppId = result.AppId;
        SelectedCoverPath = null;
        SelectedCoverPreviewUrl = null;
        ClearPendingCover();
        try
        {
            IReadOnlyList<CoverCandidate> candidates;
            if (result.LibraryItemId is Guid libraryItemId)
            {
                if (existingItemCoverWorkflow is null)
                    throw new ExistingItemCoverUpdateException(
                        "existingCoverWorkflowUnavailable",
                        "现有游戏封面事务当前不可用，游戏库未发生变化。");
                _existingCoverTarget = (libraryItemId, result.AppId);
                candidates = await existingItemCoverWorkflow.FindVerifiedAsync(
                    libraryItemId,
                    result.AppId,
                    cancellationToken);
            }
            else
            {
                _existingCoverTarget = null;
                candidates = await coverArtService.FindSteamAsync(result.Game, cancellationToken);
            }

            foreach (var candidate in candidates)
                CoverCandidates.Add(candidate);
            Message = CoverCandidates.Count == 0
                ? $"Steam App ID {result.AppId} 没有可验证的本机 Library Capsule，将使用默认封面。"
                : result.IsAdded
                    ? $"已按 Steam App ID {result.AppId} 找到本机 Steam Library Capsule；选择后将持久化到现有游戏并同步。"
                    : $"已按 Steam App ID {result.AppId} 找到本机 Steam Library Capsule；请选择封面。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _existingCoverTarget = null;
            Message = $"无法读取 Steam App ID {result.AppId} 的封面：{exception.Message}";
        }
        finally
        {
            IsSearchingCovers = false;
        }
    }

    public Task SelectCoverAsync(
        CoverCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_existingCoverTarget is { } target && candidate.SteamAppId != target.SteamAppId)
            throw new ExistingItemCoverUpdateException(
                "coverRequestCorrelationMismatch",
                "该封面不属于当前 Steam 游戏，未保存。");
        _pendingCoverCandidate = candidate;
        _pendingDefaultCover = false;
        SelectedCoverPreviewUrl = candidate.PreviewUrl;
        SelectedCoverSteamAppId = candidate.SteamAppId;
        Message = $"正在预览“{candidate.Name}”。请确认保存，或选择默认封面/取消。";
        OnPropertyChanged(nameof(CanSaveCoverSelection));
        return Task.CompletedTask;
    }

    public void PreviewDefaultCover()
    {
        _pendingCoverCandidate = null;
        _pendingDefaultCover = true;
        SelectedCoverPreviewUrl = null;
        Message = "正在预览 Ligase 默认封面。请确认保存，或取消本次更改。";
        OnPropertyChanged(nameof(CanSaveCoverSelection));
    }

    public void CancelCoverSelection()
    {
        ClearPendingCover();
        _existingCoverTarget = null;
        Message = "已取消封面更改；游戏库和同步内容未发生变化。";
    }

    public async Task SaveCoverSelectionAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanSaveCoverSelection) return;
        IsSearchingCovers = true;
        try
        {
            if (_existingCoverTarget is { } target)
            {
                if (existingItemCoverWorkflow is null)
                    throw new ExistingItemCoverUpdateException(
                        "existingCoverWorkflowUnavailable",
                        "现有游戏封面事务当前不可用，游戏库未发生变化。");
                if (_pendingDefaultCover)
                {
                    var reset = await existingItemCoverWorkflow.ResetAsync(
                        target.LibraryItemId, target.SteamAppId, cancellationToken);
                    SelectedCoverPath = null;
                    SelectedCoverPreviewUrl = null;
                    SelectedCoverSteamAppId = target.SteamAppId;
                    Message = reset.Idempotent
                        ? $"当前已使用默认封面（库修订 {reset.LibraryRevision}）。"
                        : $"已恢复默认封面并完成 Host/Android 同步回读（库修订 {reset.LibraryRevision}）。";
                }
                else
                {
                    var candidate = _pendingCoverCandidate!;
                    var result = await existingItemCoverWorkflow.ApplyAsync(
                        target.LibraryItemId,
                        target.SteamAppId,
                        candidate,
                        cancellationToken);
                    SelectedCoverPath = result.CoverImagePath;
                    SelectedCoverPreviewUrl = new Uri(result.CoverImagePath).AbsoluteUri;
                    SelectedCoverSteamAppId = target.SteamAppId;
                    Message = result.Idempotent
                        ? $"“{candidate.Name}”已经是当前持久化封面（库修订 {result.LibraryRevision}）。"
                        : $"已持久化“{candidate.Name}”并完成 Host/Android 同步回读（库修订 {result.LibraryRevision}）。";
                }
                _existingCoverTarget = null;
            }
            else if (_pendingDefaultCover)
            {
                SelectedCoverPath = null;
                SelectedCoverPreviewUrl = null;
                SelectedCoverSteamAppId = null;
                Message = "添加时将使用 Ligase 默认封面；尚未写入游戏库。";
            }
            else
            {
                var candidate = _pendingCoverCandidate!;
                SelectedCoverPath = await coverArtService.DownloadAsync(
                    candidate, cancellationToken);
                SelectedCoverPreviewUrl = new Uri(SelectedCoverPath).AbsoluteUri;
                SelectedCoverSteamAppId = candidate.SteamAppId;
                Message = $"已准备“{candidate.Name}”；仅在确认添加游戏后写入游戏库。";
            }
            ClearPendingCover();
        }
        finally
        {
            IsSearchingCovers = false;
        }
    }

    private void ClearPendingCover()
    {
        _pendingCoverCandidate = null;
        _pendingDefaultCover = false;
        OnPropertyChanged(nameof(CanSaveCoverSelection));
    }

    public void SetExecutablePath(string path)
    {
        ExecutablePath = path;
        if (string.IsNullOrWhiteSpace(ApplicationName))
        {
            ApplicationName = Path.GetFileNameWithoutExtension(path);
        }
        if (string.IsNullOrWhiteSpace(WorkingDirectory))
        {
            WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty;
        }
    }

    public async Task RefreshAuthorityAsync(CancellationToken cancellationToken = default)
    {
        var authority = await authorityService.GetStateAsync(cancellationToken);
        CanModifyLibrary = authority.CanWrite;
        AuthorityMessage = authority.CanWrite ? null : authority.Message;
    }

    private async Task EnsureWritableAsync(CancellationToken cancellationToken)
    {
        await RefreshAuthorityAsync(cancellationToken);
        if (!CanModifyLibrary)
            throw new LibraryAuthorityException(
                "libraryReadOnly",
                AuthorityMessage ?? "当前游戏库暂时只读。请重新启动 Ligase Host。");
    }

    private async Task<string?> ResolveAutomaticCoverAsync(
        string name,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(SelectedCoverPath)) return SelectedCoverPath;
        try
        {
            var first = (await coverArtService.SearchAsync(name, cancellationToken)).FirstOrDefault();
            if (first is null)
            {
                Message = "未找到在线封面，将使用默认封面；稍后仍可更换。";
                return null;
            }
            return await coverArtService.DownloadAsync(first, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Message = $"封面自动匹配失败，将使用默认封面：{exception.Message}";
            return null;
        }
    }

    private async Task<string?> ResolveAutomaticSteamCoverAsync(
        SteamGame game,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(SelectedCoverPath) &&
            SelectedCoverSteamAppId == game.AppId)
            return SelectedCoverPath;
        try
        {
            var first = (await coverArtService.FindSteamAsync(game, cancellationToken))
                .SingleOrDefault();
            if (first is null)
            {
                Message = $"Steam App ID {game.AppId} 没有本机 Library Capsule，将使用默认封面。";
                return null;
            }
            return await coverArtService.DownloadAsync(first, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Message = $"Steam App ID {game.AppId} 封面导入失败，将使用默认封面：{exception.Message}";
            return null;
        }
    }

    private async Task PruneCoverCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = await applicationLibrary.LoadAsync(cancellationToken);
            await coverArtService.PruneUnreferencedAsync(state.Items, cancellationToken);
        }
        catch
        {
            // Cache cleanup is secondary and never changes the library transaction result.
        }
    }

    private void ResetCoverSelection()
    {
        ClearPendingCover();
        _existingCoverTarget = null;
        SelectedCoverPath = null;
        SelectedCoverPreviewUrl = null;
        SelectedCoverSteamAppId = null;
        CoverCandidates.Clear();
        CoverSearchText = string.Empty;
    }
}
