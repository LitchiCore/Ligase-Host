using System.Text.Json.Serialization;

namespace Ligase.Host.Core.Models;

public sealed record ManagedPairingCore(
    LigaseEndpoint Endpoint,
    string HostUniqueId,
    string StartNonce,
    ushort BasePort)
{
    public string InstanceKey =>
        $"{StartNonce}|{HostUniqueId.ToLowerInvariant()}|{BasePort}";
}

public sealed record PairingSourceAddress(
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("scopeId")] uint? ScopeId);

public sealed record PendingPairingDevice(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("platform")] string Platform);

public sealed record PendingPairingRequest(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("device")] PendingPairingDevice Device,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("readyForApproval")] bool ReadyForApproval,
    [property: JsonPropertyName("safetyCode")] string SafetyCode,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("sourceAddress")] PairingSourceAddress SourceAddress);

public sealed record PendingPairingSnapshot(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("requests")]
    IReadOnlyList<PendingPairingRequest> Requests);

public sealed record PairingRequestStatus(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("failure")] string? Failure);

public sealed record AttendedPairingProjection(
    ManagedPairingCore Core,
    IReadOnlyList<PendingPairingRequest> Requests,
    DateTimeOffset ObservedAt);

public sealed record AttendedPairingReadyEvent(
    string InstanceKey,
    PendingPairingRequest Request);

public sealed record AttendedPairingRemovedEvent(
    string InstanceKey,
    string RequestId);
