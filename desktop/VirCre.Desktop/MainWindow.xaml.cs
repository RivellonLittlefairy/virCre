using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VirCre.Presentation;
using Windows.Graphics;
using Windows.UI;

namespace VirCre.Desktop;

public sealed partial class MainWindow : Window {
    private readonly ShellViewModel model = new();
    private bool ready;
    private XamlRoot? observedRoot;
    private double minimumScale;
    private bool allowClose;

    public MainWindow() {
        catalogController = new(phone, model);
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        Root.ActualThemeChanged += (_, _) => UpdateCaptionColors();
        model.PropertyChanged += (_, _) => RenderState();
        AppGrid.ItemsSource = model.Apps;
        ready = true;
        UpdateCaptionColors();
        Navigation.SelectedItem = Navigation.MenuItems[0];
        AppWindow.Closing += AppWindow_Closing;
        Closed += (_, _) => {
            closing = true;
            lifetime.Cancel();
            catalogController.Dispose();
            lifetime.Dispose();
            if (observedRoot != null) observedRoot.Changed -= XamlRoot_Changed;
        };
        RenderState();
    }
    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args) {
        if (allowClose) return;
        args.Cancel = true;
        if (closing) return;
        closing = true;
        Root.IsHitTestVisible = false;
        FooterStatus.Text = "正在停止设备读取…";
        lifetime.Cancel();
        try { await Task.WhenAll(catalogController.StopAsync(), refreshTask); }
        catch (Exception error) when (error is OperationCanceledException or IOException or TimeoutException) {
            // Each metadata command already kills and awaits its own process before it completes.
            System.Diagnostics.Debug.WriteLine("Metadata shutdown: " + error.GetType().Name);
        }
        allowClose = true;
        Close();
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e) {
        if (observedRoot != null) return;
        observedRoot = Root.XamlRoot;
        observedRoot.Changed += XamlRoot_Changed;
        var scale = Root.XamlRoot.RasterizationScale;
        AppWindow.Resize(new SizeInt32((int)(1120 * scale), (int)(760 * scale)));
        UpdateMinimumSize();
        if (DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary) is { } display) {
            var area = display.WorkArea;
            AppWindow.Move(new PointInt32(area.X + Math.Max(0, (area.Width - AppWindow.Size.Width) / 2),
                area.Y + Math.Max(0, (area.Height - AppWindow.Size.Height) / 2)));
        }
        await RefreshDevicesAsync();
    }
    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateMinimumSize();
    private void UpdateMinimumSize() {
        var scale = observedRoot?.RasterizationScale ?? 1;
        if (scale == minimumScale) return;
        minimumScale = scale;
        if (AppWindow.Presenter is OverlappedPresenter presenter) {
            presenter.PreferredMinimumWidth = (int)Math.Ceiling(900 * scale);
            presenter.PreferredMinimumHeight = (int)Math.Ceiling(600 * scale);
        }
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args) {
        if (!ready) return;
        var page = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string;
        if (page != null) model.Navigate(page);
        SearchBox.Text = model.Query;
    }
    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) {
        if (ready && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) model.Search(sender.Text);
    }
    private void Favorite_Click(object sender, RoutedEventArgs e) {
        if ((sender as Button)?.Tag is string packageName) model.ToggleFavorite(packageName);
    }
    private void AppGrid_SizeChanged(object sender, SizeChangedEventArgs e) {
        if (AppGrid.ItemsPanelRoot is ItemsWrapGrid panel && e.NewSize.Width > 0) {
            // Reserve the scroll gutter and each container's 6 px margins on both sides.
            var available = Math.Max(1, e.NewSize.Width - 16);
            var columns = Math.Max(1, (int)(available / 242));
            panel.ItemWidth = Math.Max(1, Math.Floor(available / columns) - 12);
        }
    }
    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (!ready) return;
        Root.RequestedTheme = ThemeChoice.SelectedIndex switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default };
    }
    private void UpdateCaptionColors() {
        var dark = Root.ActualTheme == ElementTheme.Dark;
        AppWindow.TitleBar.ButtonForegroundColor = dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0);
        AppWindow.TitleBar.ButtonInactiveForegroundColor = dark ? Color.FromArgb(255, 160, 160, 160) : Color.FromArgb(255, 100, 100, 100);
        AppWindow.TitleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
    }
    private void RenderState() {
        if (!ready || closing) return;
        PageTitle.Text = model.Title;
        PageDescription.Text = model.Description;
        AppCount.Text = model.ResultCount;
        DeviceName.Text = model.DeviceLabel;
        DeviceHint.Text = scanning ? "正在检查设备…" : model.DeviceStatus;
        ConnectionStatus.Text = model.DeviceStatus;
        FooterStatus.Text = model.FooterText;
        LoadingRing.IsActive = model.IsLoading || scanning;
        SearchBox.IsEnabled = !model.IsLoading && model.HasDevice;
        EmptyTitle.Text = scanning ? "正在查找手机" : model.EmptyTitle;
        EmptyDescription.Text = scanning ? "正在检查已授权的 ADB 设备。" : model.EmptyDescription;
        if (SearchBox.Text != model.Query) SearchBox.Text = model.Query;
        EmptyIcon.Glyph = model.Page == "running" ? "\uE7F4" : model.Page == "favorites" ? "\uE734" : "\uE721";
        var settings = model.Page == "settings";
        var running = model.Page == "running";
        SettingsContent.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        SearchRow.Visibility = settings || running ? Visibility.Collapsed : Visibility.Visible;
        AppGrid.Visibility = settings || model.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = !settings && model.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }
}
