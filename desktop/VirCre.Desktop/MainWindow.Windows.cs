using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VirCre.Launcher;
using VirCre.Presentation;
using VirCre.Camera;

namespace VirCre.Desktop;

public sealed partial class MainWindow {
    private readonly DesktopWindowHost windowHost;
    private readonly AppWindowManager appWindows;

    private void AppWindows_Changed(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() => {
        if (!closing) model.UpdateWindows(appWindows.Windows);
        CloseIdleShortcutSession();
    });
    private void AppWindows_Problem(string message) => DispatcherQueue.TryEnqueue(() => ShowAction(message, true));
    private void ShowAction(string message, bool error = false) {
        if (closing) return;
        // A shortcut-only session has no visible launcher to display failures.
        if(error && shortcutOnly)ShowLauncher();
        ActionNotice.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        ActionNotice.Title = message;
        ActionNotice.ActionButton = null;
        ActionNotice.IsOpen = true;
    }
    private async void OpenApp_Click(object sender, RoutedEventArgs args) {
        if ((sender as Button)?.Tag is AppCard card) await OpenCardAsync(card);
    }
    private async void AppCard_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args) {
        // Buttons own their clicks, including favorites. A double click there must not launch the card.
        for (var current = args.OriginalSource as DependencyObject; current != null && !ReferenceEquals(current, sender); current = VisualTreeHelper.GetParent(current))
            if (current is Button) return;
        args.Handled = true;
        if ((sender as FrameworkElement)?.Tag is AppCard card) await OpenCardAsync(card);
    }
    private AppLaunchRequest CreateLaunchRequest(string serial,string deviceName,string packageName,string name) {
        // A changed default affects new windows; clicking an existing app still focuses its current mode.
        return appWindows.Windows.FirstOrDefault(value=>value.Request.Serial==serial && value.PackageName==packageName)?.Request
            ?? new(serial,deviceName,packageName,name,model.VirtualCameraEnabled,model.CameraFrameRate);
    }
    private async Task OpenCardAsync(AppCard card) {
        if (closing || windowShutdown.HasStarted) return;
        if (scanning || model.IsLoading || !model.HasDevice || DevicePicker.SelectedItem is not PhoneDevice { State: DeviceState.Online } device ||
            card.SourceSerial != device.Serial || model.CurrentSerial != device.Serial || !model.Apps.Any(app => app.PackageName == card.PackageName)) {
            ShowAction("设备或应用列表已经变化，请刷新后重新选择。", true);return;
        }
        ShowAction($"正在打开 {card.Name}…");
        try {
            var result = await appWindows.OpenAsync(CreateLaunchRequest(device.Serial, device.Model, card.PackageName, card.Name), lifetime.Token);
            ShowAction(result.Window.IsVirtualCamera ? $"{card.Name} 已以虚拟摄像头模式打开。采集窗口盖住的桌面；设置中可调整帧率或关闭默认相机。" :
                result.Activated ? result.Reused ? $"已切回 {card.Name}。" : $"{card.Name} 的独立窗口已打开。" : $"{card.Name} 窗口已存在，请从任务栏切回。");
        } catch (OperationCanceledException) { if (!closing) ShowAction("应用启动已取消。"); }
        catch (Exception error) { ShowAction("打开失败：" + error.Message, true); }
    }
    private void FocusWindow_Click(object sender, RoutedEventArgs args) {
        if ((sender as Button)?.Tag is Guid id && !appWindows.Focus(id)) ShowAction("暂时无法激活窗口，请从任务栏切回。", true);
    }
    private async void CloseWindow_Click(object sender, RoutedEventArgs args) {
        if ((sender as Button)?.Tag is not Guid id) return;
        try { await appWindows.CloseAsync(id); }
        catch (Exception error) { ShowAction("关闭失败：" + error.Message, true); }
    }
    private async void CameraControl_Click(object sender, RoutedEventArgs args) {
        if ((sender as Button)?.Tag is not Guid id) return;
        var entry=appWindows.Windows.FirstOrDefault(value=>value.Id==id);
        if(entry is not {IsVirtualCamera:true})return;
        var state=windowHost.GetStatus(entry.Request);
        var content=new TextBlock {Text=state.Message+"\n\n画面来自此投屏窗口客户区盖住的桌面，不包含投屏窗口自身。请将窗口完整放在一个显示器内。关闭应用窗口后停止采集。",TextWrapping=TextWrapping.Wrap};
        var dialog=new ContentDialog {
            XamlRoot=Root.XamlRoot,Title="虚拟摄像头 · "+entry.Name,Content=content,
            PrimaryButtonText=state.Paused?"继续采集":"暂停采集",CloseButtonText="返回"
        };
        try {
            if(await dialog.ShowAsync()==ContentDialogResult.Primary) {
                windowHost.SetPaused(entry.Request,!state.Paused);
                ShowAction(state.Paused?"正在恢复窗口下方画面采集。":"已暂停虚拟摄像头画面。");
            }
        } catch(Exception error) {ShowAction(error.Message,true);}
    }
}
