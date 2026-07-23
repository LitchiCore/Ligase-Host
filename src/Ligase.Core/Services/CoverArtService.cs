using System.Net;
using System.Text.Json;
using Ligase.Host.Core.Models;

namespace Ligase.Host.Core.Services;

public sealed class CoverArtService(HttpClient httpClient, LigasePaths paths)
{
    private const string DatabaseRoot =
        "https://raw.githubusercontent.com/LizardByte/GameDB/gh-pages";
    private const string ImageHost = "images.igdb.com";
    private const int MaximumResults = 12;
    private const int MaximumImageBytes = 12 * 1024 * 1024;

    public async Task<IReadOnlyList<CoverCandidate>> SearchAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var query = name.Trim();
        if (query.Length == 0) return [];

        var bucket = new string(query
            .Take(2)
            .Where(character => char.IsAsciiLetterOrDigit(character))
            .Select(char.ToLowerInvariant)
            .ToArray());
        if (bucket.Length == 0) bucket = "@";

        using var bucketResponse = await httpClient.GetAsync(
            $"{DatabaseRoot}/buckets/{Uri.EscapeDataString(bucket)}.json",
            cancellationToken);
        if (bucketResponse.StatusCode == HttpStatusCode.NotFound) return [];
        bucketResponse.EnsureSuccessStatusCode();

        await using var bucketStream = await bucketResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var bucketDocument = await JsonDocument.ParseAsync(bucketStream, cancellationToken: cancellationToken);
        var matches = bucketDocument.RootElement
            .EnumerateObject()
            .Select(entry => new
            {
                Id = entry.Name,
                Name = entry.Value.TryGetProperty("name", out var value)
                    ? value.GetString()
                    : null
            })
            .Where(entry => entry.Name?.StartsWith(query, StringComparison.OrdinalIgnoreCase) == true)
            .Take(MaximumResults)
            .ToArray();

        var candidates = await Task.WhenAll(matches.Select(
            match => LoadCandidateAsync(match.Id, cancellationToken)));
        return candidates.Where(candidate => candidate is not null).Cast<CoverCandidate>().ToArray();
    }

    public async Task<string> DownloadAsync(
        CoverCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(candidate.DownloadUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, ImageHost, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("封面来源不受信任。");

        using var response = await httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumImageBytes)
            throw new InvalidOperationException("封面文件过大。");

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > MaximumImageBytes ||
            bytes.Length < 8 ||
            !bytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            throw new InvalidOperationException("下载结果不是有效的 PNG 封面。");

        Directory.CreateDirectory(paths.CoversDirectory);
        var safeKey = string.Concat(candidate.Key.Where(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
        if (safeKey.Length == 0) safeKey = Guid.NewGuid().ToString("N");
        var destination = Path.Combine(paths.CoversDirectory, safeKey + ".png");
        var temporary = destination + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
        File.Move(temporary, destination, true);
        return destination;
    }

    private async Task<CoverCandidate?> LoadCandidateAsync(
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{DatabaseRoot}/games/{Uri.EscapeDataString(id)}.json",
                cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("name", out var nameProperty) ||
                !root.TryGetProperty("cover", out var cover) ||
                !cover.TryGetProperty("url", out var urlProperty))
                return null;

            var rawUrl = urlProperty.GetString();
            var name = nameProperty.GetString();
            if (string.IsNullOrWhiteSpace(rawUrl) || string.IsNullOrWhiteSpace(name)) return null;
            var slash = rawUrl.LastIndexOf('/');
            var dot = rawUrl.LastIndexOf('.');
            if (slash < 0 || dot <= slash) return null;
            var slug = rawUrl[(slash + 1)..dot];
            return new CoverCandidate(
                name,
                $"igdb_{id}",
                $"https://{ImageHost}/igdb/image/upload/t_cover_big/{slug}.jpg",
                $"https://{ImageHost}/igdb/image/upload/t_cover_big_2x/{slug}.png");
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
