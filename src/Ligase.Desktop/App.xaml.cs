using Ligase.Host.Desktop.ViewModels;
using Ligase.Host.Desktop.Services;
using Ligase.Host.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Windows.Globalization;

namespace Ligase.Host.Desktop;

public partial class App : Application
{
    private readonly IHost _host;
    private bool _isExiting;
    public IServiceProvider Services => _host.Services;

    public App()
    {
        InitializeComponent();
        _host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                var paths = LigasePaths.CreateFromCommandLine(
                    Environment.GetCommandLineArgs(),
                    environmentDataRoot: Environment.GetEnvironmentVariable(
                        "LIGASE_DATA_ROOT"),
                    bootstrapFile: Path.Combine(
                        AppContext.BaseDirectory,
                        "ligase-bootstrap.json"));
                services.AddSingleton(paths);
                services.AddSingleton<ISteamInstallationLocator, WindowsSteamInstallationLocator>();
                services.AddSingleton<ISteamLibraryService, SteamLibraryService>();
                services.AddSingleton(_ =>
                {
                    var client = new HttpClient
                    {
                        Timeout = TimeSpan.FromSeconds(12)
                    };
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("LigaseHost/1.0");
                    return client;
                });
                services.AddSingleton<CoverArtService>();
                services.AddSingleton<IApolloAppsWriter, ApolloAppsWriter>();
                services.AddSingleton<ApplicationLibrary>();
                services.AddSingleton<IApplicationLibrary>(provider =>
                    provider.GetRequiredService<ApplicationLibrary>());
                services.AddSingleton<ApolloPortAllocator>();
                services.AddSingleton<ApolloInstanceManager>();
                services.AddSingleton<ApolloCoreLocator>();
                services.AddSingleton<IManagedPairingCoreResolver, ManagedPairingCoreResolver>();
                services.AddSingleton<IAttendedPairingRepository, AttendedPairingRepository>();
                services.AddSingleton<LibraryAuthorityService>();
                services.AddSingleton<ILibraryAuthorityService>(provider =>
                    provider.GetRequiredService<LibraryAuthorityService>());
                services.AddSingleton<LibraryMutationCoordinator>();
                services.AddSingleton<HostPreferencesService>();
                services.AddSingleton<LigaseSyncDocumentWriter>();
                services.AddSingleton<StreamingSettingsService>();
                services.AddSingleton<ApolloDeviceService>();
                services.AddSingleton<ApolloSessionService>();
                services.AddSingleton<IDesktopPreviewService, GdiDesktopPreviewService>();
                services.AddSingleton<SingleInstanceService>();
                services.AddSingleton<WindowsTrayIconService>();
                services.AddSingleton<AttendedPairingCoordinator>();
                services.AddSingleton<PairingNotificationService>();
                services.AddSingleton<AttendedPairingUiCoordinator>();
                services.AddTransient<GameLibraryViewModel>();
                services.AddTransient<AddApplicationViewModel>();
                services.AddTransient<StreamMonitorViewModel>();
                services.AddTransient<DevicesViewModel>();
                services.AddTransient<AttendedPairingViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // AppNotificationManager must be registered before the first call to
        // AppInstance.GetActivatedEventArgs() so an unpackaged cold-start
        // notification activation is surfaced to this process.
        var notifications =
            _host.Services.GetRequiredService<PairingNotificationService>();
        notifications.Initialize();

        var singleInstance = _host.Services.GetRequiredService<SingleInstanceService>();
        var activation = await singleInstance.TryAcquireAsync();
        if (!activation.IsPrimary)
        {
            Exit();
            return;
        }

        // Show the shell before starting file and core initialization. A slow
        // encoder probe or damaged local state must not look like the app
        // failed to launch.
        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Activate();
        _host.Services.GetRequiredService<AttendedPairingUiCoordinator>()
            .HandleInitialActivation(activation.Arguments);

        var preferences = _host.Services.GetRequiredService<HostPreferencesService>();
        try
        {
            await preferences.InitializeAsync();
            ApplicationLanguages.PrimaryLanguageOverride =
                HostPreferencesService.GetPrimaryLanguageOverride(preferences.Current.Language);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Ligase preference initialization failed: {exception.Message}");
        }

        try
        {
            var library = await _host.Services.GetRequiredService<IApplicationLibrary>().LoadAsync();
            var streaming = await _host.Services.GetRequiredService<StreamingSettingsService>().LoadAsync();
            await _host.Services.GetRequiredService<LigaseSyncDocumentWriter>()
                .WriteAsync(library, streaming);
            await _host.Services.GetRequiredService<IApolloAppsWriter>().WriteAsync(library.Items);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Ligase projection initialization failed: {exception.Message}");
        }

        var managedCore = _host.Services.GetRequiredService<ApolloInstanceManager>();
        var coreLocator = _host.Services.GetRequiredService<ApolloCoreLocator>();
        if ((await coreLocator.DiscoverAsync()).Count == 0)
            await managedCore.StartAsync();
        // The core process can exist before serverinfo is ready. Refresh once
        // after a short bounded readiness window so the shell does not remain
        // in a stale read-only state for the whole session.
        await Task.Delay(1500);
        await window.RefreshCoreStatusAsync();
        _host.Services.GetRequiredService<AttendedPairingCoordinator>().Start();
        if (Environment.GetCommandLineArgs().Any(argument =>
                string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase)))
        {
            window.HideToTray();
        }
    }

    public async Task ExitAsync()
    {
        if (_isExiting) return;
        _isExiting = true;
        Services.GetRequiredService<WindowsTrayIconService>().Dispose();
        await Services.GetRequiredService<AttendedPairingCoordinator>()
            .DisposeAsync();
        Services.GetRequiredService<PairingNotificationService>().Dispose();
        await Services.GetRequiredService<ApolloInstanceManager>().StopAsync();
        Services.GetRequiredService<SingleInstanceService>().Dispose();
        _host.Dispose();
        Exit();
    }
}
