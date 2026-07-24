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
    CoverArtService coverArtService) : ObservableObject
{
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
    private bool _isSearchingCovers;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuthorityWarning))]
    private string? _authorityMessage;

    [ObservableProperty]
    private bool _canModifyLibrary;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool HasAuthorityWarning => !string.IsNullOrWhiteSpace(AuthorityMessage);
    public Visibility ScanningVisibility => IsScanning ? Visibility.Visible : Visibility.Collapsed;

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
    partial void OnCanModifyLibraryChanged(bool value) =>
        SteamResults.SetCanModifyLibrary(value);

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
            var coverPath = await ResolveAutomaticCoverAsync(
                result.Name,
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

    public async Task SelectCoverAsync(
        CoverCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        IsSearchingCovers = true;
        try
        {
            SelectedCoverPath = await coverArtService.DownloadAsync(candidate, cancellationToken);
            SelectedCoverPreviewUrl = new Uri(SelectedCoverPath).AbsoluteUri;
            Message = $"已选择“{candidate.Name}”的封面。";
        }
        finally
        {
            IsSearchingCovers = false;
        }
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

    private void ResetCoverSelection()
    {
        SelectedCoverPath = null;
        SelectedCoverPreviewUrl = null;
        CoverCandidates.Clear();
        CoverSearchText = string.Empty;
    }
}
