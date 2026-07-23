using System.Net.NetworkInformation;
using System.Xml.Linq;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public sealed record ApolloCoreEndpoint(
    LigaseEndpoint Endpoint,
    string UniqueId,
    string HostName)
{
    public ushort BasePort => Endpoint.Port;
}

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
            return await ProbeLoopbackAsync(managedCore.BasePort, cancellationToken)
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
            var endpoint = await ProbeLoopbackAsync((ushort)candidate, cancellationToken);
            if (endpoint is not null) matches.Add(endpoint);
        }

        var distinctMatches = matches
            .GroupBy(match => $"{match.UniqueId.ToLowerInvariant()}|{match.BasePort}")
            .Select(group => group.First())
            .ToArray();
        return distinctMatches.Length switch
        {
            1 => distinctMatches[0],
            0 => throw new ApolloCoreNotRunningException(
                "Ligase 串流核心没有运行。请在概览页启动核心，然后刷新设备。"),
            _ => throw new ApolloCoreAmbiguousException(
                "检测到多个 Ligase 串流核心。请停止不需要的实例，再刷新设备。")
        };
    }

    public async Task<IReadOnlyList<ApolloCoreEndpoint>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
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
            var endpoint = await ProbeLoopbackAsync((ushort)candidate, cancellationToken);
            if (endpoint is not null) matches.Add(endpoint);
        }

        return matches
            .GroupBy(match => $"{match.UniqueId.ToLowerInvariant()}|{match.BasePort}")
            .Select(group => group.First())
            .ToArray();
    }

    internal static bool IsLigaseBasePortCandidate(int port) =>
        port >= 48989 && port <= 65464 && (port - 48989) % 50 == 0;

    internal static ApolloCoreEndpoint? ParseServerInfo(LigaseEndpoint endpoint, string xml)
    {
        var root = XDocument.Parse(xml).Root;
        if (root?.Element("LigaseSyncVersion")?.Value != "1") return null;
        var uniqueId = root.Element("uniqueid")?.Value;
        if (string.IsNullOrWhiteSpace(uniqueId)) return null;
        return new ApolloCoreEndpoint(
            endpoint,
            uniqueId,
            root.Element("hostname")?.Value ?? "Ligase Host");
    }

    private async Task<ApolloCoreEndpoint?> ProbeLoopbackAsync(
        ushort basePort,
        CancellationToken cancellationToken)
    {
        var candidates = new[]
        {
            LigaseEndpoint.Create(
                LigaseEndpointScheme.Http, "::1", basePort, source: LigaseEndpointSource.Loopback),
            LigaseEndpoint.Create(
                LigaseEndpointScheme.Http, "127.0.0.1", basePort, source: LigaseEndpointSource.Loopback)
        };
        foreach (var candidate in candidates)
        {
            var match = await ProbeAsync(candidate, cancellationToken);
            if (match is not null) return match;
        }

        return null;
    }

    private async Task<ApolloCoreEndpoint?> ProbeAsync(
        LigaseEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.GetAsync(
                endpoint.BuildUri("/serverinfo?uniqueid=ligase-desktop"),
                cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return ParseServerInfo(
                endpoint,
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
