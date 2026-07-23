using Ligase.Host.Desktop.ViewModels;
using Ligase.Host.Desktop.Services;
using Ligase.Host.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

namespace Ligase.Host.Desktop;

public partial class App : Application
{
    private readonly IHost _host;
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
                services.AddSingleton<IDesktopPreviewService, GdiDesktopPreviewService>();
                services.AddTransient<GameLibraryViewModel>();
                services.AddTransient<AddApplicationViewModel>();
                services.AddTransient<StreamMonitorViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await _host.StartAsync();
        var library = await _host.Services.GetRequiredService<IApplicationLibrary>().LoadAsync();
        await _host.Services.GetRequiredService<IApolloAppsWriter>().WriteAsync(library.Items);
        await _host.Services.GetRequiredService<ApolloInstanceManager>().StartAsync();
        _host.Services.GetRequiredService<MainWindow>().Activate();
    }
}
