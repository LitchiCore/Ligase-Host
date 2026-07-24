using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.UI.Xaml;

namespace Ligase.Host.Desktop.ViewModels;

public sealed class SteamGameResultViewModel : INotifyPropertyChanged
{
    private Guid? _libraryItemId;
    private bool _isBusy;
    private bool _canModifyLibrary;

    public SteamGameResultViewModel(
        SteamGame game,
        Guid? libraryItemId = null,
        bool canModifyLibrary = false)
    {
        Game = game;
        _libraryItemId = libraryItemId;
        _canModifyLibrary = canModifyLibrary;
    }

    public SteamGame Game { get; }
    public uint AppId => Game.AppId;
    public string Name => Game.Name;
    public string InstallPath => Game.InstallPath;
    public string AppIdLabel => Game.AppIdLabel;
    public string SizeLabel => Game.SizeLabel;
    public Guid? LibraryItemId => _libraryItemId;
    public bool IsAdded => _libraryItemId.HasValue;
    public bool IsBusy => _isBusy;
    public bool CanMutate => _canModifyLibrary && !_isBusy;
    public string StatusText => _isBusy
        ? IsAdded ? "正在移除…" : "正在添加…"
        : IsAdded ? "已添加" : "可添加";
    public Visibility AddedVisibility => IsAdded && !_isBusy
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility AddVisibility => !IsAdded && !_isBusy
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility RemoveVisibility => IsAdded && !_isBusy
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BusyVisibility => _isBusy
        ? Visibility.Visible
        : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool TryBeginMutation()
    {
        if (_isBusy) return false;
        _isBusy = true;
        NotifyStateChanged();
        return true;
    }

    public void CompleteAdd(Guid libraryItemId)
    {
        _libraryItemId = libraryItemId;
        NotifyStateChanged();
    }

    public void CompleteRemove()
    {
        _libraryItemId = null;
        NotifyStateChanged();
    }

    public void EndMutation()
    {
        if (!_isBusy) return;
        _isBusy = false;
        NotifyStateChanged();
    }

    internal void ReconcileMembership(Guid? libraryItemId)
    {
        _libraryItemId = libraryItemId;
        NotifyStateChanged();
    }

    internal void SetCanModifyLibrary(bool value)
    {
        if (_canModifyLibrary == value) return;
        _canModifyLibrary = value;
        OnPropertyChanged(nameof(CanMutate));
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(LibraryItemId));
        OnPropertyChanged(nameof(IsAdded));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanMutate));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(AddedVisibility));
        OnPropertyChanged(nameof(AddVisibility));
        OnPropertyChanged(nameof(RemoveVisibility));
        OnPropertyChanged(nameof(BusyVisibility));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class SteamGameResultsViewModel : INotifyPropertyChanged
{
    private IReadOnlyList<SteamGameResultViewModel> _allResults = [];
    private string _searchText = string.Empty;
    private bool _canModifyLibrary;

    public ObservableCollection<SteamGameResultViewModel> AvailableGames { get; } = [];
    public ObservableCollection<SteamGameResultViewModel> AddedGames { get; } = [];
    public Visibility AvailableVisibility => AvailableGames.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility AddedVisibility => AddedGames.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility EmptyVisibility => AvailableGames.Count + AddedGames.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Reconcile(
        IReadOnlyList<SteamGame> games,
        IReadOnlyList<LibraryItem> libraryItems)
    {
        var membership = libraryItems
            .Where(item =>
                item.Kind == LibraryItemKind.Steam &&
                item.SteamAppId.HasValue)
            .GroupBy(item => item.SteamAppId!.Value)
            .ToDictionary(group => group.Key, group => (Guid?)group.First().Id);
        var existing = _allResults.ToDictionary(result => result.AppId);

        _allResults = games
            .GroupBy(game => game.AppId)
            .Select(group => group.First())
            .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(game => game.AppId)
            .Select(game =>
            {
                membership.TryGetValue(game.AppId, out var libraryItemId);
                if (!existing.TryGetValue(game.AppId, out var result))
                    return new SteamGameResultViewModel(
                        game,
                        libraryItemId,
                        _canModifyLibrary);

                result.ReconcileMembership(libraryItemId);
                return result;
            })
            .ToArray();

        ApplyFilter(_searchText);
    }

    public void SetCanModifyLibrary(bool value)
    {
        _canModifyLibrary = value;
        foreach (var result in _allResults)
            result.SetCanModifyLibrary(value);
    }

    public void ApplyFilter(string? searchText)
    {
        _searchText = searchText?.Trim() ?? string.Empty;
        var matches = _searchText.Length == 0
            ? _allResults
            : _allResults.Where(result =>
                SteamGameSearch.Matches(result.Game, _searchText));

        AvailableGames.Clear();
        AddedGames.Clear();
        foreach (var result in matches)
            (result.IsAdded ? AddedGames : AvailableGames).Add(result);
        NotifyGroupsChanged();
    }

    public void MarkAdded(SteamGameResultViewModel result, Guid libraryItemId)
    {
        result.CompleteAdd(libraryItemId);
        MoveBetweenGroups(result, AvailableGames, AddedGames);
    }

    public void MarkRemoved(SteamGameResultViewModel result)
    {
        result.CompleteRemove();
        MoveBetweenGroups(result, AddedGames, AvailableGames);
    }

    private void MoveBetweenGroups(
        SteamGameResultViewModel result,
        ObservableCollection<SteamGameResultViewModel> source,
        ObservableCollection<SteamGameResultViewModel> destination)
    {
        source.Remove(result);
        var insertionIndex = 0;
        while (insertionIndex < destination.Count &&
               Compare(destination[insertionIndex], result) < 0)
            insertionIndex++;
        destination.Insert(insertionIndex, result);
        NotifyGroupsChanged();
    }

    private static int Compare(
        SteamGameResultViewModel left,
        SteamGameResultViewModel right)
    {
        var name = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        return name != 0 ? name : left.AppId.CompareTo(right.AppId);
    }

    private void NotifyGroupsChanged()
    {
        OnPropertyChanged(nameof(AvailableVisibility));
        OnPropertyChanged(nameof(AddedVisibility));
        OnPropertyChanged(nameof(EmptyVisibility));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
