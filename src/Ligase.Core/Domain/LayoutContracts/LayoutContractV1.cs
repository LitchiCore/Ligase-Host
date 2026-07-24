namespace Ligase.Host.Core.Models;

public sealed record PortableGameIdentityV1(string Provider, string Id);

public sealed record LayoutBindingV1(string LayoutId, long Revision);

public sealed record LayoutRevisionV1(string LayoutId, long Revision);

public sealed record LayoutCompatibilityV1(
    int MinClientContractVersion,
    int MinLayoutRuntimeVersion);

public sealed record LayoutVariantV1(
    string VariantId,
    string InputProfile,
    IReadOnlyList<string> DeviceClasses,
    IReadOnlyList<string> Orientations);

public sealed record LayoutDescriptorV1(
    int SchemaVersion,
    string LayoutId,
    long Revision,
    IReadOnlyList<PortableGameIdentityV1> PortableIdentities,
    LayoutCompatibilityV1 Compatibility,
    string PublicationStatus,
    IReadOnlyList<LayoutVariantV1> Variants);

public sealed record LayoutPreferenceV1(
    string LayoutId,
    long Revision,
    string VariantId);

public sealed record LayoutResolutionContextV1(
    int ClientContractVersion,
    int LayoutRuntimeVersion,
    string InputProfile,
    string DeviceClass,
    string Orientation,
    IReadOnlySet<LayoutRevisionV1>? InstalledDrafts = null,
    LayoutPreferenceV1? Preference = null);

public sealed record LayoutResolutionRequestV1(
    string HostUniqueId,
    string AppUuid,
    PortableGameIdentityV1? PortableIdentity,
    LayoutBindingV1? LayoutBinding,
    LayoutResolutionContextV1 Context,
    IReadOnlyList<LayoutDescriptorV1> Descriptors);

public sealed record LayoutResolutionV1(
    string Code,
    string? Source = null,
    string? LayoutId = null,
    long? Revision = null,
    string? VariantId = null,
    string? Detail = null);

public static class LayoutContractV1Codes
{
    public const string Resolved = "resolved";
    public const string InvalidJson = "invalidJson";
    public const string InvalidSchema = "invalidSchema";
    public const string UnknownField = "unknownField";
    public const string InvalidRevision = "invalidRevision";
    public const string InvalidInstanceIdentity = "invalidInstanceIdentity";
    public const string InvalidContext = "invalidContext";
    public const string InvalidDescriptor = "invalidDescriptor";
    public const string InvalidBinding = "invalidBinding";
    public const string BindingNotFound = "bindingNotFound";
    public const string BindingRetired = "bindingRetired";
    public const string BindingDraftNotInstalled = "bindingDraftNotInstalled";
    public const string IncompatibleBinding = "incompatibleBinding";
    public const string InvalidSyncPortableIdentity = "invalidSyncPortableIdentity";
    public const string InputProfileDoesNotAutoMatch = "inputProfileDoesNotAutoMatch";
    public const string NoMatch = "noMatch";
    public const string NoCompatibleRevision = "noCompatibleRevision";
    public const string LayoutConflict = "layoutConflict";
    public const string NoEligibleVariant = "noEligibleVariant";
    public const string NeedsVariantSelection = "needsVariantSelection";
}
