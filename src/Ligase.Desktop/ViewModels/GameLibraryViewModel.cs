using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.UI.Xaml;

namespace Ligase.Host.Desktop.ViewModels;

public sealed record LibrarySortOption(string Label, LibrarySortMode Value);

public partial class GameLibraryViewModel(IApplicationLibrary applicationLibrary) : ObservableObject
{
    private IReadOnlyList<LibraryItem> _allItems = [];

    public ObservableCollection<LibraryItem> FilteredItems { get; } = [];
    public IReadOnlyList<LibrarySortOption> SortOptions { get; } =
    [
        new("名称 A–Z", LibrarySortMode.NameAscending),
        new("名称 Z–A", LibrarySortMode.NameDescending),
        new("最近添加", LibrarySortMode.AddedNewest),
        new("最早添加", LibrarySortMode.AddedOldest),
        new("最近启动", LibrarySortMode.LastPlayedNewest)
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private LibrarySortOption? _selectedSortOption;

    public bool IsNotLoading => !IsLoading;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public int ItemCount => FilteredItems.Count;
    public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => !IsLoading && FilteredItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public string EmptyTitle => _allItems.Count == 0 ? "游戏库还是空的" : "没有匹配的项目";
    public string EmptyDescription => _allItems.Count == 0
        ? "添加 Steam 游戏或本地应用后，它们会出现在这里并同步到 Ligase 的 Apollo 核心。"
        : "尝试缩短关键词，或切换排序方式。";

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var state = await applicationLibrary.LoadAsync(cancellationToken);
            _allItems = state.Items;
            SelectedSortOption = SortOptions.First(option => option.Value == state.SortMode);
            ApplyView();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyView();

    async partial void OnSelectedSortOptionChanged(LibrarySortOption? value)
    {
        if (value is null) return;
        ApplyView();
        await applicationLibrary.SetSortModeAsync(value.Value);
    }

    private void ApplyView()
    {
        var query = SearchText.Trim();
        var matches = string.IsNullOrWhiteSpace(query)
            ? _allItems
            : _allItems.Where(item =>
                item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.LocationLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (item.SteamAppId?.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

        var sortMode = SelectedSortOption?.Value ?? LibrarySortMode.NameAscending;
        var pinned = matches
            .OrderBy(item => item.Kind == LibraryItemKind.Desktop ? 0 :
                item.Kind == LibraryItemKind.VirtualDesktop ? 1 : 2);
        var ordered = sortMode switch
        {
            LibrarySortMode.NameDescending => pinned.ThenByDescending(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            LibrarySortMode.AddedNewest => pinned.ThenByDescending(item => item.AddedAt),
            LibrarySortMode.AddedOldest => pinned.ThenBy(item => item.AddedAt),
            LibrarySortMode.LastPlayedNewest => pinned
                .ThenByDescending(item => item.LastPlayedAt.HasValue)
                .ThenByDescending(item => item.LastPlayedAt),
            _ => pinned.ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        FilteredItems.Clear();
        foreach (var item in ordered.ThenBy(item => item.Id)) FilteredItems.Add(item);
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
    }
}
