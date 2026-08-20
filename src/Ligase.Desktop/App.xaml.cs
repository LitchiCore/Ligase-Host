using Ligase.Host.Core.Application.LayoutCatalog;
using Ligase.Host.Core.Application.Installation;
using Ligase.Host.Core.Application.WindowsFirewall;
using Ligase.Host.Core.Domain.Installation;
using Ligase.Host.Core.Infrastructure.Storage;
using Ligase.Host.Core.Infrastructure.Windows;
using Ligase.Host.Desktop.Platform.Windows.Firewall;
using Ligase.Host.Desktop.Presentation.LayoutCatalog;
using Ligase.Host.Desktop.Presentation.Onboarding;
using Ligase.Host.Desktop.Presentation.Settings.Firewall;
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
    private readonly IrreversibleExitGate _exit = new();
    public IServiceProvider Services => _host.Services;

    public App()
    {
        InitializeComponent();
        _host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                var installationLayout =
                    InstallationLayoutResolver.ResolveFromDesktopBase(
                        AppContext.BaseDirectory);
                var paths = LigasePaths.CreateFromCommandLine(
                    Environment.GetCommandLineArgs(),
                    environmentDataRoot: Environment.GetEnvironmentVariable(
                        "LIGASE_DATA_ROOT"),
                    bootstrapFile: installationLayout.BootstrapPath);
                services.AddSingleton(installationLayout);
                services.AddSingleton(paths);
                services.AddSingleton<DataRootIdentityService>();
                services.AddSingleton<ILayoutCatalogRepository>(_ =>
                    new JsonLayoutCatalogRepository(
                        paths.LayoutCatalogFile));
                services.AddSingleton<LayoutCatalogService>();
                services.AddSingleton<
                    IInstallationReadbackSource,
                    WindowsInstallationReadbackSource>();
                services.AddSingleton<
                    IInstallationReadinessService,
                    InstallationReadinessService>();
                services.AddSingleton<
                    IInstallationSetupProbe,
                    HostInstallationSetupProbe>();
                services.AddSingleton<
                    IInstallationRecoveryLauncher,
                    UnavailableInstallationRecoveryLauncher>();
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
                services.AddSingleton<ICoverArtifactService>(provider =>
                    provider.GetRequiredService<CoverArtService>());
                services.AddSingleton<IApolloAppsWriter, ApolloAppsWriter>();
                services.AddSingleton<ApplicationLibrary>();
                services.AddSingleton<IApplicationLibrary>(provider =>
                    provider.GetRequiredService<ApplicationLibrary>());
                services.AddSingleton<ApolloPortAllocator>();
                services.AddSingleton<ApolloInstanceManager>();
                services.AddSingleton<WindowsFirewallPlanner>();
                services.AddSingleton<WindowsFirewallEnvironmentReader>();
                services.AddSingleton<
                    IFirewallElevatedProcessLauncher,
                    SystemFirewallElevatedProcessLauncher>();
                services.AddSingleton<
                    IFirewallAccessGateway,
                    WindowsFirewallAccessGateway>();
                services.AddTransient<FirewallSettingsViewModel>();
                services.AddSingleton<ApolloCoreLocator>();
                services.AddSingleton<IManagedPairingCoreResolver, ManagedPairingCoreResolver>();
                services.AddSingleton<IAttendedPairingRepository, AttendedPairingRepository>();
                services.AddSingleton<LibraryAuthorityService>();
                services.AddSingleton<ILibraryAuthorityService>(provider =>
                    provider.GetRequiredService<LibraryAuthorityService>());
                services.AddSingleton<LibraryMutationCoordinator>();
                services.AddSingleton<WindowsShortcutResolver>();
                services.AddSingleton<HostPreferencesService>();
                services.AddSingleton<LigaseSyncDocumentWriter>();
                services.AddSingleton<StreamingSettingsService>();
                services.AddSingleton<ApolloDeviceService>();
                services.AddSingleton<IApolloDeviceService>(provider =>
                    provider.GetRequiredService<ApolloDeviceService>());
                services.AddSingleton<ApolloSessionService>();
                services.AddSingleton<IVirtualDisplayControlService,
                    VirtualDisplayControlService>();
                services.AddSingleton<IDesktopPreviewService, GdiDesktopPreviewService>();
                services.AddSingleton<SingleInstanceService>();
                services.AddSingleton<InstallerShutdownService>();
                services.AddSingleton<WindowsTrayIconService>();
                services.AddSingleton<AttendedPairingCoordinator>();
                services.AddSingleton<DevicePresenceCoordinator>();
                services.AddSingleton<IPairingNotificationPlatform,
                    WindowsPairingNotificationPlatform>();
                services.AddSingleton<PairingNotificationEvidenceStore>();
                services.AddSingleton<PairingNotificationService>();
                services.AddSingleton<AttendedPairingUiCoordinator>();
                services.AddTransient<GameLibraryViewModel>();
                services.AddTransient<OverviewViewModel>();
                services.AddTransient<LayoutCatalogViewModel>();
                services.AddTransient<HostSetupViewModel>();
                services.AddTransient<AddApplicationViewModel>();
                services.AddTransient<ExistingItemCoverViewModel>();
                services.AddTransient<IExistingItemCoverWorkflow>(provider =>
                    provider.GetRequiredService<ExistingItemCoverViewModel>());
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
        _host.Services.GetRequiredService<InstallerShutdownService>().Start(
            () => window.InvokeInstallerShutdownAsync(
                PrepareForInstallerShutdownAsync),
            window.ExitAfterInstallerShutdown);
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
        window.StartCoreReadiness();
        _host.Services.GetRequiredService<AttendedPairingCoordinator>().Start();
        _host.Services.GetRequiredService<DevicePresenceCoordinator>().Start();
        if (Environment.GetCommandLineArgs().Any(argument =>
                string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase)))
        {
            window.HideToTray();
        }
    }

    public async Task ExitAsync()
    {
        if (!_exit.TryCommit()) return;
        Services.GetRequiredService<SingleInstanceService>().BeginExit();
        Services.GetRequiredService<MainWindow>().AllowApplicationExit();
        Services.GetRequiredService<WindowsTrayIconService>().Dispose();
        try
        {
            await Services.GetRequiredService<AttendedPairingCoordinator>()
                .DisposeAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(3));
            await Services.GetRequiredService<DevicePresenceCoordinator>()
                .DisposeAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(3));
            Services.GetRequiredService<PairingNotificationService>().Dispose();
            await Services.GetRequiredService<ApolloInstanceManager>()
                .StopAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            // Never leave the Desktop window behind after the managed core
            // has already stopped. Process teardown completes remaining work.
        }
        finally
        {
            Services.GetRequiredService<SingleInstanceService>().Dispose();
            // Host disposal can synchronously wait for the same timed-out
            // background service. App.Exit owns final process teardown.
            Exit();
        }
    }

    private async Task<InstallerShutdownOutcome> PrepareForInstallerShutdownAsync()
    {
        if (!_exit.TryCommit())
            return new InstallerShutdownOutcome(
                true, "exitAlreadyCommitted", "inProgress",
                "notObserved", true);

        // A validated current-user pipe request is the irreversible transition.
        // From this point activation, tray restore, readiness and new windows
        // stay disabled even when best-effort component cleanup is incomplete.
        var window = Services.GetRequiredService<MainWindow>();
        window.AllowApplicationExit();
        Services.GetRequiredService<SingleInstanceService>().BeginExit();
        Services.GetRequiredService<WindowsTrayIconService>().Dispose();
        Services.GetRequiredService<PairingNotificationService>().Dispose();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var cleanupState = "completed";
        var coreStopCode = "notObserved";
        var coreStillAlive = true;
        try
        {
            var pairingTask = Services.GetRequiredService<AttendedPairingCoordinator>()
                .DisposeAsync().AsTask();
            var presenceTask = Services.GetRequiredService<DevicePresenceCoordinator>()
                .DisposeAsync().AsTask();
            var coreTask = Services.GetRequiredService<ApolloInstanceManager>()
                .StopForInstallerAsync();
            try
            {
                var core = await coreTask.WaitAsync(TimeSpan.FromSeconds(5));
                coreStopCode = core.Code;
                coreStillAlive = core.ProcessStillAlive;
                if (core.ProcessStillAlive) cleanupState = "deferred";
            }
            catch (TimeoutException)
            {
                cleanupState = "deferred";
                coreStopCode = "gracefulTimeout";
            }
            catch
            {
                cleanupState = "faulted";
                coreStopCode = "gracefulObservationFailed";
            }

            var pairingBudget = TimeSpan.FromSeconds(6) - clock.Elapsed;
            if (pairingBudget > TimeSpan.Zero)
            {
                try { await pairingTask.WaitAsync(pairingBudget); }
                catch { cleanupState = cleanupState == "completed" ? "deferred" : cleanupState; }
            }
            var presenceBudget = TimeSpan.FromSeconds(6) - clock.Elapsed;
            if (presenceBudget > TimeSpan.Zero)
            {
                try { await presenceTask.WaitAsync(presenceBudget); }
                catch { cleanupState = cleanupState == "completed" ? "deferred" : cleanupState; }
            }
        }
        catch
        {
            cleanupState = "faulted";
        }

        // The installer now owns actual PID and Restart Manager convergence.
        // In-process cleanup status is evidence, never permission to re-enter
        // the running UI state after an authenticated exit request.
        return new InstallerShutdownOutcome(
            true, "exitCommitted", cleanupState, coreStopCode, coreStillAlive);
    }

    internal void CompleteInstallerShutdown()
    {
        Services.GetRequiredService<SingleInstanceService>().Dispose();
        Exit();
    }
}
