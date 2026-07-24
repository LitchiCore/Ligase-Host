using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.UI.Xaml;

namespace Ligase.Host.Desktop.ViewModels;

public enum LibraryViewSortMode
{
    Manual,
    NameAscending,
    NameDescending,
    AddedNewest,
    AddedOldest,
    LastPlayedNewest
}

public sealed record LibrarySortOption(string Label, LibraryViewSortMode Value);

public partial class GameLibraryViewModel(
    IApplicationLibrary applicationLibrary,
    ILibraryAuthorityService authorityService,
    LibraryMutationCoordinator mutationCoordinator) : ObservableObject
{
    private IReadOnlyList<LibraryItem> _allItems = [];
    private long _libraryRevision;

    public ObservableCollection<LibraryItem> FilteredItems { get; } = [];
    public IReadOnlyList<LibrarySortOption> SortOptions { get; } =
    [
        new("手动排序（同步）", LibraryViewSortMode.Manual),
        new("名称 A–Z（仅此窗口）", LibraryViewSortMode.NameAscending),
        new("名称 Z–A（仅此窗口）", LibraryViewSortMode.NameDescending),
        new("最近添加（仅此窗口）", LibraryViewSortMode.AddedNewest),
        new("最早添加（仅此窗口）", LibraryViewSortMode.AddedOldest),
        new("最近启动（仅此窗口）", LibraryViewSortMode.LastPlayedNewest)
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuthorityWarning))]
    private string? _authorityMessage;

    [ObservableProperty]
    private bool _canModifyLibrary;

    public bool IsNotLoading => !IsLoading;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasAuthorityWarning => !string.IsNullOrWhiteSpace(AuthorityMessage);
    public int ItemCount => FilteredItems.Count;
    public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => !IsLoading && FilteredItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public string EmptyTitle => _allItems.Count == 0 ? "游戏库还是空的" : "没有匹配的项目";
    public string EmptyDescription => _allItems.Count == 0
        ? "添加 Steam 游戏或本地应用后，它们会出现在这里并同步到 Ligase 的 Apollo 核心。"
        : "尝试缩短关键词，或切换排序方式。";
    public string SortDescription => SelectedSortOption?.Value == LibraryViewSortMode.Manual
        ? "使用每个项目右侧的上移、下移按钮调整共享顺序；桌面入口也可以移动。"
        : "名称、添加时间和最近游玩只改变此窗口的查看方式，不会覆盖共享手动顺序。";

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var state = await applicationLibrary.LoadAsync(cancellationToken);
            var authority = await authorityService.GetStateAsync(cancellationToken);
            CanModifyLibrary = authority.CanWrite;
            AuthorityMessage = authority.CanWrite ? null : authority.Message;
            _allItems = state.Items.ToArray();
            _libraryRevision = state.Revision;
            SelectedSortOption ??= SortOptions.First(option =>
                option.Value == (state.SortMode == LibrarySortMode.Manual
                    ? LibraryViewSortMode.Manual
                    : LibraryViewSortMode.NameAscending));
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

    public async Task<bool> EnsureWritableAsync(CancellationToken cancellationToken = default)
    {
        var authority = await authorityService.GetStateAsync(cancellationToken);
        CanModifyLibrary = authority.CanWrite;
        AuthorityMessage = authority.CanWrite ? null : authority.Message;
        return authority.CanWrite;
    }

    public async Task<bool> RemoveAsync(
        LibraryItem item,
        CancellationToken cancellationToken = default)
    {
        if (item.IsSystemEntry)
        {
            ErrorMessage = "系统桌面入口需要始终保留，不能删除。";
            return false;
        }

        if (!await EnsureWritableAsync(cancellationToken)) return false;

        try
        {
            await mutationCoordinator.RemoveAsync(item.Id, cancellationToken);
            await RefreshAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = exception.Message;
            return false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyView();

    partial void OnSelectedSortOptionChanged(LibrarySortOption? value)
    {
        if (value is null) return;
        OnPropertyChanged(nameof(SortDescription));
        ApplyView();
    }

    public async Task MoveManualAsync(
        LibraryItem item,
        int direction,
        CancellationToken cancellationToken = default)
    {
        if (!item.PublishedToClients || !await EnsureWritableAsync(cancellationToken)) return;
        try
        {
            var published = _allItems
                .Where(candidate => candidate.PublishedToClients)
                .Select(candidate => candidate.Id)
                .ToList();
            var currentIndex = published.IndexOf(item.Id);
            if (currentIndex < 0) return;
            var targetIndex = Math.Clamp(currentIndex + direction, 0, published.Count - 1);
            if (targetIndex == currentIndex) return;
            published.RemoveAt(currentIndex);
            published.Insert(targetIndex, item.Id);
            SelectedSortOption = SortOptions.First(option =>
                option.Value == LibraryViewSortMode.Manual);
            await mutationCoordinator.SetManualOrderAsync(
                _libraryRevision, published, cancellationToken);
            await RefreshAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = exception.Message;
        }
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

        var pinned = matches
            .OrderBy(item => item.Kind == LibraryItemKind.Desktop ? 0 :
                item.Kind == LibraryItemKind.VirtualDesktop ? 1 : 2);
        var ordered = SelectedSortOption?.Value switch
        {
            LibraryViewSortMode.NameDescending => pinned
                .ThenByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id),
            LibraryViewSortMode.AddedNewest => pinned
                .ThenByDescending(item => item.AddedAt)
                .ThenBy(item => item.Id),
            LibraryViewSortMode.AddedOldest => pinned
                .ThenBy(item => item.AddedAt)
                .ThenBy(item => item.Id),
            LibraryViewSortMode.LastPlayedNewest => pinned
                .ThenByDescending(item => item.LastPlayedAt.HasValue)
                .ThenByDescending(item => item.LastPlayedAt)
                .ThenBy(item => item.Id),
            LibraryViewSortMode.Manual => matches,
            _ => pinned
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id)
        };

        FilteredItems.Clear();
        foreach (var item in ordered) FilteredItems.Add(item);
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
    }
}
