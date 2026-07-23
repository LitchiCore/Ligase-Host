using System.Globalization;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public static class LayoutContractV1Validator
{
    public const long MaxRevision = 9_007_199_254_740_991;

    public static readonly IReadOnlySet<string> InputProfiles =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "touch", "gamepad", "keyboardMouse", "none"
        };

    public static readonly IReadOnlySet<string> DeviceClasses =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "phone", "tablet"
        };

    public static readonly IReadOnlySet<string> Orientations =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "portrait", "landscape"
        };

    public static readonly IReadOnlySet<string> PublicationStatuses =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "draft", "published", "retired"
        };

    public static bool TryNormalizeUuid(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            !Guid.TryParseExact(value, "D", out var parsed))
        {
            return false;
        }

        normalized = parsed.ToString("D");
        return true;
    }

    public static bool TryNormalizePortableIdentity(
        PortableGameIdentityV1? identity,
        out PortableGameIdentityV1 normalized)
    {
        normalized = new PortableGameIdentityV1(string.Empty, string.Empty);
        if (identity is null ||
            !string.Equals(identity.Provider, "steam", StringComparison.Ordinal) ||
            string.IsNullOrEmpty(identity.Id) ||
            identity.Id.Length > 10 ||
            identity.Id.Any(character => character is < '0' or > '9') ||
            identity.Id[0] == '0' ||
            !uint.TryParse(identity.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) ||
            appId == 0 ||
            !string.Equals(appId.ToString(CultureInfo.InvariantCulture), identity.Id, StringComparison.Ordinal))
        {
            return false;
        }

        normalized = new PortableGameIdentityV1("steam", identity.Id);
        return true;
    }

    public static string? ValidateDescriptor(LayoutDescriptorV1? descriptor)
    {
        if (descriptor is null) return "descriptor.null";
        if (descriptor.SchemaVersion != 1) return "descriptor.schemaVersion";
        if (!TryNormalizeUuid(descriptor.LayoutId, out var layoutId) ||
            !string.Equals(layoutId, descriptor.LayoutId, StringComparison.Ordinal))
            return "descriptor.layoutId";
        if (!IsValidRevision(descriptor.Revision)) return "descriptor.revision";
        if (descriptor.Compatibility is null ||
            descriptor.Compatibility.MinClientContractVersion <= 0 ||
            descriptor.Compatibility.MinLayoutRuntimeVersion <= 0)
            return "descriptor.compatibility";
        if (!PublicationStatuses.Contains(descriptor.PublicationStatus))
            return "descriptor.publicationStatus";
        if (descriptor.PortableIdentities is null) return "descriptor.portableIdentities";

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identity in descriptor.PortableIdentities)
        {
            if (!TryNormalizePortableIdentity(identity, out var normalized))
                return "descriptor.portableIdentity";
            if (!identities.Add($"{normalized.Provider}:{normalized.Id}"))
                return "descriptor.portableIdentityDuplicate";
        }

        if (descriptor.Variants is null || descriptor.Variants.Count == 0)
            return "descriptor.variants";
        var variantIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in descriptor.Variants)
        {
            if (variant is null) return "descriptor.variant";
            if (!TryNormalizeUuid(variant.VariantId, out var variantId) ||
                !string.Equals(variantId, variant.VariantId, StringComparison.Ordinal))
                return "descriptor.variantId";
            if (!variantIds.Add(variantId)) return "descriptor.variantIdDuplicate";
            if (!InputProfiles.Contains(variant.InputProfile))
                return "descriptor.inputProfile";
            if (!IsValidSet(variant.DeviceClasses, DeviceClasses))
                return "descriptor.deviceClasses";
            if (!IsValidSet(variant.Orientations, Orientations))
                return "descriptor.orientations";
        }

        return null;
    }

    public static string? ValidateContext(LayoutResolutionContextV1? context)
    {
        if (context is null ||
            context.ClientContractVersion <= 0 ||
            context.LayoutRuntimeVersion <= 0 ||
            !InputProfiles.Contains(context.InputProfile) ||
            !DeviceClasses.Contains(context.DeviceClass) ||
            !Orientations.Contains(context.Orientation))
        {
            return "context.invalid";
        }

        if (context.Preference is not null &&
            (!TryNormalizeUuid(context.Preference.LayoutId, out var layoutId) ||
             !string.Equals(layoutId, context.Preference.LayoutId, StringComparison.Ordinal) ||
            !IsValidRevision(context.Preference.Revision) ||
             !TryNormalizeUuid(context.Preference.VariantId, out var variantId) ||
             !string.Equals(variantId, context.Preference.VariantId, StringComparison.Ordinal)))
        {
            return "context.preference";
        }

        if (context.InstalledDrafts is not null &&
            context.InstalledDrafts.Any(draft =>
                !TryNormalizeUuid(draft.LayoutId, out var layoutId) ||
                !string.Equals(layoutId, draft.LayoutId, StringComparison.Ordinal) ||
                !IsValidRevision(draft.Revision)))
        {
            return "context.installedDrafts";
        }

        return null;
    }

    public static bool IsValidRevision(long revision) =>
        revision is >= 1 and <= MaxRevision;

    private static bool IsValidSet(IReadOnlyList<string>? values, IReadOnlySet<string> allowlist)
    {
        if (values is null || values.Count == 0) return false;
        var unique = new HashSet<string>(StringComparer.Ordinal);
        return values.All(value => allowlist.Contains(value) && unique.Add(value));
    }
}
