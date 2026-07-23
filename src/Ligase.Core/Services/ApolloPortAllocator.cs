using System.Net.NetworkInformation;

namespace Ligase.Host.Core.Services;

public sealed class ApolloPortAllocator
{
    private static readonly int[] PortOffsets = [-5, 0, 1, 9, 10, 11, 21];

    public ushort FindAvailableBasePort(ushort preferredBasePort = 48989)
    {
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        var usedPorts = properties.GetActiveTcpListeners().Select(endpoint => endpoint.Port)
            .Concat(properties.GetActiveUdpListeners().Select(endpoint => endpoint.Port))
            .ToHashSet();

        for (var candidate = (int)preferredBasePort; candidate <= 65464; candidate += 50)
        {
            if (PortOffsets.All(offset => !usedPorts.Contains(candidate + offset)))
            {
                return checked((ushort)candidate);
            }
        }

        throw new InvalidOperationException("没有找到可供 Ligase Apollo 使用的完整端口组。");
    }

    public static IReadOnlyList<int> ExpandPortFamily(ushort basePort) =>
        PortOffsets.Select(offset => basePort + offset).ToArray();
}
