using System.Net.NetworkInformation;
using System.Xml.Linq;

namespace Ligase.Host.Core.Services;

public sealed record ApolloCoreEndpoint(ushort BasePort, string UniqueId, string HostName);

public sealed class ApolloCoreLocator(ApolloInstanceManager managedCore)
{
    private readonly HttpClient _client = new()
    {
        Timeout = TimeSpan.FromMilliseconds(750)
    };

    public async Task<ApolloCoreEndpoint> ResolveAsync(
        CancellationToken cancellationToken = default)
    {
        if (managedCore.IsRunning && managedCore.BasePort != 0)
        {
            return await ProbeAsync(managedCore.BasePort, cancellationToken)
                   ?? throw new ApolloCoreUnavailableException(
                       "Ligase 串流核心正在启动，但设备接口还没有准备好。请稍后刷新。");
        }

        var candidates = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Select(endpoint => endpoint.Port)
            .Where(IsLigaseBasePortCandidate)
            .Distinct()
            .Order()
            .ToArray();
        var matches = new List<ApolloCoreEndpoint>();
        foreach (var candidate in candidates)
        {
            var endpoint = await ProbeAsync((ushort)candidate, cancellationToken);
            if (endpoint is not null) matches.Add(endpoint);
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ApolloCoreNotRunningException(
                "Ligase 串流核心没有运行。请在概览页启动核心，然后刷新设备。"),
            _ => throw new ApolloCoreAmbiguousException(
                "检测到多个 Ligase 串流核心。请停止不需要的实例，再刷新设备。")
        };
    }

    internal static bool IsLigaseBasePortCandidate(int port) =>
        port >= 48989 && port <= 65464 && (port - 48989) % 50 == 0;

    internal static ApolloCoreEndpoint? ParseServerInfo(ushort basePort, string xml)
    {
        var root = XDocument.Parse(xml).Root;
        if (root?.Element("LigaseSyncVersion")?.Value != "1") return null;
        var uniqueId = root.Element("uniqueid")?.Value;
        if (string.IsNullOrWhiteSpace(uniqueId)) return null;
        return new ApolloCoreEndpoint(
            basePort,
            uniqueId,
            root.Element("hostname")?.Value ?? "Ligase Host");
    }

    private async Task<ApolloCoreEndpoint?> ProbeAsync(
        ushort basePort,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.GetAsync(
                $"http://127.0.0.1:{basePort}/serverinfo?uniqueid=ligase-desktop",
                cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return ParseServerInfo(
                basePort,
                await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            return null;
        }
    }
}

public class ApolloCoreUnavailableException(string message) : InvalidOperationException(message);
public sealed class ApolloCoreNotRunningException(string message) : ApolloCoreUnavailableException(message);
public sealed class ApolloCoreAmbiguousException(string message) : ApolloCoreUnavailableException(message);
