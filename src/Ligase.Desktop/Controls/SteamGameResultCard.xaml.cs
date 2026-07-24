using Ligase.Host.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ligase.Host.Desktop.Controls;

public sealed partial class SteamGameResultCard : UserControl
{
    public static readonly DependencyProperty ResultProperty =
        DependencyProperty.Register(
            nameof(Result),
            typeof(SteamGameResultViewModel),
            typeof(SteamGameResultCard),
            new PropertyMetadata(null));

    public SteamGameResultViewModel? Result
    {
        get => (SteamGameResultViewModel?)GetValue(ResultProperty);
        set => SetValue(ResultProperty, value);
    }

    public event RoutedEventHandler? AddRequested;
    public event RoutedEventHandler? RemoveRequested;
    public event RoutedEventHandler? FindCoverRequested;

    public SteamGameResultCard()
    {
        InitializeComponent();
    }

    private void OnAdd(object sender, RoutedEventArgs e) =>
        AddRequested?.Invoke(this, e);

    private void OnRemove(object sender, RoutedEventArgs e) =>
        RemoveRequested?.Invoke(this, e);

    private void OnFindCover(object sender, RoutedEventArgs e) =>
        FindCoverRequested?.Invoke(this, e);
}
