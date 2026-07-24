using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public interface IAttendedPairingRepository
{
    Task<AttendedPairingProjection> GetPendingAsync(
        CancellationToken cancellationToken = default);
    Task<PairingRequestStatus> AllowAsync(
        ManagedPairingCore core,
        string requestId,
        CancellationToken cancellationToken = default);
    Task<PairingRequestStatus> RejectAsync(
        ManagedPairingCore core,
        string requestId,
        CancellationToken cancellationToken = default);
}

public sealed class AttendedPairingRepository(
    IManagedPairingCoreResolver resolver,
    HttpClient client) : IAttendedPairingRepository
{
    private static readonly Regex SafetyCodePattern = new(
        "^[A-Z2-7]{4}-[A-Z2-7]{4}$",
        RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<AttendedPairingProjection> GetPendingAsync(
        CancellationToken cancellationToken = default)
    {
        var core = await resolver.ResolveAsync(cancellationToken);
        using var response = await client.GetAsync(
            core.Endpoint.BuildUri("/ligase/v1/pairing/requests"),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new AttendedPairingUnavailableException(
                "当前核心不支持一键配对。请升级或重新启动 Ligase Host。");
        if (!response.IsSuccessStatusCode)
            throw new AttendedPairingUnavailableException(
                "待批准设备暂时无法读取。请确认串流核心仍在运行。");
        PendingPairingSnapshot snapshot;
        try
        {
            snapshot = await response.Content.ReadFromJsonAsync<
                PendingPairingSnapshot>(JsonOptions, cancellationToken)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new AttendedPairingUnavailableException(
                "核心返回的待批准设备数据无效。请重新启动 Ligase Host。");
        }
        if (snapshot.Version != 1 || snapshot.Requests is null)
            throw new AttendedPairingUnavailableException(
                "核心配对协议版本不受支持。请更新 Ligase Host。");
        if (snapshot.Requests.Any(request =>
                request.Device is null ||
                request.SourceAddress is null ||
                !IsValidDeviceName(request.Device.Name) ||
                request.Device.Platform != "android" ||
                request.State is not ("pending" or "approved") ||
                (request.State == "approved" && !request.ReadyForApproval) ||
                !Guid.TryParseExact(request.RequestId, "D", out _) ||
                request.RequestId != request.RequestId.ToLowerInvariant() ||
                !SafetyCodePattern.IsMatch(request.SafetyCode) ||
                request.CreatedAt > request.ExpiresAt ||
                !IsCanonicalSource(request.SourceAddress)) ||
            !IsSorted(snapshot.Requests))
        {
            throw new AttendedPairingUnavailableException(
                "核心返回的待批准设备数据无效。请重新启动 Ligase Host。");
        }
        return new AttendedPairingProjection(
            core,
            snapshot.Requests,
            DateTimeOffset.UtcNow);
    }

    private static bool IsValidDeviceName(string? value)
    {
        if (string.IsNullOrEmpty(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            return false;
        try
        {
            _ = new UTF8Encoding(false, true).GetByteCount(value);
            var count = value.EnumerateRunes().Count();
            return count is >= 1 and <= 80;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsCanonicalSource(PairingSourceAddress source)
    {
        if (!IPAddress.TryParse(source.Address, out var address) ||
            IPAddress.Any.Equals(address) ||
            IPAddress.IPv6Any.Equals(address) ||
            address.IsIPv6Multicast ||
            address.IsIPv4MappedToIPv6 ||
            !string.Equals(
                address.ToString(),
                source.Address,
                StringComparison.Ordinal))
            return false;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return source.ScopeId is null;
        return address.IsIPv6LinkLocal
            ? source.ScopeId is > 0
            : source.ScopeId is null;
    }

    private static bool IsSorted(IReadOnlyList<PendingPairingRequest> requests)
    {
        for (var index = 1; index < requests.Count; index++)
        {
            var previous = requests[index - 1];
            var current = requests[index];
            var timeOrder = previous.CreatedAt.CompareTo(current.CreatedAt);
            if (timeOrder > 0 ||
                (timeOrder == 0 &&
                 string.CompareOrdinal(
                     previous.RequestId,
                     current.RequestId) > 0))
                return false;
        }
        return true;
    }

    public Task<PairingRequestStatus> AllowAsync(
        ManagedPairingCore core,
        string requestId,
        CancellationToken cancellationToken = default) =>
        ActAsync(core, requestId, "allow", cancellationToken);

    public Task<PairingRequestStatus> RejectAsync(
        ManagedPairingCore core,
        string requestId,
        CancellationToken cancellationToken = default) =>
        ActAsync(core, requestId, "reject", cancellationToken);

    private async Task<PairingRequestStatus> ActAsync(
        ManagedPairingCore core,
        string requestId,
        string action,
        CancellationToken cancellationToken)
    {
        var current = await resolver.ResolveAsync(cancellationToken);
        if (!string.Equals(
                current.InstanceKey,
                core.InstanceKey,
                StringComparison.Ordinal))
            throw new AttendedPairingUnavailableException(
                "核心实例已经变化，旧配对请求已失效。请等待客户端重新发起。");

        using var content = new ByteArrayContent([]);
        using var response = await client.PostAsync(
            current.Endpoint.BuildUri(
                $"/ligase/v1/pairing/requests/{requestId}/{action}"),
            content,
            cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            try
            {
                var status = await response.Content.ReadFromJsonAsync<
                    PairingRequestStatus>(JsonOptions, cancellationToken);
                if (status is not null &&
                    status.RequestId == requestId &&
                    status.State is "approved" or "paired" or "rejected")
                    return status;
            }
            catch (JsonException)
            {
                // Fall through to the stable product-facing error.
            }
            throw new AttendedPairingUnavailableException(
                "核心返回的配对结果无效。请重新启动 Ligase Host 后重试。");
        }
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
            throw new AttendedPairingUnavailableException(
                "该配对请求已经失效或状态已变化，请等待客户端重新发起。");
        throw new AttendedPairingUnavailableException(
            "无法更新配对请求。请确认核心仍在运行后重试。");
    }
}
