using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.UI.Xaml;

namespace Ligase.Host.Desktop.ViewModels;

public partial class AddApplicationViewModel(
    ISteamLibraryService steamLibraryService,
    IApplicationLibrary applicationLibrary,
    CoverArtService coverArtService) : ObservableObject
{
    private IReadOnlyList<SteamGame> _allSteamGames = [];

    public ObservableCollection<SteamGame> SteamGames { get; } = [];
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

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public Visibility ScanningVisibility => IsScanning ? Visibility.Visible : Visibility.Collapsed;

    partial void OnSteamSearchTextChanged(string value) => ApplySteamFilter();
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

    public async Task ScanSteamAsync(CancellationToken cancellationToken = default)
    {
        if (IsScanning) return;
        IsScanning = true;
        Message = null;
        try
        {
            _allSteamGames = await steamLibraryService.DiscoverGamesAsync(cancellationToken);
            ApplySteamFilter();
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

    public async Task AddSteamAsync(SteamGame game, CancellationToken cancellationToken = default)
    {
        var coverPath = await ResolveAutomaticCoverAsync(game.Name, cancellationToken);
        await applicationLibrary.AddSteamAsync(game, coverPath, cancellationToken);
        Message = $"已将“{game.Name}”添加到游戏库。";
        ResetCoverSelection();
    }

    public async Task AddExecutableAsync(CancellationToken cancellationToken = default)
    {
        var coverPath = await ResolveAutomaticCoverAsync(ApplicationName, cancellationToken);
        await applicationLibrary.AddExecutableAsync(
            ApplicationName,
            ExecutablePath,
            Arguments,
            WorkingDirectory,
            coverPath,
            cancellationToken);
        Message = $"已将“{ApplicationName.Trim()}”添加到游戏库。";
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

    private void ApplySteamFilter()
    {
        var query = SteamSearchText.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? _allSteamGames
            : _allSteamGames.Where(game => SteamGameSearch.Matches(game, query));
        SteamGames.Clear();
        foreach (var game in matches) SteamGames.Add(game);
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
