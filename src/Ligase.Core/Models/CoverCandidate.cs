namespace Ligase.Host.Core.Models;

public sealed record CoverCandidate(
    string Name,
    string Key,
    string PreviewUrl,
    string DownloadUrl);
