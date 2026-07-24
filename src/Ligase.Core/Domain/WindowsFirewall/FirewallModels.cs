namespace Ligase.Host.Core.Domain.WindowsFirewall;

public enum FirewallTransport
{
    Tcp,
    Udp
}

public sealed record FirewallPortDefinition(
    string Id,
    FirewallTransport Transport,
    IReadOnlyList<int> Offsets);

public sealed record FirewallManifest(
    int SchemaVersion,
    string RuleGroup,
    string Profile,
    string RemoteAddress,
    IReadOnlyList<string> OwnedRuleNames,
    IReadOnlyList<FirewallPortDefinition> Rules);

public sealed record FirewallRulePlan(
    string Name,
    string DisplayName,
    FirewallTransport Transport,
    IReadOnlyList<int> LocalPorts);

public sealed record FirewallPlan(
    int SchemaVersion,
    string RuleGroup,
    string Program,
    ushort BasePort,
    string Profile,
    string RemoteAddress,
    IReadOnlyList<string> OwnedRuleNames,
    IReadOnlyList<FirewallRulePlan> Rules);

public static class FirewallOutcomeCodes
{
    public const string Configured = "configured";
    public const string Removed = "removed";
    public const string NotConfigured = "notConfigured";
    public const string Drifted = "drifted";
    public const string RequiresElevation = "requiresElevation";
    public const string CommandFailed = "commandFailed";
    public const string InvalidManifest = "invalidManifest";
    public const string InvalidProgram = "invalidProgram";
    public const string InvalidBasePort = "invalidBasePort";
}

public sealed record FirewallOperationResult(
    string Code,
    bool IsConfigured,
    bool RequiresElevation,
    string? Detail = null);
