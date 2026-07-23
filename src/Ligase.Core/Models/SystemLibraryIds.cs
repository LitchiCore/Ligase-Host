namespace Ligase.Host.Core.Models;

public static class SystemLibraryIds
{
    public static readonly Guid Desktop =
        Guid.Parse("78A25216-F239-45BD-B4AA-F41C814066E9");

    // Keep this aligned with Apollo's VIRTUAL_DISPLAY_UUID so the library DTO
    // and GameStream applist expose the same stable application identity.
    public static readonly Guid VirtualDesktop =
        Guid.Parse("8902CB19-674A-403D-A587-41B092E900BA");
}
