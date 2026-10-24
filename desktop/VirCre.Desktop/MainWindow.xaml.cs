using Microsoft.UI.Windowing;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VirCre.Launcher;
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
    private readonly ShutdownCoordinator windowShutdown;

    /// <summary>Read-only surface for compiled bindings; the window never replaces the model.</summary>
    public ShellViewModel Model => model;
    public MainWindow(bool shortcutOnly=false) {
        this.shortcutOnly=shortcutOnly;
        windowHost=new(stage=>RuntimeDiagnostics.Write(stage.StartsWith("touch-",StringComparison.Ordinal)?"touch-stage":stage.StartsWith("power-",StringComparison.Ordinal)?"power-stage":"input-rejected",details:stage),message=>{
            RuntimeDiagnostics.Write("virtual-camera",details:message);
            DispatcherQueue.TryEnqueue(()=>ShowAction(message));
        },stage=>RuntimeDiagnostics.Write("camera-stage",details:stage));
        appWindows=new(windowHost,observerFailure:error=>RuntimeDiagnostics.Write("window-observer-failed",error));
        var settings = LoadSettings();
        RuntimeDiagnostics.Write("startup",details:DataDirectory);
        model = new(settings: settings.Settings, dispatcher: new WindowDispatcher(DispatcherQueue));
        windowShutdown = new(text: model.Text);
        catalogController = new(phone, model);
        devices = new(phone, catalogController, model);
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","Brand","vircre.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        Root.ActualThemeChanged += (_, _) => {UpdateCaptionColors();RenderState();};
        model.PropertyChanged += (_, _) => RenderState();
        AppGrid.ItemsSource = model.Apps;
        RunningList.ItemsSource = model.RunningCards;
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
        devices.Changed += Devices_Changed;
        appWindows.Changed += AppWindows_Changed;
        appWindows.Problem += AppWindows_Problem;
        ready = true;
        UpdateCaptionColors();
        Navigation.SelectedItem = Navigation.MenuItems[0];
        AppWindow.Closing += AppWindow_Closing;
        Closed += (_, _) => {
            closing = true;
            statusMessageTimer?.Stop();
            lifetime.Cancel();
            devices.Changed -= Devices_Changed;
            devices.Dispose();
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
        ClearStatusMessage();
        closing = true;
        model.SetShutdown(started:true,stopping:true);
        Root.IsHitTestVisible = false;
        Navigation.IsEnabled = false;
        FooterStatus.Text = "正在关闭应用窗口并停止设备读取…";
        try {
            var result=await windowShutdown.TryStopAsync([
                new("lifetime",()=>{lifetime.Cancel();return Task.CompletedTask;}),
                new("devices",devices.StopAsync),
                new("settings",()=>SaveQueue.StopAsync()),new("shortcut-activation",()=>shortcutTask),
                new("native-drag",()=>nativeDragTask),new("shortcut-writes",()=>Task.WhenAll(shortcutWrites.ToArray()))],
                appWindows.StopAsync,()=>appWindows.Windows.Count>0,
                enabled=>{Root.IsHitTestVisible=enabled;Navigation.IsEnabled=enabled;});
            foreach(var failure in result.Failures)RuntimeDiagnostics.Write(failure.Error is TimeoutException?"shutdown-timeout":"shutdown-work-failed",failure.Error,failure.Name);
            model.SetShutdown(windowShutdown.HasStarted,windowShutdown.IsStopping);
            if(!result.CanClose) {
                RestoreCleanupControls(result.Failures.Any(failure=>failure.Error is TimeoutException)
                    ?$"收尾等待超过{windowShutdown.WaitLimit.TotalSeconds:0.###}秒，界面已恢复；后台任务仍在结束，请稍后重试关闭。剩余应用窗口可在运行中页面关闭。"
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
        model.SetShutdown(windowShutdown.HasStarted,windowShutdown.IsStopping);
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
        AppWindow.Resize(new SizeInt32(Math.Min((int)(960 * scale),workArea?.Width??int.MaxValue), Math.Min((int)(600 * scale),workArea?.Height??int.MaxValue)));
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
            presenter.PreferredMinimumWidth = Math.Min((int)Math.Ceiling(720 * scale),area?.Width??int.MaxValue);
            presenter.PreferredMinimumHeight = Math.Min((int)Math.Ceiling(520 * scale),area?.Height??int.MaxValue);
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
        if ((sender as FrameworkElement)?.Tag is string packageName) model.ToggleFavorite(packageName);
    }
    private void AppGrid_SizeChanged(object sender, SizeChangedEventArgs e) {
        if (AppGrid.ItemsPanelRoot is ItemsWrapGrid panel && e.NewSize.Width > 0) {
            // Reserve the scroll gutter and each container's 5 px margins on both sides.
            var available = Math.Max(1, e.NewSize.Width - 16);
            var columns = Math.Max(1, (int)(available / 220));
            panel.ItemWidth = Math.Max(1, Math.Floor(available / columns));
        }
    }
    private void ContentArea_SizeChanged(object sender, SizeChangedEventArgs args) {
        var compact=Root.ActualWidth<820;
        ContentArea.Padding=compact?new Thickness(18,22,18,0):new Thickness(28,24,28,0);
        RefreshLabel.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        SearchBox.Width=Math.Max(100,Math.Min(345,args.NewSize.Width-ContentArea.Padding.Left-ContentArea.Padding.Right-140));
    }
    private async void EmptyAction_Click(object sender,RoutedEventArgs args) {
        if(windowShutdown.HasStarted)return;
        if(model.Page=="running") {
            Navigation.SelectedItem=Navigation.MenuItems[0];
            model.Navigate("apps");
        }
        else if(!model.HasDevice || model.ErrorMessage.Length>0 || model.Page=="apps" && model.Query.Length==0)
            await RefreshDevicesAsync(forceRefresh:true);
        else if(model.Query.Length>0)model.Search("");
        else {
            Navigation.SelectedItem=Navigation.MenuItems[0];
            model.Navigate("apps");
        }
    }
    private Brush StatusBrush(bool connected) {
        var contrast=new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        var theme=contrast?"HighContrast":Root.ActualTheme==ElementTheme.Dark?"Default":"Light";
        var resources=(ResourceDictionary)Application.Current.Resources.MergedDictionaries[1].ThemeDictionaries[theme];
        return (Brush)resources[connected?"ShellGreen":"ShellMuted"];
    }
    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (!ready || windowShutdown.HasStarted) return;
        Root.RequestedTheme = ThemeChoice.SelectedIndex switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default };
        model.SetTheme(ThemeChoice.SelectedIndex switch {1=>"Light",2=>"Dark",_=>"System"});
    }
    /// <summary>Compiled-bind helper: x:Bind cannot resolve a converter from a Window root.</summary>
    private Visibility ViewVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
    private void UpdateCaptionColors() {
        var dark = Root.ActualTheme == ElementTheme.Dark;
        AppWindow.TitleBar.ButtonForegroundColor = dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0);
        AppWindow.TitleBar.ButtonInactiveForegroundColor = dark ? Color.FromArgb(255, 160, 160, 160) : Color.FromArgb(255, 100, 100, 100);
        AppWindow.TitleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
    }
    private void RenderState() {
        if (!ready || closing) return;
        RenderConnection();
        RenderNavigation();
        RenderCameraCompatibility(windowShutdown.HasStarted);
        if (SearchBox.Text != model.Query) SearchBox.Text = model.Query;
    }
    /// <summary>Theme and high-contrast brushes belong to the view; the model only reports state.</summary>
    private void RenderConnection() {
        var statusBrush=StatusBrush(model.IsConnectedStatus);
        ConnectionDot.Fill=FooterDot.Fill=statusBrush;
        ConnectionStatus.Foreground=statusBrush;
        RenderFooter();
    }
    /// <summary>Navigation items carry no compiled binding; cleanup only narrows them.</summary>
    private void RenderNavigation() {
        var cleanupOnly=windowShutdown.HasStarted;
        foreach(var item in Navigation.MenuItems.OfType<NavigationViewItem>())item.IsEnabled=!cleanupOnly || Equals(item.Tag,"running");
        if(Navigation.SettingsItem is NavigationViewItem settingsItem)settingsItem.IsEnabled=!cleanupOnly;
    }
}
