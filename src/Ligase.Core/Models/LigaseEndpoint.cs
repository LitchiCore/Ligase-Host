using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ligase.Host.Core.Models;

public enum LigaseEndpointScheme
{
    Http,
    Https,
    Rtsp
}

public enum LigaseEndpointSource
{
    Manual,
    Mdns,
    Local,
    Remote,
    Loopback
}

public sealed record LigaseEndpoint
{
    private static readonly char[] InvalidZoneCharacters = ['%', '[', ']', '/', '\\', '?', '#', ':'];

    private LigaseEndpoint(
        LigaseEndpointScheme scheme,
        string host,
        ushort port,
        string? zone,
        LigaseEndpointSource? source,
        AddressFamily? addressFamily)
    {
        Scheme = scheme;
        Host = host;
        Port = port;
        Zone = zone;
        Source = source;
        AddressFamily = addressFamily;
    }

    public LigaseEndpointScheme Scheme { get; }
    public string Host { get; }
    public ushort Port { get; }
    public string? Zone { get; }
    public LigaseEndpointSource? Source { get; }
    public AddressFamily? AddressFamily { get; }

    public static LigaseEndpoint Create(
        LigaseEndpointScheme scheme,
        string host,
        int port,
        string? zone = null,
        LigaseEndpointSource? source = null)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "端口必须在 1 到 65535 之间。");
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("电脑地址不能为空。", nameof(host));

        var normalizedZone = NormalizeZone(zone);
        var normalizedHost = NormalizeHost(host, out var family, out var isLinkLocal);
        if (isLinkLocal && normalizedZone is null)
            throw new ArgumentException("IPv6 本地链路地址必须指定网络接口。", nameof(zone));
        if (!isLinkLocal && normalizedZone is not null)
            throw new ArgumentException("只有 IPv6 本地链路地址可以指定网络接口。", nameof(zone));

        return new LigaseEndpoint(
            scheme,
            normalizedHost,
            checked((ushort)port),
            normalizedZone,
            source,
            family);
    }

    public Uri BuildUri(string pathAndQuery)
    {
        if (Zone is not null)
            throw new NotSupportedException(
                "带网络接口的 IPv6 请求必须使用可保留 socket scope 的传输适配器。");
        return new Uri(FormatUri(pathAndQuery), UriKind.Absolute);
    }

    public string FormatUri(string pathAndQuery)
    {
        if (string.IsNullOrEmpty(pathAndQuery) || pathAndQuery[0] != '/')
            throw new ArgumentException("请求路径必须以 / 开头。", nameof(pathAndQuery));
        if (Uri.TryCreate(pathAndQuery, UriKind.Absolute, out _))
            throw new ArgumentException("请求路径不能包含另一个绝对地址。", nameof(pathAndQuery));

        var authorityHost = AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{Host}{FormatZone()}]"
            : Host;
        return $"{Scheme.ToString().ToLowerInvariant()}://{authorityHost}:{Port}{pathAndQuery}";
    }

    public string CandidateKey =>
        $"{Scheme.ToString().ToLowerInvariant()}|{Host}|{Zone?.ToLowerInvariant()}|{Port}";

    private string FormatZone() =>
        Zone is null ? string.Empty : $"%25{Uri.EscapeDataString(Zone)}";

    private static string NormalizeHost(
        string value,
        out AddressFamily? family,
        out bool isLinkLocal)
    {
        var host = value.Trim();
        if (host.IndexOfAny(['[', ']', '%', '/', '\\', '?', '#', '@']) >= 0)
            throw new ArgumentException("电脑地址不能包含括号、接口、端口、路径或协议。", nameof(value));

        var ipv4Parts = host.Split('.');
        if (ipv4Parts.Length == 4 && ipv4Parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit)))
        {
            var bytes = new byte[4];
            for (var index = 0; index < ipv4Parts.Length; index++)
            {
                if (!byte.TryParse(ipv4Parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out bytes[index]))
                    throw new ArgumentException("IPv4 地址无效。", nameof(value));
            }
            family = System.Net.Sockets.AddressFamily.InterNetwork;
            isLinkLocal = false;
            return string.Join('.', bytes);
        }
        if (host.All(character => char.IsAsciiDigit(character) || character == '.'))
            throw new ArgumentException("IPv4 地址必须包含四个十进制字段。", nameof(value));

        if (IPAddress.TryParse(host, out var address))
        {
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                family = address.AddressFamily;
                isLinkLocal = false;
                return string.Join('.', address.GetAddressBytes());
            }

            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6Multicast)
                    throw new ArgumentException("不支持 IPv6 组播地址。", nameof(value));
                family = address.AddressFamily;
                isLinkLocal = address.IsIPv6LinkLocal;
                return address.ToString().ToLowerInvariant();
            }
        }

        if (host.Contains(':'))
            throw new ArgumentException("IPv6 地址无效，或地址中混入了端口。", nameof(value));

        var ascii = new IdnMapping().GetAscii(host).ToLowerInvariant();
        if (ascii.Length > 253 || ascii.StartsWith('.') || ascii.EndsWith('.'))
            throw new ArgumentException("电脑名称无效。", nameof(value));
        foreach (var label in ascii.Split('.'))
        {
            if (label.Length is < 1 or > 63
                || label.StartsWith('-')
                || label.EndsWith('-')
                || label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
                throw new ArgumentException("电脑名称无效。", nameof(value));
        }

        family = null;
        isLinkLocal = false;
        return ascii;
    }

    private static string? NormalizeZone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var zone = value.Trim();
        if (zone.EnumerateRunes().Count() > 128
            || zone.Any(char.IsControl)
            || zone.IndexOfAny(InvalidZoneCharacters) >= 0)
            throw new ArgumentException("网络接口名称无效。", nameof(value));
        if (zone.All(char.IsAsciiDigit)
            && (!uint.TryParse(zone, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                || index == 0))
            throw new ArgumentException("网络接口编号必须在 1 到 4294967295 之间。", nameof(value));
        return zone;
    }
}
