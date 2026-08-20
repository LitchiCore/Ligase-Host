namespace Ligase.Host.Core.Models;

public sealed record LibraryMutationOutcomeDocument(
    int SchemaVersion,
    string Operation,
    string State,
    LibraryMutationAttempt Primary,
    LibraryMutationAttempt Rollback,
    DateTimeOffset WrittenAtUtc);

public sealed record LibraryMutationAttempt(
    string ResultCode,
    string Stage,
    string ReasonCode,
    int? HttpStatusCode,
    long? ElapsedMs)
{
    public static LibraryMutationAttempt NotAttempted { get; } =
        new("notAttempted", "none", "none", null, null);
}

public sealed record LibraryMutationPersistenceStatus(
    string ResultCode,
    string State,
    string ReasonCode)
{
    public static LibraryMutationPersistenceStatus NotAttempted { get; } =
        new("notAttempted", "none", "none");
}

public sealed record LibraryMutationSecondaryFailure(
    string Stage,
    string ReasonCode);

public static class LibraryMutationOutcomeSemanticValidator
{
    private static readonly HashSet<string> Operations =
    [
        "addSteam", "addExecutable", "setManualOrder", "setPublished", "remove",
        "setLayoutBinding", "updateSteamCover", "resetSteamCover"
    ];

    public static void Validate(LibraryMutationOutcomeDocument document)
    {
        if (document.SchemaVersion != 1 || !Operations.Contains(document.Operation))
            throw Invalid("schemaVersion or operation");
        ValidateAttempt(document.Primary, document.Operation, rollback: false);
        ValidateAttempt(document.Rollback, document.Operation, rollback: true);

        var primaryCompleted = Is(
            document.Primary, "completed", "reload.readback", "none", 200,
            elapsedRequired: true);
        var primaryNotAttempted = IsNotAttempted(document.Primary);
        var rollbackCompleted = Is(
            document.Rollback, "completed", "reload.readback", "none", 200,
            elapsedRequired: true);
        var rollbackNotAttempted = IsNotAttempted(document.Rollback);
        var validState = document.State switch
        {
            "committed" => primaryCompleted && rollbackNotAttempted,
            "rolledBack" => !primaryCompleted && !primaryNotAttempted && rollbackCompleted,
            "rollbackUnproven" => !primaryCompleted && !primaryNotAttempted &&
                                  !rollbackCompleted && !rollbackNotAttempted,
            _ => false
        };
        if (!validState) throw Invalid("state/attempt cross-field tuple");
    }

    public static void ValidateAttempt(
        LibraryMutationAttempt attempt,
        string operation,
        bool rollback = false)
    {
        if (!Operations.Contains(operation)) throw Invalid("operation");
        if (attempt.ElapsedMs is < 0 or > int.MaxValue) throw Invalid("elapsedMs");

        var valid =
            Is(attempt, "completed", "reload.readback", "none", 200, elapsedRequired: true) ||
            IsNotAttempted(attempt) ||
            Is(attempt, "rejected", "reload.precondition", "sessionActive", 409, true) ||
            Is(attempt, "rejected", "reload.requestValidation", "loopbackOnly", 403, true) ||
            Is(attempt, "rejected", "reload.authorityValidation", "authorityMismatch", 403, true) ||
            Is(attempt, "unavailable", "reload.authorityValidation", "authorityUnavailable", 404, true) ||
            Is(attempt, "failed", "reload.appCatalogReload", "reloadFailed", 500, true) ||
            Is(attempt, "failed", "reload.appCatalogParse", "catalogMalformed", 500, true) ||
            Is(attempt, "failed", "reload.appCatalogLoad", "catalogUnreadable", 500, true) ||
            Is(attempt, "failed", "reload.appCatalogLoad", "catalogLoadFailed", 500, true) ||
            Is(attempt, "failed", "reload.appCatalogSwap", "catalogSwapFailed", 500, true) ||
            Is(attempt, "timeout", "reload.transport", "deadlineExceeded", null, true) ||
            Is(attempt, "transportFailed", "reload.transport", "httpRequestFailed", null, true) ||
            IsStatus(attempt, "transportFailed", "reload.transport", "httpStatusFailure") ||
            Is(attempt, "invalidResponse", "reload.deserialize", "invalidJson", 200, true) ||
            IsKnownMetadataMismatch(attempt) ||
            IsStatus(attempt, "invalidResponse", "reload.response", "unexpectedHttpStatus") ||
            Is(attempt, "projectionMismatch", "reload.readback", "projectionMismatch", null) ||
            Is(attempt, "projectionMismatch", "rollback.verify", "projectionMismatch", null,
                elapsedOptional: true) ||
            IsMutationFailure(attempt, operation, rollback);
        if (!valid) throw Invalid("attempt tuple");
    }

    private static bool IsMutationFailure(
        LibraryMutationAttempt attempt,
        string operation,
        bool rollback) =>
        rollback
            ? Is(attempt, "failed", "rollback.mutation", "mutationFailed", null)
            : Is(attempt, "failed", $"{operation}.mutation", "mutationFailed", null);

    private static bool IsKnownMetadataMismatch(LibraryMutationAttempt attempt) =>
        attempt.ResultCode == "invalidResponse" &&
        attempt.Stage == "reload.semantic" &&
        attempt.ReasonCode == "reloadMetadataMismatch" &&
        attempt.HttpStatusCode is 200 or 403 or 404 or 409 or 500 &&
        ValidElapsed(attempt.ElapsedMs, required: true, optional: false);

    private static bool IsStatus(
        LibraryMutationAttempt attempt,
        string result,
        string stage,
        string reason) =>
        attempt.ResultCode == result && attempt.Stage == stage && attempt.ReasonCode == reason &&
        attempt.HttpStatusCode is >= 100 and <= 599 &&
        ValidElapsed(attempt.ElapsedMs, required: true, optional: false);

    private static bool IsNotAttempted(LibraryMutationAttempt attempt) =>
        Is(attempt, "notAttempted", "none", "none", null);

    private static bool Is(
        LibraryMutationAttempt attempt,
        string result,
        string stage,
        string reason,
        int? status,
        bool elapsedRequired = false,
        bool elapsedOptional = false) =>
        attempt.ResultCode == result && attempt.Stage == stage && attempt.ReasonCode == reason &&
        attempt.HttpStatusCode == status &&
        ValidElapsed(attempt.ElapsedMs, elapsedRequired, elapsedOptional);

    private static bool ValidElapsed(long? elapsed, bool required, bool optional) =>
        required ? elapsed is >= 0 and <= int.MaxValue :
        optional ? elapsed is null or (>= 0 and <= int.MaxValue) : elapsed is null;

    private static InvalidDataException Invalid(string field) =>
        new($"library mutation outcome semantic validation failed: {field}");
}
