using System.Net;
using System.Text.Json;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public sealed class ApolloDeviceService(ApolloInstanceManager core)
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
        if (!core.IsRunning || core.BasePort == 0)
            throw new InvalidOperationException("Apollo 核心尚未运行。");

        using var response = await _client.GetAsync(
            $"http://127.0.0.1:{core.BasePort}/ligase/v1/devices",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("当前 Apollo 核心尚未包含 Ligase 设备接口。");
        response.EnsureSuccessStatusCode();

        return DeserializeSnapshot(
            await response.Content.ReadAsStringAsync(cancellationToken)).Devices;
    }

    internal static ApolloDeviceSnapshot DeserializeSnapshot(string json) =>
        JsonSerializer.Deserialize<ApolloDeviceSnapshot>(json, JsonOptions)
        ?? new ApolloDeviceSnapshot();
}
