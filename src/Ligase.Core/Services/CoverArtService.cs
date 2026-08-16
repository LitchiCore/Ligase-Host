using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ligase.Host.Core.Models;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Ligase.Host.Core.Services;

public sealed class CoverArtService(
    HttpClient httpClient,
    LigasePaths paths,
    ISteamInstallationLocator steamInstallationLocator)
{
    private const string DatabaseRoot =
        "https://raw.githubusercontent.com/LizardByte/GameDB/gh-pages";
    private const string ImageHost = "images.igdb.com";
    private const string SteamSourceKind = "steamClientLibraryCache";
    private const string SteamUsageRights = "thirdPartyArtworkLocalUseOnlyNoRedistribution";
    private const int MaximumResults = 12;
    private const int MaximumImageBytes = 12 * 1024 * 1024;
    private const ulong MaximumPixels = 4096UL * 4096UL;
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private static readonly JsonSerializerOptions CacheJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public Task<IReadOnlyList<CoverCandidate>> FindSteamAsync(
        SteamGame game,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsVerifiedSteamManifest(game))
            return Task.FromResult<IReadOnlyList<CoverCandidate>>([]);
        var steamRoot = steamInstallationLocator.FindSteamPath();
        if (steamRoot is null)
            return Task.FromResult<IReadOnlyList<CoverCandidate>>([]);

        if (!TryResolveExactSteamCacheFile(steamRoot, game, out var source))
            return Task.FromResult<IReadOnlyList<CoverCandidate>>([]);

        IReadOnlyList<CoverCandidate> result =
        [
            new(
                game.Name,
                $"steam_{game.AppId}",
                new Uri(source).AbsoluteUri,
                new Uri(source).AbsoluteUri,
                SteamSourceKind,
                game.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SteamUsageRights,
                game.AppId)
        ];
        return Task.FromResult(result);
    }

    public async Task<IReadOnlyList<CoverCandidate>> SearchAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var query = name.Trim();
        if (query.Length == 0) return [];
        var bucket = new string(query.Take(2)
            .Where(char.IsAsciiLetterOrDigit)
            .Select(char.ToLowerInvariant).ToArray());
        if (bucket.Length == 0) bucket = "@";

        using var bucketResponse = await httpClient.GetAsync(
            $"{DatabaseRoot}/buckets/{Uri.EscapeDataString(bucket)}.json",
            cancellationToken);
        if (bucketResponse.StatusCode == HttpStatusCode.NotFound) return [];
        bucketResponse.EnsureSuccessStatusCode();
        await using var bucketStream = await bucketResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var bucketDocument = await JsonDocument.ParseAsync(
            bucketStream, cancellationToken: cancellationToken);
        var matches = bucketDocument.RootElement.EnumerateObject()
            .Select(entry => new
            {
                Id = entry.Name,
                Name = entry.Value.TryGetProperty("name", out var value)
                    ? value.GetString() : null
            })
            .Where(entry => entry.Name?.StartsWith(
                query, StringComparison.OrdinalIgnoreCase) == true)
            .Take(MaximumResults).ToArray();
        var candidates = await Task.WhenAll(matches.Select(
            match => LoadCandidateAsync(match.Id, cancellationToken)));
        return candidates.Where(candidate => candidate is not null)
            .Cast<CoverCandidate>().ToArray();
    }

    public async Task<string> DownloadAsync(
        CoverCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        byte[] png;
        if (string.Equals(candidate.SourceKind, SteamSourceKind, StringComparison.Ordinal))
        {
            if (candidate.SteamAppId is not uint appId ||
                !string.Equals(candidate.SourceId, appId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) ||
                !Uri.TryCreate(candidate.DownloadUrl, UriKind.Absolute, out var localUri) ||
                !localUri.IsFile)
                throw new InvalidOperationException("Steam 封面没有绑定可验证的 App ID。");
            var steamRoot = steamInstallationLocator.FindSteamPath()
                ?? throw new InvalidOperationException("找不到 Steam 客户端素材缓存。");
            if (!IsExactSafeSteamCacheFile(steamRoot, appId, localUri.LocalPath))
                throw new InvalidOperationException("Steam 封面缓存身份已变化。");
            png = await TranscodeSteamLibraryCapsuleAsync(
                ReadStable(localUri.LocalPath), cancellationToken);
        }
        else
        {
            if (!Uri.TryCreate(candidate.DownloadUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(uri.Host, ImageHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("封面来源不受信任。");
            using var response = await httpClient.GetAsync(
                uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (!string.Equals(response.Content.Headers.ContentType?.MediaType,
                    "image/png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("封面响应类型不是 PNG。");
            if (response.Content.Headers.ContentLength > MaximumImageBytes)
                throw new InvalidOperationException("封面文件过大。");
            png = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            await ValidatePngAsync(png, false, cancellationToken);
        }

        Directory.CreateDirectory(paths.CoversDirectory);
        var safeKey = string.Concat(candidate.Key.Where(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
        if (safeKey.Length == 0) safeKey = Guid.NewGuid().ToString("N");
        var contentSha = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
        var destination = Path.Combine(
            paths.CoversDirectory, $"{safeKey}_{contentSha[..16]}.png");
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        await using (var stream = new FileStream(
                         temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         64 * 1024, FileOptions.WriteThrough | FileOptions.Asynchronous))
        {
            await stream.WriteAsync(png, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(true);
        }
        File.Move(temporary, destination, true);
        await RecordAuthorityAsync(new CoverCacheAuthority(
            Path.GetRelativePath(paths.RootDirectory, destination)
                .Replace(Path.DirectorySeparatorChar, '/'),
            contentSha,
            candidate.SourceKind,
            string.IsNullOrWhiteSpace(candidate.SourceId) ? candidate.Key : candidate.SourceId,
            candidate.UsageRights,
            candidate.SteamAppId,
            DateTimeOffset.UtcNow), cancellationToken);
        return destination;
    }

    public async Task PruneUnreferencedAsync(
        IReadOnlyCollection<LibraryItem> items,
        CancellationToken cancellationToken = default)
    {
        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            var document = ReadCacheAuthority();
            var referenced = items.Select(item => item.CoverImagePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(path!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var retained = new List<CoverCacheAuthority>();
            foreach (var entry in document.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryResolveOwnedCachePath(entry.RelativePath, out var fullPath)) continue;
                if (referenced.Contains(fullPath))
                {
                    retained.Add(entry);
                    continue;
                }
                if (File.Exists(fullPath) &&
                    !File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint))
                    File.Delete(fullPath);
            }
            await WriteCacheAuthorityAsync(new(1, retained), cancellationToken);
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    public static CoverCacheAuthority? TryReadAuthority(
        LigasePaths paths,
        string? coverPath)
    {
        if (string.IsNullOrWhiteSpace(coverPath) ||
            !File.Exists(paths.CoverCacheAuthorityFile)) return null;
        try
        {
            var relative = Path.GetRelativePath(paths.RootDirectory, Path.GetFullPath(coverPath))
                .Replace(Path.DirectorySeparatorChar, '/');
            var document = JsonSerializer.Deserialize<CoverCacheDocument>(
                File.ReadAllBytes(paths.CoverCacheAuthorityFile), CacheJson);
            if (document is not { SchemaVersion: 1 }) return null;
            var entry = document.Entries.SingleOrDefault(candidate => string.Equals(
                candidate.RelativePath, relative, StringComparison.Ordinal));
            if (entry is null || !File.Exists(coverPath) ||
                File.GetAttributes(coverPath).HasFlag(FileAttributes.ReparsePoint)) return null;
            var actualSha = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(coverPath))).ToLowerInvariant();
            return string.Equals(actualSha, entry.ContentSha256, StringComparison.Ordinal)
                ? entry : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task RecordAuthorityAsync(
        CoverCacheAuthority authority,
        CancellationToken cancellationToken)
    {
        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            var entries = ReadCacheAuthority().Entries
                .Where(entry => !string.Equals(
                    entry.RelativePath, authority.RelativePath, StringComparison.Ordinal))
                .Append(authority)
                .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToArray();
            await WriteCacheAuthorityAsync(new(1, entries), cancellationToken);
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    private CoverCacheDocument ReadCacheAuthority()
    {
        if (!File.Exists(paths.CoverCacheAuthorityFile)) return new(1, []);
        try
        {
            return JsonSerializer.Deserialize<CoverCacheDocument>(
                       File.ReadAllBytes(paths.CoverCacheAuthorityFile), CacheJson)
                   is { SchemaVersion: 1 } document
                ? document : new(1, []);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(1, []);
        }
    }

    private async Task WriteCacheAuthorityAsync(
        CoverCacheDocument document,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.RootDirectory);
        var temporary = paths.CoverCacheAuthorityFile + $".{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temporary,
            JsonSerializer.SerializeToUtf8Bytes(document, CacheJson), cancellationToken);
        File.Move(temporary, paths.CoverCacheAuthorityFile, true);
    }

    private bool TryResolveOwnedCachePath(string relative, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathFullyQualified(relative) ||
            relative.Split('/').Any(segment => segment is "" or "." or "..") ||
            relative.Contains('\\')) return false;
        var normalized = Path.GetFullPath(Path.Combine(
            paths.RootDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.GetFullPath(paths.CoversDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        fullPath = normalized;
        return true;
    }

    private static bool IsVerifiedSteamManifest(SteamGame game)
    {
        try
        {
            if (!string.Equals(Path.GetFileName(game.ManifestPath),
                    $"appmanifest_{game.AppId}.acf", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(game.ManifestPath)) return false;
            var state = VdfParser.Parse(File.ReadAllText(game.ManifestPath)).GetObject("AppState");
            return state is not null && uint.TryParse(state.GetString("appid"),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var manifestAppId) && manifestAppId == game.AppId;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FormatException)
        {
            return false;
        }
    }

    private static bool TryResolveExactSteamCacheFile(
        string steamRoot, SteamGame game, out string source)
    {
        source = string.Empty;
        try
        {
            if (!IsVerifiedSteamManifest(game)) return false;
            var appDirectory = Path.GetFullPath(Path.Combine(
                steamRoot, "appcache", "librarycache",
                game.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var current = new DirectoryInfo(Path.GetFullPath(steamRoot));
            foreach (var segment in new[] { "appcache", "librarycache",
                         game.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            {
                if (!current.Exists || current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return false;
                current = new DirectoryInfo(Path.Combine(current.FullName, segment));
            }
            if (!current.Exists || current.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                !string.Equals(current.FullName, appDirectory, StringComparison.OrdinalIgnoreCase))
                return false;

            var legacy = Path.Combine(appDirectory, "library_600x900.jpg");
            if (IsSafeRegularFile(legacy))
            {
                source = legacy;
                return true;
            }

            var state = VdfParser.Parse(File.ReadAllText(game.ManifestPath)).GetObject("AppState");
            var language = state?.GetObject("UserConfig")?.GetString("language");
            if (!IsCanonicalSteamLanguage(language)) return false;
            var fileName = $"library_capsule_{language}.jpg";
            var matches = current.EnumerateDirectories()
                .Where(directory =>
                    IsLowerHexSha1(directory.Name) &&
                    !directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .Select(directory => Path.Combine(directory.FullName, fileName))
                .Where(IsSafeRegularFile)
                .Take(2)
                .ToArray();
            if (matches.Length != 1) return false;
            source = matches[0];
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
                FormatException)
        {
            return false;
        }
    }

    private static bool IsExactSafeSteamCacheFile(
        string steamRoot, uint appId, string candidate)
    {
        try
        {
            var appDirectory = Path.GetFullPath(Path.Combine(
                steamRoot, "appcache", "librarycache",
                appId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var current = new DirectoryInfo(Path.GetFullPath(steamRoot));
            foreach (var segment in new[] { "appcache", "librarycache",
                         appId.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            {
                if (!current.Exists || current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return false;
                current = new DirectoryInfo(Path.Combine(current.FullName, segment));
            }
            if (!current.Exists || current.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                !string.Equals(current.FullName, appDirectory, StringComparison.OrdinalIgnoreCase))
                return false;

            var actual = Path.GetFullPath(candidate);
            var legacy = Path.Combine(appDirectory, "library_600x900.jpg");
            if (string.Equals(actual, legacy, StringComparison.OrdinalIgnoreCase))
                return IsSafeRegularFile(actual);

            var relative = Path.GetRelativePath(appDirectory, actual);
            var segments = relative.Split(Path.DirectorySeparatorChar);
            if (segments.Length != 2 || !IsLowerHexSha1(segments[0]) ||
                !IsLocalizedCapsuleFileName(segments[1])) return false;
            var parent = new DirectoryInfo(Path.Combine(appDirectory, segments[0]));
            return parent.Exists && !parent.Attributes.HasFlag(FileAttributes.ReparsePoint) &&
                   IsSafeRegularFile(actual);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSafeRegularFile(string path) =>
        File.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    private static bool IsCanonicalSteamLanguage(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 32 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_') &&
        string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal);

    private static bool IsLowerHexSha1(string value) =>
        value.Length == 40 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsLocalizedCapsuleFileName(string value)
    {
        const string prefix = "library_capsule_";
        const string suffix = ".jpg";
        return value.StartsWith(prefix, StringComparison.Ordinal) &&
               value.EndsWith(suffix, StringComparison.Ordinal) &&
               IsCanonicalSteamLanguage(value[prefix.Length..^suffix.Length]);
    }

    private static byte[] ReadStable(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        if (stream.Length is < 8 or > MaximumImageBytes)
            throw new InvalidOperationException("Steam 封面缓存大小无效。");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static async Task<byte[]> TranscodeSteamLibraryCapsuleAsync(
        byte[] source,
        CancellationToken cancellationToken)
    {
        if (source.Length < 3 || source[0] != 0xFF || source[1] != 0xD8 || source[2] != 0xFF)
            throw new InvalidOperationException("Steam library capsule 不是 JPEG。");
        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input))
        {
            writer.WriteBytes(source);
            await writer.StoreAsync().AsTask(cancellationToken);
            await writer.FlushAsync().AsTask(cancellationToken);
            writer.DetachStream();
        }
        input.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(input).AsTask(cancellationToken);
        if (!IsSteamLibraryCapsuleSize(decoder.PixelWidth, decoder.PixelHeight))
            throw new InvalidOperationException("Steam library capsule 尺寸不是 600×900 或 300×450。");
        using var output = new InMemoryRandomAccessStream();
        var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.ColorManageToSRgb)
            .AsTask(cancellationToken);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output)
            .AsTask(cancellationToken);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            decoder.PixelWidth,
            decoder.PixelHeight,
            decoder.DpiX,
            decoder.DpiY,
            pixelData.DetachPixelData());
        await encoder.FlushAsync().AsTask(cancellationToken);
        if (output.Size is < 8 or > MaximumImageBytes)
            throw new InvalidOperationException("转换后的 PNG 尺寸无效。");
        var png = new byte[checked((int)output.Size)];
        using var reader = new DataReader(output.GetInputStreamAt(0));
        await reader.LoadAsync((uint)png.Length).AsTask(cancellationToken);
        reader.ReadBytes(png);
        await ValidatePngAsync(png, true, cancellationToken);
        return png;
    }

    private static async Task ValidatePngAsync(
        byte[] bytes, bool requireSteamCapsuleSize, CancellationToken cancellationToken)
    {
        if (bytes.Length is < 8 or > MaximumImageBytes ||
            !bytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            throw new InvalidOperationException("下载结果不是有效的 PNG 封面。");
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync().AsTask(cancellationToken);
            await writer.FlushAsync().AsTask(cancellationToken);
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 ||
            (ulong)decoder.PixelWidth * decoder.PixelHeight > MaximumPixels ||
            (requireSteamCapsuleSize &&
             !IsSteamLibraryCapsuleSize(decoder.PixelWidth, decoder.PixelHeight)))
            throw new InvalidOperationException("PNG 封面尺寸无效。");
    }

    private static bool IsSteamLibraryCapsuleSize(uint width, uint height) =>
        (width == 600 && height == 900) || (width == 300 && height == 450);

    private async Task<CoverCandidate?> LoadCandidateAsync(
        string id, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"{DatabaseRoot}/games/{Uri.EscapeDataString(id)}.json", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("name", out var nameProperty) ||
                !root.TryGetProperty("cover", out var cover) ||
                !cover.TryGetProperty("url", out var urlProperty)) return null;
            var rawUrl = urlProperty.GetString();
            var name = nameProperty.GetString();
            if (string.IsNullOrWhiteSpace(rawUrl) || string.IsNullOrWhiteSpace(name)) return null;
            var slash = rawUrl.LastIndexOf('/');
            var dot = rawUrl.LastIndexOf('.');
            if (slash < 0 || dot <= slash) return null;
            var slug = rawUrl[(slash + 1)..dot];
            return new CoverCandidate(name, $"igdb_{id}",
                $"https://{ImageHost}/igdb/image/upload/t_cover_big/{slug}.jpg",
                $"https://{ImageHost}/igdb/image/upload/t_cover_big_2x/{slug}.png",
                "gameDbIgdb", id, "thirdPartyArtworkLocalUseOnlyNoRedistribution");
        }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
    }

    private sealed record CoverCacheDocument(
        int SchemaVersion,
        IReadOnlyList<CoverCacheAuthority> Entries);
}
