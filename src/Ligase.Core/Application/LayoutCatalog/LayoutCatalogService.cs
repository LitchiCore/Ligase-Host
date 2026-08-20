using Ligase.Host.Core.Domain.LayoutCatalog;
using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;

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

    public async Task SetBindingAsync(
        string hostUniqueId,
        Guid appId,
        LayoutBindingV1? binding,
        CancellationToken cancellationToken = default)
    {
        if (!LayoutContractV1Validator.TryNormalizeUuid(hostUniqueId, out var hostId) ||
            !string.Equals(hostId, hostUniqueId, StringComparison.OrdinalIgnoreCase))
            throw new LayoutCatalogException(LayoutCatalogCodes.InvalidBinding, "hostUniqueId");

        var snapshot = await repository.LoadAsync(cancellationToken);
        if (binding is not null)
        {
            if (!LayoutContractV1Validator.TryNormalizeUuid(binding.LayoutId, out var layoutId) ||
                !string.Equals(layoutId, binding.LayoutId, StringComparison.Ordinal) ||
                !LayoutContractV1Validator.IsValidRevision(binding.Revision))
                throw new LayoutCatalogException(LayoutCatalogCodes.InvalidBinding, "binding");
            var descriptor = snapshot.InstalledRevisions.SingleOrDefault(candidate =>
                candidate.LayoutId == binding.LayoutId &&
                candidate.Revision == binding.Revision);
            if (descriptor is null || descriptor.PublicationStatus == "retired")
                throw new LayoutCatalogException(LayoutCatalogCodes.InvalidBinding, "targetUnavailable");
        }

        var appUuid = appId.ToString("D");
        var bindings = snapshot.ExplicitBindings
            .Where(candidate =>
                !string.Equals(
                    candidate.Instance.HostUniqueId,
                    hostId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    candidate.Instance.AppUuid,
                    appUuid,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (binding is not null)
        {
            bindings.Add(new LayoutCatalogBinding(
                new LayoutCatalogInstanceIdentity(hostId, appUuid),
                binding));
        }

        await repository.SaveAsync(
            snapshot with { ExplicitBindings = bindings },
            cancellationToken);
    }
}
