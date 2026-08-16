using System.Text.Json;

namespace Ligase.Host.Core.Services;

public sealed class LigasePaths
{
    public LigasePaths(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }
    public string LibraryFile => Path.Combine(RootDirectory, "library.json");
    public string StreamingSettingsFile => Path.Combine(RootDirectory, "streaming.json");
    public string SyncFile => Path.Combine(RootDirectory, "ligase-sync.json");
    public string PreferencesFile => Path.Combine(RootDirectory, "preferences.json");
    public string AuthorityFile => Path.Combine(RootDirectory, "ligase-authority.json");
    public string CoversDirectory => Path.Combine(RootDirectory, "covers");
    public string CoverCacheAuthorityFile => Path.Combine(
        RootDirectory,
        "cover-cache-authority-v1.json");
    public string ApolloDirectory => Path.Combine(RootDirectory, "apollo");
    public string ApolloConfigFile => Path.Combine(ApolloDirectory, "sunshine.conf");
    public string ApolloAppsFile => Path.Combine(ApolloDirectory, "apps.json");
    public string ApolloLogFile => Path.Combine(ApolloDirectory, "apollo.log");
    public string ApolloStateFile => Path.Combine(ApolloDirectory, "state.json");
    public string ApolloPrivateKeyFile => Path.Combine(ApolloDirectory, "credentials", "cakey.pem");
    public string ApolloCertificateFile => Path.Combine(ApolloDirectory, "credentials", "cacert.pem");

    public static LigasePaths CreateDefault() =>
        CreateDefault(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static LigasePaths CreateDefault(string localApplicationData) =>
        new(Path.Combine(localApplicationData, "Ligase Host"));

    public static LigasePaths CreateFromCommandLine(
        IReadOnlyList<string> arguments,
        string? localApplicationData = null,
        string? environmentDataRoot = null,
        string? bootstrapFile = null)
    {
        string? explicitRoot = null;
        for (var index = 1; index < arguments.Count; index++)
        {
            if (!string.Equals(
                    arguments[index],
                    "--data-root",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (explicitRoot is not null)
                throw new ArgumentException("--data-root 只能指定一次。", nameof(arguments));
            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
                throw new ArgumentException("--data-root 需要一个绝对目录。", nameof(arguments));

            explicitRoot = arguments[index];
        }

        explicitRoot ??= environmentDataRoot;
        if (explicitRoot is null &&
            bootstrapFile is not null &&
            File.Exists(bootstrapFile))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(bootstrapFile));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("dataRoot", out var dataRoot) ||
                dataRoot.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException(
                    "ligase-bootstrap.json 必须包含字符串 dataRoot。");
            }

            explicitRoot = dataRoot.GetString();
        }
        if (explicitRoot is null)
        {
            return CreateDefault(localApplicationData ??
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }

        if (!Path.IsPathFullyQualified(explicitRoot))
            throw new ArgumentException("--data-root 必须是绝对目录。", nameof(arguments));

        return new LigasePaths(explicitRoot);
    }
}
