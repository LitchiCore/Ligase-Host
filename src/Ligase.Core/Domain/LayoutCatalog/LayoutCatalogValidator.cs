using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;

namespace Ligase.Host.Core.Domain.LayoutCatalog;

public static class LayoutCatalogValidator
{
    public static void Validate(LayoutCatalogSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != 1 ||
            snapshot.InstalledRevisions is null ||
            snapshot.ExplicitBindings is null)
        {
            throw new LayoutCatalogException(LayoutCatalogCodes.InvalidSchema);
        }

        var revisions = new HashSet<(string LayoutId, long Revision)>();
        foreach (var descriptor in snapshot.InstalledRevisions)
        {
            var detail = LayoutContractV1Validator.ValidateDescriptor(descriptor);
            if (detail is not null)
                throw new LayoutCatalogException(LayoutCatalogCodes.InvalidDescriptor, detail);
            if (!revisions.Add((descriptor.LayoutId, descriptor.Revision)))
                throw new LayoutCatalogException(
                    LayoutCatalogCodes.DuplicateRevision,
                    $"{descriptor.LayoutId}:{descriptor.Revision}");
        }

        var instances = new HashSet<(string HostUniqueId, string AppUuid)>();
        foreach (var catalogBinding in snapshot.ExplicitBindings)
        {
            if (catalogBinding is null ||
                catalogBinding.Instance is null ||
                catalogBinding.Binding is null ||
                !IsCanonicalUuid(catalogBinding.Instance.HostUniqueId) ||
                !IsCanonicalUuid(catalogBinding.Instance.AppUuid) ||
                !IsCanonicalUuid(catalogBinding.Binding.LayoutId) ||
                !LayoutContractV1Validator.IsValidRevision(catalogBinding.Binding.Revision))
            {
                throw new LayoutCatalogException(LayoutCatalogCodes.InvalidBinding);
            }

            if (!instances.Add((
                    catalogBinding.Instance.HostUniqueId,
                    catalogBinding.Instance.AppUuid)))
            {
                throw new LayoutCatalogException(
                    LayoutCatalogCodes.DuplicateBinding,
                    $"{catalogBinding.Instance.HostUniqueId}:{catalogBinding.Instance.AppUuid}");
            }
        }
    }

    private static bool IsCanonicalUuid(string value) =>
        LayoutContractV1Validator.TryNormalizeUuid(value, out var normalized) &&
        string.Equals(value, normalized, StringComparison.Ordinal);
}
