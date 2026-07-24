using Ligase.Host.Core.Domain.LayoutCatalog;

namespace Ligase.Host.Core.Application.LayoutCatalog;

public interface ILayoutCatalogRepository
{
    Task<LayoutCatalogSnapshot> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(
        LayoutCatalogSnapshot snapshot,
        CancellationToken cancellationToken = default);
}

public sealed class LayoutCatalogService(ILayoutCatalogRepository repository)
{
    public async Task<LayoutCatalogOverview> QueryAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await repository.LoadAsync(cancellationToken);
        var revisions = snapshot.InstalledRevisions
            .OrderBy(descriptor => descriptor.LayoutId, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.Revision)
            .ToArray();
        var byRevision = revisions.ToDictionary(
            descriptor => (descriptor.LayoutId, descriptor.Revision));
        var bindings = snapshot.ExplicitBindings
            .OrderBy(binding => binding.Instance.HostUniqueId, StringComparer.Ordinal)
            .ThenBy(binding => binding.Instance.AppUuid, StringComparer.Ordinal)
            .Select(binding =>
            {
                if (!byRevision.TryGetValue(
                        (binding.Binding.LayoutId, binding.Binding.Revision),
                        out var descriptor))
                {
                    return new LayoutCatalogBindingView(
                        binding,
                        LayoutCatalogBindingStatuses.Missing);
                }

                return new LayoutCatalogBindingView(
                    binding,
                    descriptor.PublicationStatus == "retired"
                        ? LayoutCatalogBindingStatuses.Retired
                        : LayoutCatalogBindingStatuses.Available);
            })
            .ToArray();
        return new LayoutCatalogOverview(revisions, bindings);
    }

    public Task ReplaceAsync(
        LayoutCatalogSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        LayoutCatalogValidator.Validate(snapshot);
        return repository.SaveAsync(snapshot, cancellationToken);
    }
}
