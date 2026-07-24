using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public sealed class ApolloDeviceService(
    ApolloCoreLocator coreLocator,
    IManagedPairingCoreResolver managedResolver)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _client = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    public async Task<IReadOnlyList<ApolloDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        var core = await coreLocator.ResolveAsync(cancellationToken);

        using var response = await _client.GetAsync(
            core.Endpoint.BuildUri("/ligase/v1/devices"),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ApolloDeviceInterfaceUnavailableException(
                "当前 Ligase 核心不支持设备列表。请升级 Host 后重试。");
        if (!response.IsSuccessStatusCode)
            throw new ApolloDeviceReadException(
                "设备列表暂时无法读取。请确认核心仍在运行，然后重试。");

        return DeserializeSnapshot(
            await response.Content.ReadAsStringAsync(cancellationToken)).Devices;
    }

    internal static ApolloDeviceSnapshot DeserializeSnapshot(string json) =>
        JsonSerializer.Deserialize<ApolloDeviceSnapshot>(json, JsonOptions)
        ?? new ApolloDeviceSnapshot();

    public async Task SetAccessModeAsync(
        string uuid,
        string mode,
        CancellationToken cancellationToken = default)
    {
        var canonical = CanonicalUuid(uuid);
        if (mode is not ("operate" or "observe"))
            throw new ArgumentOutOfRangeException(nameof(mode));
        var core = await managedResolver.ResolveAsync(cancellationToken);
        using var response = await _client.PutAsJsonAsync(
            core.Endpoint.BuildUri(
                $"/ligase/v1/devices/{canonical}/access"),
            new { mode },
            cancellationToken);
        if (response.IsSuccessStatusCode) return;
        throw await MutationErrorAsync(response, "修改设备权限失败。");
    }

    public async Task DeleteAsync(
        string uuid,
        bool endActiveSession,
        CancellationToken cancellationToken = default)
    {
        var canonical = CanonicalUuid(uuid);
        var core = await managedResolver.ResolveAsync(cancellationToken);
        var path = endActiveSession
            ? $"/ligase/v1/devices/{canonical}/end-session-and-delete"
            : $"/ligase/v1/devices/{canonical}";
        using var request = new HttpRequestMessage(
            endActiveSession ? HttpMethod.Post : HttpMethod.Delete,
            core.Endpoint.BuildUri(path))
        {
            Content = endActiveSession ? new ByteArrayContent([]) : null
        };
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent) return;
        throw await MutationErrorAsync(response, "删除设备失败。");
    }

    private static string CanonicalUuid(string value) =>
        Guid.TryParseExact(value, "D", out var parsed)
            ? parsed.ToString("D").ToLowerInvariant()
            : throw new ArgumentException("设备 UUID 无效。", nameof(value));

    private static async Task<Exception> MutationErrorAsync(
        HttpResponseMessage response,
        string fallback)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
            return new ApolloDeviceActiveException(
                "该设备仍有活动串流。请先结束串流，或选择“结束串流并删除”。");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new ApolloDeviceReadException(
                "设备已经不存在或核心实例已变化，请刷新后重试。");
        _ = await response.Content.ReadAsStringAsync();
        return new ApolloDeviceReadException(
            $"{fallback} 请确认 Ligase 核心仍在运行。");
    }
}

public sealed class ApolloDeviceInterfaceUnavailableException(string message)
    : InvalidOperationException(message);
public sealed class ApolloDeviceReadException(string message)
    : InvalidOperationException(message);
public sealed class ApolloDeviceActiveException(string message)
    : InvalidOperationException(message);
