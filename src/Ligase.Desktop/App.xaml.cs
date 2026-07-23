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
                var paths = LigasePaths.CreateDefault();
                services.AddSingleton(paths);
                services.AddSingleton<ISteamInstallationLocator, WindowsSteamInstallationLocator>();
                services.AddSingleton<ISteamLibraryService, SteamLibraryService>();
                services.AddSingleton<IApolloAppsWriter, ApolloAppsWriter>();
                services.AddSingleton<IApplicationLibrary, ApplicationLibrary>();
                services.AddSingleton<ApolloPortAllocator>();
                services.AddSingleton<ApolloInstanceManager>();
                services.AddSingleton<HostPreferencesService>();
                services.AddSingleton<LigaseSyncDocumentWriter>();
                services.AddSingleton<StreamingSettingsService>();
                services.AddSingleton<ApolloDeviceService>();
                services.AddSingleton<IDesktopPreviewService, GdiDesktopPreviewService>();
                services.AddSingleton<SingleInstanceService>();
                services.AddSingleton<WindowsTrayIconService>();
                services.AddTransient<GameLibraryViewModel>();
                services.AddTransient<AddApplicationViewModel>();
                services.AddTransient<StreamMonitorViewModel>();
                services.AddTransient<DevicesViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var singleInstance = _host.Services.GetRequiredService<SingleInstanceService>();
        if (!singleInstance.TryAcquire())
        {
            Exit();
            return;
        }

        await _host.StartAsync();
        var preferences = _host.Services.GetRequiredService<HostPreferencesService>();
        await preferences.InitializeAsync();
        ApplicationLanguages.PrimaryLanguageOverride =
            HostPreferencesService.GetPrimaryLanguageOverride(preferences.Current.Language);
        var library = await _host.Services.GetRequiredService<IApplicationLibrary>().LoadAsync();
        var streaming = await _host.Services.GetRequiredService<StreamingSettingsService>().LoadAsync();
        await _host.Services.GetRequiredService<LigaseSyncDocumentWriter>()
            .WriteAsync(library, streaming);
        await _host.Services.GetRequiredService<IApolloAppsWriter>().WriteAsync(library.Items);
        await _host.Services.GetRequiredService<ApolloInstanceManager>().StartAsync();
        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Activate();
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
        await Services.GetRequiredService<ApolloInstanceManager>().StopAsync();
        Services.GetRequiredService<SingleInstanceService>().Dispose();
        await _host.StopAsync();
        Exit();
    }
}
