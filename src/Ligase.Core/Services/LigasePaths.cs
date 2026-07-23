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
    public string CoversDirectory => Path.Combine(RootDirectory, "covers");
    public string ApolloDirectory => Path.Combine(RootDirectory, "apollo");
    public string ApolloConfigFile => Path.Combine(ApolloDirectory, "sunshine.conf");
    public string ApolloAppsFile => Path.Combine(ApolloDirectory, "apps.json");
    public string ApolloLogFile => Path.Combine(ApolloDirectory, "apollo.log");
    public string ApolloStateFile => Path.Combine(ApolloDirectory, "state.json");
    public string ApolloPrivateKeyFile => Path.Combine(ApolloDirectory, "credentials", "cakey.pem");
    public string ApolloCertificateFile => Path.Combine(ApolloDirectory, "credentials", "cacert.pem");

    public static LigasePaths CreateDefault() =>
        new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ligase Host"));
}
