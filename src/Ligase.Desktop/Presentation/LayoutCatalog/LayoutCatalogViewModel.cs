using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ligase.Host.Core.Application.LayoutCatalog;
using Ligase.Host.Core.Domain.LayoutCatalog;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Desktop.Presentation.LayoutCatalog;

public sealed record LayoutCatalogDescriptorCard(
    string LayoutId,
    long Revision,
    string PublicationStatus,
    string PublicationStatusLabel,
    string CompatibilitySummary,
    string VariantSummary,
    string VariantDetails,
    string PortableIdentitySummary,
    string BindingSummary)
{
    public string RevisionLabel => $"修订 {Revision}";
    public string AutomationSummary =>
        $"{LayoutId}，{RevisionLabel}，{PublicationStatusLabel}，{VariantSummary}，{BindingSummary}";
}

public sealed record LayoutCatalogBindingAlert(
    string AppUuid,
    string LayoutId,
    long Revision,
    string Status,
    string StatusLabel)
{
    public string Summary =>
        $"应用 {AppUuid} · {LayoutId} / 修订 {Revision} · {StatusLabel}";
}

public partial class LayoutCatalogViewModel(LayoutCatalogService catalog)
    : ObservableObject
{
    public ObservableCollection<LayoutCatalogDescriptorCard> Items { get; } = [];
    public ObservableCollection<LayoutCatalogBindingAlert> BindingAlerts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    private LayoutCatalogDescriptorCard? _selectedItem;

    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => !IsLoading && !HasItems && !HasError;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var overview = await catalog.QueryAsync(cancellationToken);
            Apply(overview);
        }
        catch (LayoutCatalogException exception)
        {
            Items.Clear();
            BindingAlerts.Clear();
            SelectedItem = null;
            ErrorMessage =
                $"布局目录暂时无法读取（{exception.Code}）。请检查本机目录文件后重试；现有布局不会被修改。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Items.Clear();
            BindingAlerts.Clear();
            SelectedItem = null;
            ErrorMessage =
                $"布局目录暂时无法读取：{exception.Message}。请稍后重试；现有布局不会被修改。";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasError));
        }
    }

    private void Apply(LayoutCatalogOverview overview)
    {
        var bindingsByRevision = overview.ExplicitBindings
            .GroupBy(view => (
                view.Binding.Binding.LayoutId,
                view.Binding.Binding.Revision))
            .ToDictionary(group => group.Key, group => group.ToArray());

        Items.Clear();
        foreach (var descriptor in overview.InstalledRevisions)
        {
            bindingsByRevision.TryGetValue(
                (descriptor.LayoutId, descriptor.Revision),
                out var bindings);
            Items.Add(ToCard(descriptor, bindings ?? []));
        }

        BindingAlerts.Clear();
        foreach (var binding in overview.ExplicitBindings.Where(
                     view => view.Status != LayoutCatalogBindingStatuses.Available))
        {
            BindingAlerts.Add(new LayoutCatalogBindingAlert(
                binding.Binding.Instance.AppUuid,
                binding.Binding.Binding.LayoutId,
                binding.Binding.Binding.Revision,
                binding.Status,
                BindingStatusLabel(binding.Status)));
        }

        SelectedItem = Items.FirstOrDefault();
    }

    private static LayoutCatalogDescriptorCard ToCard(
        LayoutDescriptorV1 descriptor,
        IReadOnlyList<LayoutCatalogBindingView> bindings)
    {
        var deviceClasses = descriptor.Variants
            .SelectMany(variant => variant.DeviceClasses)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var orientations = descriptor.Variants
            .SelectMany(variant => variant.Orientations)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var inputProfiles = descriptor.Variants
            .Select(variant => variant.InputProfile)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var variantDetails = descriptor.Variants.Count == 0
            ? "没有变体"
            : string.Join(
                Environment.NewLine,
                descriptor.Variants.Select(variant =>
                    $"{variant.VariantId} · {variant.InputProfile} · " +
                    $"{string.Join("/", variant.DeviceClasses)} · " +
                    $"{string.Join("/", variant.Orientations)}"));
        var identitySummary = descriptor.PortableIdentities.Count == 0
            ? "没有可移植游戏身份"
            : string.Join(
                " · ",
                descriptor.PortableIdentities.Select(identity =>
                    $"{identity.Provider}:{identity.Id}"));
        var bindingSummary = bindings.Count == 0
            ? "没有显式绑定"
            : $"{bindings.Count} 个显式绑定 · " +
              string.Join(
                  " / ",
                  bindings.GroupBy(binding => binding.Status)
                      .OrderBy(group => group.Key, StringComparer.Ordinal)
                      .Select(group =>
                          $"{BindingStatusLabel(group.Key)} {group.Count()}"));

        return new LayoutCatalogDescriptorCard(
            descriptor.LayoutId,
            descriptor.Revision,
            descriptor.PublicationStatus,
            PublicationStatusLabel(descriptor.PublicationStatus),
            $"客户端契约 ≥ {descriptor.Compatibility.MinClientContractVersion} · " +
            $"布局运行时 ≥ {descriptor.Compatibility.MinLayoutRuntimeVersion}",
            $"{descriptor.Variants.Count} 个变体 · " +
            $"{string.Join("/", inputProfiles)} · " +
            $"{string.Join("/", deviceClasses)} · " +
            $"{string.Join("/", orientations)}",
            variantDetails,
            identitySummary,
            bindingSummary);
    }

    private static string PublicationStatusLabel(string status) => status switch
    {
        "draft" => "草稿 · draft",
        "published" => "已发布 · published",
        "retired" => "已停用 · retired",
        _ => status
    };

    private static string BindingStatusLabel(string status) => status switch
    {
        LayoutCatalogBindingStatuses.Available => "可用",
        LayoutCatalogBindingStatuses.Missing => "目标缺失",
        LayoutCatalogBindingStatuses.Retired => "目标已停用",
        _ => status
    };
}
