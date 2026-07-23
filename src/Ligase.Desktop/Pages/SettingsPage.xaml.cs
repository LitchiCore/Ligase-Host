using Ligase.Host.Core.Models;
using Ligase.Host.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Ligase.Host.Desktop.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly HostPreferencesService _preferences;
    private bool _loading;

    public SettingsPage()
    {
        _preferences = ((App)Application.Current)
            .Services.GetRequiredService<HostPreferencesService>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _loading = true;
        CloseToTrayToggle.IsOn = _preferences.Current.CloseToTray;
        StartWithWindowsToggle.IsOn = _preferences.Current.StartWithWindows;
        LanguageComboBox.SelectedIndex = (int)_preferences.Current.Language;
        _loading = false;
    }

    private async void OnCloseToTrayToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            await _preferences.SetCloseToTrayAsync(CloseToTrayToggle.IsOn);
            SaveStatusText.Text = "后台运行设置已保存";
        }
        catch (Exception exception)
        {
            SaveStatusText.Text = $"保存失败：{exception.Message}";
        }
    }

    private async void OnStartWithWindowsToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            await _preferences.SetStartWithWindowsAsync(StartWithWindowsToggle.IsOn);
            SaveStatusText.Text = StartWithWindowsToggle.IsOn
                ? "已启用开机后台启动"
                : "已关闭开机自动启动";
        }
        catch (Exception exception)
        {
            _loading = true;
            StartWithWindowsToggle.IsOn = _preferences.Current.StartWithWindows;
            _loading = false;
            SaveStatusText.Text = $"保存失败：{exception.Message}";
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
            SaveStatusText.Text = language switch
            {
                HostLanguage.SimplifiedChinese => "语言已保存，重启 Ligase Host 后生效",
                HostLanguage.English => "Language saved. Restart Ligase Host to apply it.",
                _ => "语言已保存，将在重启后跟随 Windows 系统语言"
            };
        }
        catch (Exception exception)
        {
            _loading = true;
            LanguageComboBox.SelectedIndex = (int)_preferences.Current.Language;
            _loading = false;
            SaveStatusText.Text = $"保存失败：{exception.Message}";
        }
    }

    private async void OnExitApplication(object sender, RoutedEventArgs e)
    {
        await ((App)Application.Current).ExitAsync();
    }
}
