using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Ligase.Host.Desktop.Services;
using Ligase.Host.Desktop.Presentation.Settings.Firewall;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Ligase.Host.Desktop.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly HostPreferencesService _preferences;
    private readonly PairingNotificationService _notifications;
    public FirewallSettingsViewModel FirewallViewModel { get; }
    private bool _loading;

    public SettingsPage()
    {
        _preferences = ((App)Application.Current)
            .Services.GetRequiredService<HostPreferencesService>();
        _notifications = ((App)Application.Current)
            .Services.GetRequiredService<PairingNotificationService>();
        FirewallViewModel = ((App)Application.Current)
            .Services.GetRequiredService<FirewallSettingsViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _loading = true;
        CloseToTrayToggle.IsOn = _preferences.Current.CloseToTray;
        StartWithWindowsToggle.IsOn = _preferences.Current.StartWithWindows;
        LanguageComboBox.SelectedIndex = (int)_preferences.Current.Language;
        _loading = false;
        await FirewallViewModel.LoadAsync();
    }

    private async void OnCloseToTrayToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            await _preferences.SetCloseToTrayAsync(CloseToTrayToggle.IsOn);
            SetSaveStatus("后台运行设置已保存");
        }
        catch (Exception exception)
        {
            SetSaveStatus($"保存失败：{exception.Message}", isError: true);
        }
    }

    private async void OnStartWithWindowsToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            await _preferences.SetStartWithWindowsAsync(StartWithWindowsToggle.IsOn);
            SetSaveStatus(StartWithWindowsToggle.IsOn
                ? "已启用开机后台启动"
                : "已关闭开机自动启动");
        }
        catch (Exception exception)
        {
            _loading = true;
            StartWithWindowsToggle.IsOn = _preferences.Current.StartWithWindows;
            _loading = false;
            SetSaveStatus($"保存失败：{exception.Message}", isError: true);
        }
    }

    private async void OnLanguageSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_loading || LanguageComboBox.SelectedIndex < 0) return;
        try
        {
            var language = (HostLanguage)LanguageComboBox.SelectedIndex;
            await _preferences.SetLanguageAsync(language);
            SetSaveStatus(language switch
            {
                HostLanguage.SimplifiedChinese => "语言已保存，重启 Ligase Host 后生效",
                HostLanguage.English => "Language saved. Restart Ligase Host to apply it.",
                _ => "语言已保存，将在重启后跟随 Windows 系统语言"
            });
        }
        catch (Exception exception)
        {
            _loading = true;
            LanguageComboBox.SelectedIndex = (int)_preferences.Current.Language;
            _loading = false;
            SetSaveStatus($"保存失败：{exception.Message}", isError: true);
        }
    }

    private void SetSaveStatus(string message, bool isError = false)
    {
        SaveStatusText.Text = message;
        SaveStatusText.Foreground = (Brush)Application.Current.Resources[
            isError ? "LigaseErrorDangerBrush" : "LigaseSuccessBrush"];
    }

    private async void OnExitApplication(object sender, RoutedEventArgs e)
    {
        await ((App)Application.Current).ExitAsync();
    }

    private void OnTestNotification(object sender, RoutedEventArgs e)
    {
        try
        {
            _notifications.ShowTest();
            SetSaveStatus("测试通知已发送；点击通知应返回此窗口。");
        }
        catch (Exception exception)
        {
            SetSaveStatus($"通知发送失败：{exception.Message}", isError: true);
        }
    }

    private async void OnConfigureFirewall(object sender, RoutedEventArgs e)
    {
        ConfigureFirewallButton.IsEnabled = false;
        try
        {
            await FirewallViewModel.ExecutePrimaryActionAsync();
        }
        finally
        {
            ConfigureFirewallButton.IsEnabled = true;
        }
    }
}
