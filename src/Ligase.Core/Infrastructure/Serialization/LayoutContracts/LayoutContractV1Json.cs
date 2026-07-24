using System.Text;
using System.Text.Json;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public static class LayoutContractV1Json
{
    public static LayoutResolutionV1 Resolve(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return new LayoutResolutionV1(LayoutContractV1Codes.InvalidJson);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new LayoutResolutionV1(LayoutContractV1Codes.InvalidSchema);

            var unknownFields = new List<string>();
            CollectUnknownFields(root, string.Empty, Shape.Request, unknownFields);
            if (unknownFields.Count > 0)
            {
                unknownFields.Sort(JsonPointerComparer.Instance);
                return new LayoutResolutionV1(
                    LayoutContractV1Codes.UnknownField,
                    Detail: unknownFields[0]);
            }

            if (HasInvalidRevisionToken(root))
                return new LayoutResolutionV1(LayoutContractV1Codes.InvalidRevision);

            if (!TryParseRequest(root, out var request))
                return new LayoutResolutionV1(LayoutContractV1Codes.InvalidSchema);

            return LayoutContractV1Resolver.Resolve(request);
        }
    }

    public static string SerializeResult(LayoutResolutionV1 result)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("code", result.Code);
            if (result.Source is not null) writer.WriteString("source", result.Source);
            if (result.LayoutId is not null) writer.WriteString("layoutId", result.LayoutId);
            if (result.Revision is not null) writer.WriteNumber("revision", result.Revision.Value);
            if (result.VariantId is not null) writer.WriteString("variantId", result.VariantId);
            if (result.Detail is not null) writer.WriteString("detail", result.Detail);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool TryParseRequest(
        JsonElement root,
        out LayoutResolutionRequestV1 request)
    {
        request = null!;
        if (!TryReadInt32(root, "schemaVersion", out var schemaVersion) ||
            schemaVersion != 1 ||
            !TryGetObject(root, "instance", out var instance) ||
            !TryReadString(instance, "hostUniqueId", out var hostUniqueId) ||
            !TryReadString(instance, "appUuid", out var appUuid) ||
            !TryGetObject(root, "context", out var contextElement) ||
            !TryParseContext(contextElement, out var context) ||
            !TryGetArray(root, "descriptors", out var descriptorElements))
        {
            return false;
        }

        PortableGameIdentityV1? portableIdentity = null;
        if (root.TryGetProperty("portableIdentity", out var portableElement) &&
            portableElement.ValueKind != JsonValueKind.Null &&
            !TryParsePortableIdentity(portableElement, out portableIdentity))
        {
            return false;
        }

        LayoutBindingV1? binding = null;
        if (root.TryGetProperty("layoutBinding", out var bindingElement) &&
            bindingElement.ValueKind != JsonValueKind.Null &&
            !TryParseBinding(bindingElement, out binding))
        {
            return false;
        }

        var descriptors = new List<LayoutDescriptorV1>();
        foreach (var descriptorElement in descriptorElements.EnumerateArray())
        {
            if (!TryParseDescriptor(descriptorElement, out var descriptor)) return false;
            descriptors.Add(descriptor);
        }

        request = new LayoutResolutionRequestV1(
            hostUniqueId,
            appUuid,
            portableIdentity,
            binding,
            context,
            descriptors);
        return true;
    }

    private static bool TryParseContext(
        JsonElement element,
        out LayoutResolutionContextV1 context)
    {
        context = null!;
        if (!TryReadInt32(element, "clientContractVersion", out var clientVersion) ||
            !TryReadInt32(element, "layoutRuntimeVersion", out var runtimeVersion) ||
            !TryReadString(element, "inputProfile", out var inputProfile) ||
            !TryReadString(element, "deviceClass", out var deviceClass) ||
            !TryReadString(element, "orientation", out var orientation))
        {
            return false;
        }

        var installedDrafts = new HashSet<LayoutRevisionV1>();
        if (element.TryGetProperty("installedDrafts", out var draftsElement))
        {
            if (draftsElement.ValueKind != JsonValueKind.Array) return false;
            foreach (var draftElement in draftsElement.EnumerateArray())
            {
                if (!TryReadString(draftElement, "layoutId", out var layoutId) ||
                    !TryReadRevision(draftElement, "revision", out var revision))
                    return false;
                installedDrafts.Add(new LayoutRevisionV1(layoutId, revision));
            }
        }

        LayoutPreferenceV1? preference = null;
        if (element.TryGetProperty("preferredVariant", out var preferenceElement) &&
            preferenceElement.ValueKind != JsonValueKind.Null)
        {
            if (!TryReadString(preferenceElement, "layoutId", out var layoutId) ||
                !TryReadRevision(preferenceElement, "revision", out var revision) ||
                !TryReadString(preferenceElement, "variantId", out var variantId))
                return false;
            preference = new LayoutPreferenceV1(layoutId, revision, variantId);
        }

        context = new LayoutResolutionContextV1(
            clientVersion,
            runtimeVersion,
            inputProfile,
            deviceClass,
            orientation,
            installedDrafts,
            preference);
        return true;
    }

    private static bool TryParseDescriptor(
        JsonElement element,
        out LayoutDescriptorV1 descriptor)
    {
        descriptor = null!;
        if (element.ValueKind != JsonValueKind.Object ||
            !TryReadInt32(element, "schemaVersion", out var schemaVersion) ||
            !TryReadString(element, "layoutId", out var layoutId) ||
            !TryReadRevision(element, "revision", out var revision) ||
            !TryGetArray(element, "portableIdentities", out var identityElements) ||
            !TryGetObject(element, "compatibility", out var compatibilityElement) ||
            !TryReadInt32(
                compatibilityElement,
                "minClientContractVersion",
                out var minClientVersion) ||
            !TryReadInt32(
                compatibilityElement,
                "minLayoutRuntimeVersion",
                out var minRuntimeVersion) ||
            !TryReadString(element, "publicationStatus", out var publicationStatus) ||
            !TryGetArray(element, "variants", out var variantElements))
        {
            return false;
        }

        var identities = new List<PortableGameIdentityV1>();
        foreach (var identityElement in identityElements.EnumerateArray())
        {
            if (!TryParsePortableIdentity(identityElement, out var identity)) return false;
            identities.Add(identity!);
        }

        var variants = new List<LayoutVariantV1>();
        foreach (var variantElement in variantElements.EnumerateArray())
        {
            if (!TryReadString(variantElement, "variantId", out var variantId) ||
                !TryReadString(variantElement, "inputProfile", out var inputProfile) ||
                !TryReadStringArray(variantElement, "deviceClasses", out var deviceClasses) ||
                !TryReadStringArray(variantElement, "orientations", out var orientations))
                return false;
            variants.Add(new LayoutVariantV1(
                variantId,
                inputProfile,
                deviceClasses,
                orientations));
        }

        descriptor = new LayoutDescriptorV1(
            schemaVersion,
            layoutId,
            revision,
            identities,
            new LayoutCompatibilityV1(minClientVersion, minRuntimeVersion),
            publicationStatus,
            variants);
        return true;
    }

    private static bool TryParsePortableIdentity(
        JsonElement element,
        out PortableGameIdentityV1? identity)
    {
        identity = null;
        if (element.ValueKind != JsonValueKind.Object ||
            !TryReadString(element, "provider", out var provider) ||
            !TryReadString(element, "id", out var id))
            return false;
        identity = new PortableGameIdentityV1(provider, id);
        return true;
    }

    private static bool TryParseBinding(JsonElement element, out LayoutBindingV1? binding)
    {
        binding = null;
        if (element.ValueKind != JsonValueKind.Object ||
            !TryReadString(element, "layoutId", out var layoutId) ||
            !TryReadRevision(element, "revision", out var revision))
            return false;
        binding = new LayoutBindingV1(layoutId, revision);
        return true;
    }

    private static bool HasInvalidRevisionToken(JsonElement root)
    {
        var revisions = new List<JsonElement>();
        CollectRevisionTokens(root, Shape.Request, revisions);
        return revisions.Any(element =>
            element.ValueKind != JsonValueKind.Number ||
            element.GetRawText().Contains('.') ||
            element.GetRawText().Contains('e', StringComparison.OrdinalIgnoreCase) ||
            !element.TryGetInt64(out var value) ||
            !LayoutContractV1Validator.IsValidRevision(value));
    }

    private static void CollectRevisionTokens(
        JsonElement element,
        Shape shape,
        ICollection<JsonElement> revisions)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        if (shape is Shape.Binding or Shape.Preference or Shape.Draft or Shape.Descriptor &&
            element.TryGetProperty("revision", out var revision))
        {
            revisions.Add(revision);
        }

        foreach (var property in element.EnumerateObject())
        {
            var childShape = ChildShape(shape, property.Name);
            if (childShape == Shape.None) continue;
            if (childShape is Shape.DescriptorArray or Shape.VariantArray or Shape.IdentityArray or Shape.DraftArray)
            {
                if (property.Value.ValueKind != JsonValueKind.Array) continue;
                var itemShape = ArrayItemShape(childShape);
                foreach (var item in property.Value.EnumerateArray())
                    CollectRevisionTokens(item, itemShape, revisions);
            }
            else
            {
                CollectRevisionTokens(property.Value, childShape, revisions);
            }
        }
    }

    private static void CollectUnknownFields(
        JsonElement element,
        string pointer,
        Shape shape,
        ICollection<string> unknownFields)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        var allowed = AllowedFields(shape);
        foreach (var property in element.EnumerateObject())
        {
            var propertyPointer = $"{pointer}/{EscapePointer(property.Name)}";
            if (!allowed.Contains(property.Name))
            {
                unknownFields.Add(propertyPointer);
                continue;
            }

            var childShape = ChildShape(shape, property.Name);
            if (childShape == Shape.None) continue;
            if (childShape is Shape.DescriptorArray or Shape.VariantArray or Shape.IdentityArray or Shape.DraftArray)
            {
                if (property.Value.ValueKind != JsonValueKind.Array) continue;
                var itemShape = ArrayItemShape(childShape);
                var index = 0;
                foreach (var item in property.Value.EnumerateArray())
                {
                    CollectUnknownFields(item, $"{propertyPointer}/{index}", itemShape, unknownFields);
                    index++;
                }
            }
            else
            {
                CollectUnknownFields(property.Value, propertyPointer, childShape, unknownFields);
            }
        }
    }

    private static IReadOnlySet<string> AllowedFields(Shape shape) => shape switch
    {
        Shape.Request => Set(
            "schemaVersion", "instance", "portableIdentity", "layoutBinding", "context", "descriptors"),
        Shape.Instance => Set("hostUniqueId", "appUuid"),
        Shape.Identity => Set("provider", "id"),
        Shape.Binding or Shape.Draft => Set("layoutId", "revision"),
        Shape.Context => Set(
            "clientContractVersion", "layoutRuntimeVersion", "inputProfile",
            "deviceClass", "orientation", "installedDrafts", "preferredVariant"),
        Shape.Preference => Set("layoutId", "revision", "variantId"),
        Shape.Descriptor => Set(
            "schemaVersion", "layoutId", "revision", "portableIdentities",
            "compatibility", "publicationStatus", "variants"),
        Shape.Compatibility => Set("minClientContractVersion", "minLayoutRuntimeVersion"),
        Shape.Variant => Set("variantId", "inputProfile", "deviceClasses", "orientations"),
        _ => Set()
    };

    private static Shape ChildShape(Shape shape, string property) => (shape, property) switch
    {
        (Shape.Request, "instance") => Shape.Instance,
        (Shape.Request, "portableIdentity") => Shape.Identity,
        (Shape.Request, "layoutBinding") => Shape.Binding,
        (Shape.Request, "context") => Shape.Context,
        (Shape.Request, "descriptors") => Shape.DescriptorArray,
        (Shape.Context, "installedDrafts") => Shape.DraftArray,
        (Shape.Context, "preferredVariant") => Shape.Preference,
        (Shape.Descriptor, "portableIdentities") => Shape.IdentityArray,
        (Shape.Descriptor, "compatibility") => Shape.Compatibility,
        (Shape.Descriptor, "variants") => Shape.VariantArray,
        _ => Shape.None
    };

    private static Shape ArrayItemShape(Shape shape) => shape switch
    {
        Shape.DescriptorArray => Shape.Descriptor,
        Shape.VariantArray => Shape.Variant,
        Shape.IdentityArray => Shape.Identity,
        Shape.DraftArray => Shape.Draft,
        _ => Shape.None
    };

    private static HashSet<string> Set(params string[] values) =>
        new(values, StringComparer.Ordinal);

    private static string EscapePointer(string value) =>
        value.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);

    private static bool TryReadString(
        JsonElement element,
        string property,
        out string value)
    {
        value = string.Empty;
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(property, out var propertyElement) &&
               propertyElement.ValueKind == JsonValueKind.String &&
               (value = propertyElement.GetString()!) is not null;
    }

    private static bool TryReadInt32(
        JsonElement element,
        string property,
        out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(property, out var propertyElement) &&
               propertyElement.ValueKind == JsonValueKind.Number &&
               !propertyElement.GetRawText().Contains('.') &&
               !propertyElement.GetRawText().Contains('e', StringComparison.OrdinalIgnoreCase) &&
               propertyElement.TryGetInt32(out value);
    }

    private static bool TryReadRevision(
        JsonElement element,
        string property,
        out long value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(property, out var propertyElement) &&
               propertyElement.ValueKind == JsonValueKind.Number &&
               propertyElement.TryGetInt64(out value);
    }

    private static bool TryGetObject(
        JsonElement element,
        string property,
        out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(property, out value) &&
               value.ValueKind == JsonValueKind.Object;
    }

    private static bool TryGetArray(
        JsonElement element,
        string property,
        out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(property, out value) &&
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

    private enum Shape
    {
        None,
        Request,
        Instance,
        Identity,
        Binding,
        Context,
        Preference,
        Draft,
        Descriptor,
        Compatibility,
        Variant,
        DescriptorArray,
        VariantArray,
        IdentityArray,
        DraftArray
    }

    private sealed class JsonPointerComparer : IComparer<string>
    {
        public static JsonPointerComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var leftRunes = left.EnumerateRunes().GetEnumerator();
            var rightRunes = right.EnumerateRunes().GetEnumerator();
            while (true)
            {
                var hasLeft = leftRunes.MoveNext();
                var hasRight = rightRunes.MoveNext();
                if (!hasLeft || !hasRight)
                    return hasLeft ? 1 : hasRight ? -1 : 0;
                var comparison = leftRunes.Current.Value.CompareTo(rightRunes.Current.Value);
                if (comparison != 0) return comparison;
            }
        }
    }
}
