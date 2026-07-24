using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Domain.LayoutCatalog;

public sealed record LayoutCatalogInstanceIdentity(
    string HostUniqueId,
    string AppUuid);

public sealed record LayoutCatalogBinding(
    LayoutCatalogInstanceIdentity Instance,
    LayoutBindingV1 Binding);

public sealed record LayoutCatalogSnapshot(
    int SchemaVersion,
    IReadOnlyList<LayoutDescriptorV1> InstalledRevisions,
    IReadOnlyList<LayoutCatalogBinding> ExplicitBindings);

public sealed record LayoutCatalogBindingView(
    LayoutCatalogBinding Binding,
    string Status);

public sealed record LayoutCatalogOverview(
    IReadOnlyList<LayoutDescriptorV1> InstalledRevisions,
    IReadOnlyList<LayoutCatalogBindingView> ExplicitBindings);

public static class LayoutCatalogBindingStatuses
{
    public const string Available = "available";
    public const string Missing = "missing";
    public const string Retired = "retired";
}

public static class LayoutCatalogCodes
{
    public const string InvalidJson = "invalidJson";
    public const string UnknownField = "unknownField";
    public const string InvalidSchema = "invalidSchema";
    public const string InvalidDescriptor = "invalidDescriptor";
    public const string DuplicateRevision = "duplicateRevision";
    public const string InvalidBinding = "invalidBinding";
    public const string DuplicateBinding = "duplicateBinding";
    public const string AtomicWriteFailed = "atomicWriteFailed";
}

public sealed class LayoutCatalogException(
    string code,
    string? detail = null,
    Exception? innerException = null)
    : Exception(code, innerException)
{
    public string Code { get; } = code;
    public string? Detail { get; } = detail;
}
