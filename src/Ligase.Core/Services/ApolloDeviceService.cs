using System.Net;
using System.Text.Json;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public sealed class ApolloDeviceService(ApolloCoreLocator coreLocator)
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
}

public sealed class ApolloDeviceInterfaceUnavailableException(string message)
    : InvalidOperationException(message);
public sealed class ApolloDeviceReadException(string message)
    : InvalidOperationException(message);
