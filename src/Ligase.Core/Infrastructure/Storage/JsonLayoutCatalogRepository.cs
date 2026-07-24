using System.Text;
using System.Text.Json;
using Ligase.Host.Core.Application.LayoutCatalog;
using Ligase.Host.Core.Domain.LayoutCatalog;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Infrastructure.Storage;

public sealed class JsonLayoutCatalogRepository : ILayoutCatalogRepository
{
    private readonly string _file;
    private readonly Func<CancellationToken, Task>? _afterReplace;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonLayoutCatalogRepository(string file)
        : this(file, null)
    {
    }

    internal JsonLayoutCatalogRepository(
        string file,
        Func<CancellationToken, Task>? afterReplace)
    {
        _file = Path.GetFullPath(file);
        _afterReplace = afterReplace;
    }

    public async Task<LayoutCatalogSnapshot> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadUnlockedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        LayoutCatalogSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        LayoutCatalogValidator.Validate(snapshot);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_file)
                ?? throw new LayoutCatalogException(LayoutCatalogCodes.AtomicWriteFailed);
            Directory.CreateDirectory(directory);
            var temporary = _file + ".tmp";
            var backup = _file + ".bak";
            var existed = File.Exists(_file);
            try
            {
                await File.WriteAllBytesAsync(
                    temporary,
                    Serialize(snapshot),
                    cancellationToken);
                if (existed)
                    File.Replace(temporary, _file, backup, true);
                else
                    File.Move(temporary, _file);

                if (_afterReplace is not null)
                    await _afterReplace(cancellationToken);

                var readBack = await LoadUnlockedAsync(cancellationToken);
                if (Serialize(readBack).AsSpan().SequenceEqual(Serialize(snapshot)))
                    return;
                throw new InvalidDataException("layout catalog read-back mismatch");
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException &&
                exception is not LayoutCatalogException)
            {
                Restore(existed, backup);
                throw new LayoutCatalogException(
                    LayoutCatalogCodes.AtomicWriteFailed,
                    innerException: exception);
            }
            catch (LayoutCatalogException exception)
            {
                Restore(existed, backup);
                throw new LayoutCatalogException(
                    LayoutCatalogCodes.AtomicWriteFailed,
                    innerException: exception);
            }
            finally
            {
                DeleteIfPresent(temporary);
                DeleteIfPresent(backup);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<LayoutCatalogSnapshot> LoadUnlockedAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_file))
            return new LayoutCatalogSnapshot(1, [], []);
        var bytes = await File.ReadAllBytesAsync(_file, cancellationToken);
        return Parse(bytes);
    }

    internal static LayoutCatalogSnapshot Parse(ReadOnlySpan<byte> utf8)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8.ToArray());
        }
        catch (JsonException exception)
        {
            throw new LayoutCatalogException(
                LayoutCatalogCodes.InvalidJson,
                innerException: exception);
        }

        using (document)
        {
            var root = document.RootElement;
            EnsureObject(root, "", "schemaVersion", "installedRevisions", "explicitBindings");
            if (!TryReadInt(root, "schemaVersion", out var schemaVersion) ||
                schemaVersion != 1 ||
                !TryGetArray(root, "installedRevisions", out var descriptorElements) ||
                !TryGetArray(root, "explicitBindings", out var bindingElements))
            {
                throw new LayoutCatalogException(LayoutCatalogCodes.InvalidSchema);
            }

            var descriptors = descriptorElements.EnumerateArray()
                .Select((element, index) => ParseDescriptor(
                    element,
                    $"/installedRevisions/{index}"))
                .ToArray();
            var bindings = bindingElements.EnumerateArray()
                .Select((element, index) => ParseBinding(
                    element,
                    $"/explicitBindings/{index}"))
                .ToArray();
            var snapshot = new LayoutCatalogSnapshot(
                schemaVersion,
                descriptors,
                bindings);
            LayoutCatalogValidator.Validate(snapshot);
            return snapshot;
        }
    }

    internal static byte[] Serialize(LayoutCatalogSnapshot snapshot)
    {
        LayoutCatalogValidator.Validate(snapshot);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WritePropertyName("installedRevisions");
            writer.WriteStartArray();
            foreach (var descriptor in snapshot.InstalledRevisions
                         .OrderBy(value => value.LayoutId, StringComparer.Ordinal)
                         .ThenBy(value => value.Revision))
            {
                WriteDescriptor(writer, descriptor);
            }
            writer.WriteEndArray();
            writer.WritePropertyName("explicitBindings");
            writer.WriteStartArray();
            foreach (var binding in snapshot.ExplicitBindings
                         .OrderBy(value => value.Instance.HostUniqueId, StringComparer.Ordinal)
                         .ThenBy(value => value.Instance.AppUuid, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("instance");
                writer.WriteStartObject();
                writer.WriteString("hostUniqueId", binding.Instance.HostUniqueId);
                writer.WriteString("appUuid", binding.Instance.AppUuid);
                writer.WriteEndObject();
                writer.WritePropertyName("binding");
                writer.WriteStartObject();
                writer.WriteString("layoutId", binding.Binding.LayoutId);
                writer.WriteNumber("revision", binding.Binding.Revision);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static LayoutDescriptorV1 ParseDescriptor(JsonElement element, string path)
    {
        EnsureObject(
            element,
            path,
            "schemaVersion",
            "layoutId",
            "revision",
            "portableIdentities",
            "compatibility",
            "publicationStatus",
            "variants");
        if (!TryReadInt(element, "schemaVersion", out var schemaVersion) ||
            !TryReadString(element, "layoutId", out var layoutId) ||
            !TryReadRevision(element, "revision", out var revision) ||
            !TryGetArray(element, "portableIdentities", out var identitiesElement) ||
            !element.TryGetProperty("compatibility", out var compatibilityElement) ||
            !TryReadString(element, "publicationStatus", out var publicationStatus) ||
            !TryGetArray(element, "variants", out var variantsElement))
        {
            throw new LayoutCatalogException(LayoutCatalogCodes.InvalidSchema);
        }

        EnsureObject(
            compatibilityElement,
            path + "/compatibility",
            "minClientContractVersion",
            "minLayoutRuntimeVersion");
        if (!TryReadInt(
                compatibilityElement,
                "minClientContractVersion",
                out var minClient) ||
            !TryReadInt(
                compatibilityElement,
                "minLayoutRuntimeVersion",
                out var minRuntime))
        {
            throw new LayoutCatalogException(LayoutCatalogCodes.InvalidSchema);
        }

        var identities = identitiesElement.EnumerateArray()
            .Select((identity, index) =>
            {
                EnsureObject(identity, $"{path}/portableIdentities/{index}", "provider", "id");
                if (!TryReadString(identity, "provider", out var provider) ||
                    !TryReadString(identity, "id", out var id))
                    throw new LayoutCatalogException(LayoutCatalogCodes.InvalidSchema);
                return new PortableGameIdentityV1(provider, id);
            })
            .ToArray();
        var variants = variantsElement.EnumerateArray()
            .Select((variant, index) =>
            {
                EnsureObject(
                    variant,
                    $"{path}/variants/{index}",
                    "variantId",
                    "inputProfile",
                    "deviceClasses",
                    "orientations");
                if (!TryReadString(variant, "variantId", out var variantId) ||
                    !TryReadString(variant, "inputProfile", out var inputProfile) ||
                    !TryReadStringArray(variant, "deviceClasses", out var deviceClasses) ||
                    !TryReadStringArray(variant, "orientations", out var orientations))
                    throw new LayoutCatalogException(LayoutCatalogCodes.InvalidSchema);
                return new LayoutVariantV1(
                    variantId,
                    inputProfile,
                    deviceClasses,
                    orientations);
            })
            .ToArray();
        return new LayoutDescriptorV1(
            schemaVersion,
            layoutId,
            revision,
            identities,
            new LayoutCompatibilityV1(minClient, minRuntime),
            publicationStatus,
            variants);
    }

    private static LayoutCatalogBinding ParseBinding(JsonElement element, string path)
    {
        EnsureObject(element, path, "instance", "binding");
        if (!element.TryGetProperty("instance", out var instance) ||
            !element.TryGetProperty("binding", out var binding))
            throw new LayoutCatalogException(LayoutCatalogCodes.InvalidSchema);
        EnsureObject(instance, path + "/instance", "hostUniqueId", "appUuid");
        EnsureObject(binding, path + "/binding", "layoutId", "revision");
        if (!TryReadString(instance, "hostUniqueId", out var hostUniqueId) ||
            !TryReadString(instance, "appUuid", out var appUuid) ||
            !TryReadString(binding, "layoutId", out var layoutId) ||
            !TryReadRevision(binding, "revision", out var revision))
            throw new LayoutCatalogException(LayoutCatalogCodes.InvalidSchema);
        return new LayoutCatalogBinding(
            new LayoutCatalogInstanceIdentity(hostUniqueId, appUuid),
            new LayoutBindingV1(layoutId, revision));
    }

    private static void WriteDescriptor(Utf8JsonWriter writer, LayoutDescriptorV1 value)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", value.SchemaVersion);
        writer.WriteString("layoutId", value.LayoutId);
        writer.WriteNumber("revision", value.Revision);
        writer.WritePropertyName("portableIdentities");
        writer.WriteStartArray();
        foreach (var identity in value.PortableIdentities
                     .OrderBy(item => item.Provider, StringComparer.Ordinal)
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("provider", identity.Provider);
            writer.WriteString("id", identity.Id);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("compatibility");
        writer.WriteStartObject();
        writer.WriteNumber(
            "minClientContractVersion",
            value.Compatibility.MinClientContractVersion);
        writer.WriteNumber(
            "minLayoutRuntimeVersion",
            value.Compatibility.MinLayoutRuntimeVersion);
        writer.WriteEndObject();
        writer.WriteString("publicationStatus", value.PublicationStatus);
        writer.WritePropertyName("variants");
        writer.WriteStartArray();
        foreach (var variant in value.Variants
                     .OrderBy(item => item.VariantId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("variantId", variant.VariantId);
            writer.WriteString("inputProfile", variant.InputProfile);
            WriteStringArray(writer, "deviceClasses", variant.DeviceClasses);
            WriteStringArray(writer, "orientations", variant.Orientations);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteStringArray(
        Utf8JsonWriter writer,
        string property,
        IEnumerable<string> values)
    {
        writer.WritePropertyName(property);
        writer.WriteStartArray();
        foreach (var value in values.OrderBy(item => item, StringComparer.Ordinal))
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static void EnsureObject(
        JsonElement element,
        string path,
        params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new LayoutCatalogException(LayoutCatalogCodes.InvalidSchema);
        var allowed = fields.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (element.EnumerateObject().Any(property => !seen.Add(property.Name)))
            throw new LayoutCatalogException(LayoutCatalogCodes.InvalidSchema);
        var unknown = element.EnumerateObject()
            .Select(property => property.Name)
            .Where(name => !allowed.Contains(name))
            .Select(name => $"{path}/{EscapePointer(name)}")
            .OrderBy(pointer => Encoding.UTF8.GetBytes(pointer), ByteArrayComparer.Instance)
            .FirstOrDefault();
        if (unknown is not null)
            throw new LayoutCatalogException(LayoutCatalogCodes.UnknownField, unknown);
    }

    private static bool TryReadString(
        JsonElement element,
        string property,
        out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(property, out var member) &&
               member.ValueKind == JsonValueKind.String &&
               (value = member.GetString()!) is not null;
    }

    private static bool TryReadInt(
        JsonElement element,
        string property,
        out int value)
    {
        value = 0;
        return element.TryGetProperty(property, out var member) &&
               member.ValueKind == JsonValueKind.Number &&
               IsIntegerToken(member) &&
               member.TryGetInt32(out value);
    }

    private static bool TryReadRevision(
        JsonElement element,
        string property,
        out long value)
    {
        value = 0;
        return element.TryGetProperty(property, out var member) &&
               member.ValueKind == JsonValueKind.Number &&
               IsIntegerToken(member) &&
               member.TryGetInt64(out value);
    }

    private static bool TryGetArray(
        JsonElement element,
        string property,
        out JsonElement value)
    {
        value = default;
        return element.TryGetProperty(property, out value) &&
               value.ValueKind == JsonValueKind.Array;
    }

    private static bool TryReadStringArray(
        JsonElement element,
        string property,
        out IReadOnlyList<string> values)
    {
        values = [];
        if (!TryGetArray(element, property, out var array)) return false;
        var result = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) return false;
            result.Add(item.GetString()!);
        }
        values = result;
        return true;
    }

    private static bool IsIntegerToken(JsonElement element)
    {
        var raw = element.GetRawText();
        return !raw.Contains('.') &&
               !raw.Contains('e', StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapePointer(string value) =>
        value.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);

    private void Restore(bool existed, string backup)
    {
        if (existed && File.Exists(backup))
            File.Replace(backup, _file, null, true);
        else if (!existed)
            DeleteIfPresent(_file);
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();

        public int Compare(byte[]? left, byte[]? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            return left.AsSpan().SequenceCompareTo(right);
        }
    }
}
