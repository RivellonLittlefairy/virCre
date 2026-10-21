using Microsoft.UI.Windowing;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VirCre.Presentation;
using Windows.Graphics;
using Windows.UI;

namespace VirCre.Desktop;

public sealed partial class MainWindow : Window {
    private readonly ShellViewModel model;
    private bool ready;
    private XamlRoot? observedRoot;
    private double minimumScale;
    private bool allowClose;
    private readonly ShutdownCoordinator windowShutdown=new();

    public MainWindow(bool shortcutOnly=false) {
        this.shortcutOnly=shortcutOnly;
        windowHost=new(stage=>RuntimeDiagnostics.Write(stage.StartsWith("touch-",StringComparison.Ordinal)?"touch-stage":stage.StartsWith("power-",StringComparison.Ordinal)?"power-stage":"input-rejected",details:stage),message=>{
            RuntimeDiagnostics.Write("virtual-camera",details:message);
            DispatcherQueue.TryEnqueue(()=>ShowAction(message));
        },stage=>RuntimeDiagnostics.Write("camera-stage",details:stage));
        appWindows=new(windowHost,observerFailure:error=>RuntimeDiagnostics.Write("window-observer-failed",error));
        var settings = LoadSettings();
        RuntimeDiagnostics.Write("startup",details:DataDirectory);
        model = new(settings: settings.Settings);
        catalogController = new(phone, model);
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","Brand","vircre.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        Root.ActualThemeChanged += (_, _) => UpdateCaptionColors();
        model.PropertyChanged += (_, _) => RenderState();
        AppGrid.ItemsSource = model.Apps;
        RunningList.ItemsSource = model.Running;
        ThemeChoice.SelectedIndex = model.Theme switch {"Light"=>1,"Dark"=>2,_=>0};
        VirtualCameraToggle.IsOn=model.VirtualCameraEnabled;
        CameraFrameRateBox.Value=model.CameraFrameRate;
        var assembly=typeof(MainWindow).Assembly;
        var appVersion=assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ??assembly.GetName().Version?.ToString(3)??"未知版本";
        AboutVersion.Text="virCre "+appVersion;
        Root.RequestedTheme = model.Theme switch {"Light"=>ElementTheme.Light,"Dark"=>ElementTheme.Dark,_=>ElementTheme.Default};
        model.SettingsChanged += SaveSettings;
        phone.Warning += message => DispatcherQueue.TryEnqueue(() => ShowAction(message));
        appWindows.Changed += AppWindows_Changed;
        appWindows.Problem += AppWindows_Problem;
        ready = true;
        UpdateCaptionColors();
        Navigation.SelectedItem = Navigation.MenuItems[0];
        AppWindow.Closing += AppWindow_Closing;
        Closed += (_, _) => {
            closing = true;
            lifetime.Cancel();
            catalogController.Dispose();
            appWindows.Changed -= AppWindows_Changed;
            appWindows.Problem -= AppWindows_Problem;
            lifetime.Dispose();
            if (observedRoot != null) observedRoot.Changed -= XamlRoot_Changed;
        };
        RenderState();
        if (settings.Warning != null) ShowAction(settings.Warning, true);
        else if (StorageLocation.Warning != null) ShowAction(StorageLocation.Warning);
    }
    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args) {
        if (allowClose) return;
        args.Cancel = true;
        if (closing) return;
        closing = true;
        Root.IsHitTestVisible = false;
        Navigation.IsEnabled = false;
        FooterStatus.Text = "正在关闭应用窗口并停止设备读取…";
        try {
            var result=await windowShutdown.TryStopAsync([
                new("lifetime",()=>{lifetime.Cancel();return Task.CompletedTask;}),
                new("catalog",catalogController.StopAsync),new("refresh",()=>refreshTask),
                new("settings",()=>settingsTask),new("shortcut-activation",()=>shortcutTask),
                new("native-drag",()=>nativeDragTask),new("shortcut-writes",()=>Task.WhenAll(shortcutWrites.ToArray()))],
                appWindows.StopAsync,()=>appWindows.Windows.Count>0,
                enabled=>{Root.IsHitTestVisible=enabled;Navigation.IsEnabled=enabled;});
            foreach(var failure in result.Failures)RuntimeDiagnostics.Write(failure.Error is TimeoutException?"shutdown-timeout":"shutdown-work-failed",failure.Error,failure.Name);
            if(!result.CanClose) {
                RestoreCleanupControls(result.Failures.Any(failure=>failure.Error is TimeoutException)
                    ?"收尾等待超过15秒，界面已恢复；后台任务仍在结束，请稍后重试关闭。剩余应用窗口可在运行中页面关闭。"
                    :"仍有应用窗口未关闭，请在运行中页面重试关闭窗口，再关闭主界面。");
                return;
            }
        }
        catch (Exception error) {
            // Keep ownership and allow another title-bar close attempt instead of abandoning a live process.
            RuntimeDiagnostics.Write("shutdown-failed",error);
            RestoreCleanupControls(error.Message);
            return;
        }
        allowClose = true;
        Close();
    }
    private void RestoreCleanupControls(string message) {
        closing=false;Root.IsHitTestVisible=true;Navigation.IsEnabled=true;
        model.UpdateWindows(appWindows.Windows);
        Navigation.SelectedItem=Navigation.MenuItems.OfType<NavigationViewItem>().First(item=>Equals(item.Tag,"running"));
        model.Navigate("running");
        RenderState();
        ShowAction("关闭尚未完成："+message,true);
    }

    private async void Root_Loaded(object sender, RoutedEventArgs e) {
        if (observedRoot != null) return;
        observedRoot = Root.XamlRoot;
        observedRoot.Changed += XamlRoot_Changed;
        var scale = Root.XamlRoot.RasterizationScale;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary)?.WorkArea;
        AppWindow.Resize(new SizeInt32(Math.Min((int)(1120 * scale),workArea?.Width??int.MaxValue), Math.Min((int)(760 * scale),workArea?.Height??int.MaxValue)));
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
            var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Primary)?.WorkArea;
            presenter.PreferredMinimumWidth = Math.Min((int)Math.Ceiling(900 * scale),area?.Width??int.MaxValue);
            presenter.PreferredMinimumHeight = Math.Min((int)Math.Ceiling(600 * scale),area?.Height??int.MaxValue);
        }
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args) {
        if (!ready) return;
        var page = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string;
        if(windowShutdown.HasStarted && page!="running")return;
        if (page != null) model.Navigate(page);
        SearchBox.Text = model.Query;
    }
    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) {
        if (ready && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) model.Search(sender.Text);
    }
    private void Favorite_Click(object sender, RoutedEventArgs e) {
        if(windowShutdown.HasStarted)return;
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
        if (!ready || windowShutdown.HasStarted) return;
        Root.RequestedTheme = ThemeChoice.SelectedIndex switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default };
        model.SetTheme(ThemeChoice.SelectedIndex switch {1=>"Light",2=>"Dark",_=>"System"});
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
        var cleanupOnly=windowShutdown.HasStarted;
        PageTitle.Text = model.Title;
        PageDescription.Text = model.Description;
        AppCount.Text = model.ResultCount;
        DeviceName.Text = model.DeviceLabel;
        DeviceHint.Text = cleanupOnly?"正在结束会话":scanning ? "正在检查设备…" : model.DeviceStatus;
        ConnectionStatus.Text = cleanupOnly?"等待退出":model.DeviceStatus;
        FooterStatus.Text = cleanupOnly?"数据读取已停止，可管理剩余应用窗口后重试关闭。":model.FooterText;
        LoadingRing.IsActive = cleanupOnly?windowShutdown.IsStopping:model.IsLoading || scanning;
        SearchBox.IsEnabled = !cleanupOnly && !model.IsLoading && model.HasDevice;
        DevicePicker.IsEnabled=!cleanupOnly && !scanning;
        RefreshDevices.IsEnabled=!cleanupOnly && !scanning;
        AppGrid.IsEnabled=!cleanupOnly && !scanning && !model.IsLoading && model.HasDevice;ThemeChoice.IsEnabled=!cleanupOnly;
        RenderCameraCompatibility(cleanupOnly);
        foreach(var item in Navigation.MenuItems.OfType<NavigationViewItem>())item.IsEnabled=!cleanupOnly || Equals(item.Tag,"running");
        if(Navigation.SettingsItem is NavigationViewItem settingsItem)settingsItem.IsEnabled=!cleanupOnly;
        EmptyTitle.Text = scanning ? "正在查找手机" : model.EmptyTitle;
        EmptyDescription.Text = scanning ? "正在检查已授权的 ADB 设备。" : model.EmptyDescription;
        if (SearchBox.Text != model.Query) SearchBox.Text = model.Query;
        EmptyIcon.Glyph = model.Page == "running" ? "\uE7F4" : model.Page == "favorites" ? "\uE734" : "\uE721";
        var settings = model.Page == "settings";
        var running = model.Page == "running";
        SettingsContent.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        SearchRow.Visibility = settings || running ? Visibility.Collapsed : Visibility.Visible;
        AppGrid.Visibility = settings || model.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (running) AppGrid.Visibility = Visibility.Collapsed;
        RunningList.Visibility = running && !model.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = !settings && model.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }
}
