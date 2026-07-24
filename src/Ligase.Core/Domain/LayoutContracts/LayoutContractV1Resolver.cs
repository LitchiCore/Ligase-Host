using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public static class LayoutContractV1Resolver
{
    public static LayoutResolutionV1 Resolve(LayoutResolutionRequestV1 request)
    {
        if (!LayoutContractV1Validator.TryNormalizeUuid(request.HostUniqueId, out _) ||
            !LayoutContractV1Validator.TryNormalizeUuid(request.AppUuid, out _))
        {
            return Error(LayoutContractV1Codes.InvalidInstanceIdentity);
        }

        foreach (var descriptor in request.Descriptors
                     .OrderBy(candidate => candidate.LayoutId, StringComparer.Ordinal)
                     .ThenBy(candidate => candidate.Revision))
        {
            var descriptorError = LayoutContractV1Validator.ValidateDescriptor(descriptor);
            if (descriptorError is not null)
                return Error(LayoutContractV1Codes.InvalidDescriptor);
        }

        var duplicateRevision = request.Descriptors
            .GroupBy(descriptor => (descriptor.LayoutId, descriptor.Revision))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateRevision is not null)
            return Error(
                LayoutContractV1Codes.InvalidDescriptor,
                "duplicateRevision");

        var contextError = LayoutContractV1Validator.ValidateContext(request.Context);
        if (contextError is not null)
            return Error(LayoutContractV1Codes.InvalidContext);

        if (request.LayoutBinding is not null)
            return ResolveBinding(request.LayoutBinding, request.Context, request.Descriptors);

        if (request.PortableIdentity is not null &&
            !LayoutContractV1Validator.TryNormalizePortableIdentity(
                request.PortableIdentity,
                out _))
        {
            return Error(LayoutContractV1Codes.InvalidSyncPortableIdentity);
        }

        if (!string.Equals(request.Context.InputProfile, "touch", StringComparison.Ordinal))
            return Error(LayoutContractV1Codes.InputProfileDoesNotAutoMatch);
        if (request.PortableIdentity is null) return Error(LayoutContractV1Codes.NoMatch);
        LayoutContractV1Validator.TryNormalizePortableIdentity(
            request.PortableIdentity,
            out var portableIdentity);
        return ResolvePortable(portableIdentity, request.Context, request.Descriptors);
    }

    private static LayoutResolutionV1 ResolveBinding(
        LayoutBindingV1 binding,
        LayoutResolutionContextV1 context,
        IReadOnlyList<LayoutDescriptorV1> descriptors)
    {
        if (!LayoutContractV1Validator.TryNormalizeUuid(binding.LayoutId, out var layoutId) ||
            !string.Equals(layoutId, binding.LayoutId, StringComparison.Ordinal) ||
            !LayoutContractV1Validator.IsValidRevision(binding.Revision))
        {
            return Error(LayoutContractV1Codes.InvalidBinding);
        }

        var matches = descriptors
            .Where(descriptor =>
                descriptor.LayoutId == layoutId &&
                descriptor.Revision == binding.Revision)
            .ToArray();
        if (matches.Length == 0) return Error(LayoutContractV1Codes.BindingNotFound);
        var descriptor = matches[0];
        if (descriptor.PublicationStatus == "retired")
            return Error(LayoutContractV1Codes.BindingRetired);
        if (descriptor.PublicationStatus == "draft" &&
            !(context.InstalledDrafts?.Contains(new LayoutRevisionV1(layoutId, binding.Revision)) ?? false))
        {
            return Error(LayoutContractV1Codes.BindingDraftNotInstalled);
        }
        if (!IsVersionCompatible(descriptor, context))
            return Error(LayoutContractV1Codes.IncompatibleBinding);

        return ResolveVariant(descriptor, context, "binding");
    }

    private static LayoutResolutionV1 ResolvePortable(
        PortableGameIdentityV1 identity,
        LayoutResolutionContextV1 context,
        IReadOnlyList<LayoutDescriptorV1> descriptors)
    {
        var exactMatches = descriptors
            .Where(descriptor =>
                descriptor.PublicationStatus == "published" &&
                descriptor.PortableIdentities.Any(candidate => candidate == identity))
            .ToArray();

        var layoutIds = exactMatches
            .Select(descriptor => descriptor.LayoutId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (layoutIds.Length == 0) return Error(LayoutContractV1Codes.NoMatch);
        if (layoutIds.Length > 1)
            return Error(LayoutContractV1Codes.LayoutConflict);

        var compatible = exactMatches
            .Where(descriptor => IsVersionCompatible(descriptor, context))
            .OrderByDescending(descriptor => descriptor.Revision)
            .ToArray();
        if (compatible.Length == 0)
            return Error(LayoutContractV1Codes.NoCompatibleRevision);

        var descriptor = compatible.FirstOrDefault(
            candidate => EligibleVariants(candidate, context).Count > 0);
        if (descriptor is null)
            return Error(LayoutContractV1Codes.NoEligibleVariant);

        return ResolveVariant(descriptor, context, "portable");
    }

    private static LayoutResolutionV1 ResolveVariant(
        LayoutDescriptorV1 descriptor,
        LayoutResolutionContextV1 context,
        string source)
    {
        var variants = EligibleVariants(descriptor, context);
        if (variants.Count == 0)
            return Error(LayoutContractV1Codes.NoEligibleVariant);

        LayoutVariantV1? selected = null;
        if (variants.Count == 1)
        {
            selected = variants[0];
        }
        else if (context.Preference is not null &&
                 context.Preference.LayoutId == descriptor.LayoutId &&
                 context.Preference.Revision == descriptor.Revision)
        {
            selected = variants.SingleOrDefault(
                variant => variant.VariantId == context.Preference.VariantId);
        }

        if (selected is null)
        {
            return new LayoutResolutionV1(
                LayoutContractV1Codes.NeedsVariantSelection);
        }

        return new LayoutResolutionV1(
            LayoutContractV1Codes.Resolved,
            source,
            descriptor.LayoutId,
            descriptor.Revision,
            selected.VariantId);
    }

    private static IReadOnlyList<LayoutVariantV1> EligibleVariants(
        LayoutDescriptorV1 descriptor,
        LayoutResolutionContextV1 context) =>
        descriptor.Variants
            .Where(variant =>
                variant.InputProfile == context.InputProfile &&
                variant.DeviceClasses.Contains(context.DeviceClass, StringComparer.Ordinal) &&
                variant.Orientations.Contains(context.Orientation, StringComparer.Ordinal))
            .OrderBy(variant => variant.VariantId, StringComparer.Ordinal)
            .ToArray();

    private static bool IsVersionCompatible(
        LayoutDescriptorV1 descriptor,
        LayoutResolutionContextV1 context) =>
        descriptor.Compatibility.MinClientContractVersion <= context.ClientContractVersion &&
        descriptor.Compatibility.MinLayoutRuntimeVersion <= context.LayoutRuntimeVersion;

    private static LayoutResolutionV1 Error(string code, string? detail = null) =>
        new(code, Detail: detail);
}
