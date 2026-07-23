using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.UI.Xaml;

namespace Ligase.Host.Desktop.ViewModels;

public partial class AddApplicationViewModel(
    ISteamLibraryService steamLibraryService,
    IApplicationLibrary applicationLibrary) : ObservableObject
{
    private IReadOnlyList<SteamGame> _allSteamGames = [];

    public ObservableCollection<SteamGame> SteamGames { get; } = [];

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
    private string? _message;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public Visibility ScanningVisibility => IsScanning ? Visibility.Visible : Visibility.Collapsed;

    partial void OnSteamSearchTextChanged(string value) => ApplySteamFilter();
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
        await applicationLibrary.AddSteamAsync(game, cancellationToken);
        Message = $"已将“{game.Name}”添加到游戏库。";
    }

    public async Task AddExecutableAsync(CancellationToken cancellationToken = default)
    {
        await applicationLibrary.AddExecutableAsync(
            ApplicationName,
            ExecutablePath,
            Arguments,
            WorkingDirectory,
            cancellationToken);
        Message = $"已将“{ApplicationName.Trim()}”添加到游戏库。";
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
}
