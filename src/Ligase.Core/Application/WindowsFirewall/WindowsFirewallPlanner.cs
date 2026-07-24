using System.Text.Json;
using System.Text.Json.Serialization;
using Ligase.Host.Core.Domain.WindowsFirewall;

namespace Ligase.Host.Core.Application.WindowsFirewall;

public sealed class WindowsFirewallPlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    static WindowsFirewallPlanner()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public FirewallManifest LoadManifest(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        try
        {
            var manifest = JsonSerializer.Deserialize<FirewallManifest>(
                File.ReadAllBytes(Path.GetFullPath(file)),
                JsonOptions);
            ValidateManifest(manifest);
            return manifest!;
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new FirewallPlanException(
                FirewallOutcomeCodes.InvalidManifest,
                exception);
        }
    }

    public FirewallPlan Build(
        FirewallManifest manifest,
        string managedSunshineExecutable,
        ushort basePort)
    {
        ValidateManifest(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedSunshineExecutable);
        var program = Path.GetFullPath(managedSunshineExecutable);
        if (!Path.IsPathFullyQualified(program) ||
            !string.Equals(
                Path.GetFileName(program),
                "sunshine.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new FirewallPlanException(FirewallOutcomeCodes.InvalidProgram);
        }

        var rules = manifest.Rules.Select(rule =>
        {
            var ports = rule.Offsets.Select(offset =>
            {
                var port = basePort + offset;
                if (port is < 1 or > 65535)
                    throw new FirewallPlanException(FirewallOutcomeCodes.InvalidBasePort);
                return port;
            }).ToArray();
            return new FirewallRulePlan(
                $"Ligase.Host.Lan.{rule.Transport}.v1",
                $"Ligase Host LAN {rule.Transport.ToString().ToUpperInvariant()}",
                rule.Transport,
                ports);
        }).ToArray();

        return new FirewallPlan(
            manifest.SchemaVersion,
            manifest.RuleGroup,
            program,
            basePort,
            manifest.Profile,
            manifest.RemoteAddress,
            manifest.OwnedRuleNames,
            rules);
    }

    private static void ValidateManifest(FirewallManifest? manifest)
    {
        if (manifest is null ||
            manifest.SchemaVersion != 1 ||
            !string.Equals(manifest.RuleGroup, "Ligase Host LAN Access", StringComparison.Ordinal) ||
            !string.Equals(manifest.Profile, "Private", StringComparison.Ordinal) ||
            !string.Equals(manifest.RemoteAddress, "LocalSubnet", StringComparison.Ordinal) ||
            manifest.Rules.Count != 2 ||
            manifest.OwnedRuleNames.Count == 0 ||
            manifest.OwnedRuleNames.Distinct(StringComparer.Ordinal).Count() !=
            manifest.OwnedRuleNames.Count)
        {
            throw new FirewallPlanException(FirewallOutcomeCodes.InvalidManifest);
        }

        var tcp = manifest.Rules.SingleOrDefault(rule =>
            rule.Transport == FirewallTransport.Tcp);
        var udp = manifest.Rules.SingleOrDefault(rule =>
            rule.Transport == FirewallTransport.Udp);
        string[] expectedOwnedNames =
        [
            "Ligase.Host.Lan.Tcp.v0",
            "Ligase.Host.Lan.Udp.v0",
            "Ligase.Host.Lan.Tcp.v1",
            "Ligase.Host.Lan.Udp.v1"
        ];
        if (tcp is null ||
            udp is null ||
            !string.Equals(tcp.Id, "lanTcp", StringComparison.Ordinal) ||
            !string.Equals(udp.Id, "streamUdp", StringComparison.Ordinal) ||
            !tcp.Offsets.SequenceEqual([-5, 0, 21]) ||
            !udp.Offsets.SequenceEqual([9, 10, 11]) ||
            !manifest.OwnedRuleNames.SequenceEqual(expectedOwnedNames) ||
            manifest.Rules.Any(rule =>
                rule.Offsets.Count == 0 || rule.Offsets.Distinct().Count() != rule.Offsets.Count))
        {
            throw new FirewallPlanException(FirewallOutcomeCodes.InvalidManifest);
        }
    }
}

public sealed class FirewallPlanException : Exception
{
    public FirewallPlanException(string code, Exception? innerException = null)
        : base(code, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
