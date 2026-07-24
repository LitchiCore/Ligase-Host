using System.Diagnostics;
using System.Text.Json;
using Ligase.Host.Core.Domain.WindowsFirewall;

namespace Ligase.Host.Core.Infrastructure.Windows;

public sealed record FirewallEnvironmentData(
    IReadOnlyList<string> ActiveNetworkCategories,
    IReadOnlyList<FirewallRuleObservation> Rules);

public sealed record FirewallRuleObservation(
    string Name,
    string DisplayName,
    string Group,
    string Program,
    string Profile,
    IReadOnlyList<string> RemoteAddresses);

public interface IFirewallEnvironmentDataSource
{
    Task<FirewallEnvironmentData> ReadAsync(CancellationToken cancellationToken);
}

public sealed class WindowsFirewallEnvironmentReader(
    IFirewallEnvironmentDataSource? dataSource = null)
{
    private const string OwnedGroup = "Ligase Host LAN Access";
    private static readonly HashSet<string> OwnedNames =
    [
        "Ligase.Host.Lan.Tcp.v0",
        "Ligase.Host.Lan.Udp.v0",
        "Ligase.Host.Lan.Tcp.v1",
        "Ligase.Host.Lan.Udp.v1"
    ];

    private readonly IFirewallEnvironmentDataSource _dataSource =
        dataSource ?? new PowerShellFirewallEnvironmentDataSource();

    public async Task<FirewallEnvironmentSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var data = await _dataSource.ReadAsync(cancellationToken);
        return new(
            AggregateNetworkCategory(data.ActiveNetworkCategories),
            data.Rules.Any(IsLegacyBroadSunshineRule));
    }

    internal static FirewallNetworkCategory AggregateNetworkCategory(
        IReadOnlyList<string> categories)
    {
        if (categories.Count == 0)
            return FirewallNetworkCategory.NoActiveNetwork;

        var parsed = categories.Select(ParseCategory).Distinct().ToArray();
        return parsed.Length == 1 ? parsed[0] : FirewallNetworkCategory.Unknown;
    }

    internal static bool IsLegacyBroadSunshineRule(FirewallRuleObservation rule)
    {
        if (OwnedNames.Contains(rule.Name) ||
            string.Equals(rule.Group, OwnedGroup, StringComparison.Ordinal))
        {
            return false;
        }

        var identifiesSunshine =
            ContainsProductName(rule.Name) ||
            ContainsProductName(rule.DisplayName) ||
            ContainsProductName(rule.Group) ||
            string.Equals(
                Path.GetFileName(rule.Program),
                "sunshine.exe",
                StringComparison.OrdinalIgnoreCase);
        if (!identifiesSunshine)
            return false;

        var privateOnly = string.Equals(
            rule.Profile,
            "Private",
            StringComparison.OrdinalIgnoreCase);
        var localSubnetOnly =
            rule.RemoteAddresses.Count == 1 &&
            string.Equals(
                rule.RemoteAddresses[0],
                "LocalSubnet",
                StringComparison.OrdinalIgnoreCase);
        return !privateOnly || !localSubnetOnly;
    }

    private static FirewallNetworkCategory ParseCategory(string value) =>
        value.Trim() switch
        {
            "Private" => FirewallNetworkCategory.Private,
            "Public" => FirewallNetworkCategory.Public,
            "DomainAuthenticated" or "Domain" => FirewallNetworkCategory.Domain,
            _ => FirewallNetworkCategory.Unknown
        };

    private static bool ContainsProductName(string value) =>
        value.Contains("sunshine", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("apollo", StringComparison.OrdinalIgnoreCase);
}

internal sealed class PowerShellFirewallEnvironmentDataSource
    : IFirewallEnvironmentDataSource
{
    private const string ReadScript = """
        $profiles = @(Get-NetConnectionProfile -ErrorAction Stop |
          Where-Object { $_.IPv4Connectivity -ne 'Disconnected' -or $_.IPv6Connectivity -ne 'Disconnected' } |
          ForEach-Object { [string]$_.NetworkCategory })
        $rules = @(Get-NetFirewallRule -PolicyStore ActiveStore -ErrorAction Stop |
          Where-Object { $_.Enabled -eq 'True' } |
          ForEach-Object {
            $app = $_ | Get-NetFirewallApplicationFilter
            $address = $_ | Get-NetFirewallAddressFilter
            [pscustomobject]@{
              name = [string]$_.Name
              displayName = [string]$_.DisplayName
              group = [string]$_.Group
              program = [string]$app.Program
              profile = [string]$_.Profile
              remoteAddresses = @($address.RemoteAddress | ForEach-Object { [string]$_ })
            }
          })
        [pscustomobject]@{ activeNetworkCategories = $profiles; rules = $rules } |
          ConvertTo-Json -Depth 5 -Compress
        """;

    public async Task<FirewallEnvironmentData> ReadAsync(
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(ReadScript);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("firewallReadbackStartFailed");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            await stderr;
            throw new InvalidOperationException("firewallReadbackFailed");
        }

        using var document = JsonDocument.Parse(await stdout);
        var root = document.RootElement;
        var categories = root.GetProperty("activeNetworkCategories")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
        var rules = root.GetProperty("rules").EnumerateArray().Select(item =>
            new FirewallRuleObservation(
                item.GetProperty("name").GetString() ?? string.Empty,
                item.GetProperty("displayName").GetString() ?? string.Empty,
                item.GetProperty("group").GetString() ?? string.Empty,
                item.GetProperty("program").GetString() ?? string.Empty,
                item.GetProperty("profile").GetString() ?? string.Empty,
                item.GetProperty("remoteAddresses").EnumerateArray()
                    .Select(address => address.GetString() ?? string.Empty)
                    .ToArray())).ToArray();
        return new(categories, rules);
    }
}
