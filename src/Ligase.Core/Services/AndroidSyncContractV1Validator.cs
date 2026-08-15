using System.Globalization;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public static class AndroidSyncContractV1Validator
{
    public const long MaxSafeInteger = 9_007_199_254_740_991;

    public static void Validate(LigaseSyncDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != 1 ||
            document.Library is null ||
            document.Streaming is null ||
            document.Library.Revision is < 0 or > MaxSafeInteger ||
            document.Streaming.Revision is < 0 or > MaxSafeInteger)
            throw new InvalidDataException("androidSync.invalidEnvelope");

        var ids = new HashSet<Guid>();
        foreach (var item in document.Library.Items)
        {
            if (!ids.Add(item.Id) || string.IsNullOrEmpty(item.Name) || item.Name.Length > 256)
                throw new InvalidDataException("androidSync.invalidItem");

            ValidatePortableIdentity(item);
            ValidateLayoutBinding(item.LayoutBinding);
            ValidateCover(item);
        }
    }

    private static void ValidatePortableIdentity(LibrarySyncItem item)
    {
        if (item.Kind == LibraryItemKind.Steam)
        {
            if (item.SteamAppId is null or 0 ||
                !LayoutContractV1Validator.TryNormalizePortableIdentity(
                    item.PortableIdentity,
                    out var normalized) ||
                !string.Equals(
                    normalized.Id,
                    item.SteamAppId.Value.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal))
                throw new InvalidDataException("androidSync.invalidPortableIdentity");
            return;
        }

        if (item.SteamAppId is not null || item.PortableIdentity is not null)
            throw new InvalidDataException("androidSync.invalidPortableIdentity");
    }

    private static void ValidateLayoutBinding(LayoutBindingV1? binding)
    {
        if (binding is null) return;
        if (!LayoutContractV1Validator.TryNormalizeUuid(binding.LayoutId, out var normalized) ||
            !string.Equals(normalized, binding.LayoutId, StringComparison.Ordinal) ||
            !LayoutContractV1Validator.IsValidRevision(binding.Revision))
            throw new InvalidDataException("androidSync.invalidLayoutBinding");
    }

    private static void ValidateCover(LibrarySyncItem item)
    {
        var values = new[]
        {
            item.CoverSha256,
            item.CoverSourceKind,
            item.CoverSourceId,
            item.CoverUsageRights
        };
        var present = values.Count(value => value is not null);
        if (present == 0) return;
        if (present != values.Length ||
            !IsLowerSha256(item.CoverSha256!) ||
            string.IsNullOrEmpty(item.CoverSourceId) ||
            item.CoverSourceId.Length > 128)
            throw new InvalidDataException("androidSync.invalidCoverAuthority");

        var valid = item.CoverSourceKind switch
        {
            "steamClientLibraryCache" =>
                item.Kind == LibraryItemKind.Steam &&
                item.SteamAppId is > 0 &&
                string.Equals(
                    item.CoverSourceId,
                    item.SteamAppId.Value.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal) &&
                item.CoverUsageRights == "thirdPartyArtworkLocalUseOnlyNoRedistribution",
            "gameDbIgdb" =>
                item.CoverUsageRights == "thirdPartyArtworkLocalCacheOnly",
            _ => false
        };
        if (!valid) throw new InvalidDataException("androidSync.invalidCoverAuthority");
    }

    public static bool IsLowerSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
