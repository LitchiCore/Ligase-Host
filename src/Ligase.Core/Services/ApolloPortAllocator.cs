using System.Net;
using System.Net.Sockets;

namespace Ligase.Host.Core.Services;

public sealed class ApolloPortAllocator
{
    private static readonly int[] PortOffsets = [-5, 0, 1, 9, 10, 11, 21];

    public ushort FindAvailableBasePort(ushort preferredBasePort = 48989)
    {
        for (var candidate = (int)preferredBasePort; candidate <= 65464; candidate += 50)
        {
            if (PortOffsets.All(offset => CanBind(candidate + offset)))
            {
                return checked((ushort)candidate);
            }
        }

        throw new InvalidOperationException("没有找到可供 Ligase Apollo 使用的完整端口组。");
    }

    public static IReadOnlyList<int> ExpandPortFamily(ushort basePort) =>
        PortOffsets.Select(offset => basePort + offset).ToArray();

    private static bool CanBind(int port)
    {
        try
        {
            var family = Socket.OSSupportsIPv6
                ? AddressFamily.InterNetworkV6
                : AddressFamily.InterNetwork;
            var anyAddress = Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any;
            using var tcp = new Socket(family, SocketType.Stream, ProtocolType.Tcp)
            {
                ExclusiveAddressUse = true
            };
            using var udp = new Socket(family, SocketType.Dgram, ProtocolType.Udp)
            {
                ExclusiveAddressUse = true
            };
            if (Socket.OSSupportsIPv6)
            {
                tcp.DualMode = true;
                udp.DualMode = true;
            }
            tcp.Bind(new IPEndPoint(anyAddress, port));
            udp.Bind(new IPEndPoint(anyAddress, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
