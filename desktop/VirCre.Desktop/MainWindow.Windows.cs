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
    private async void OpenApp_Click(object sender, RoutedEventArgs args) {
        if ((sender as Button)?.Tag is AppCard card) await OpenCardAsync(card);
    }
    private AppLaunchService? appLaunches;
    private AppLaunchService AppLaunches => appLaunches??=new(appWindows);
    private bool CanActOnCard(AppCard card,PhoneDevice? device=null) =>
        AppActionGuard.CanActOn(card,device??devices.State.Selected,model,devices.State,closing || windowShutdown.HasStarted);
    private async Task OpenCardAsync(AppCard card) {
        if (closing || windowShutdown.HasStarted) return;
        var device=devices.State.Selected;
        if (device is null || !CanActOnCard(card,device)) {
            ShowAction("设备或应用列表已经变化，请刷新后重新选择。", true);return;
        }
        ShowAction($"正在打开 {card.Name}…");
        try {
            var result = await AppLaunches.OpenAsync(device,card.PackageName,card.Name,model.VirtualCameraEnabled,model.CameraFrameRate,lifetime.Token);
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
        var content=new TextBlock {Text=state.Message+"\n\n画面来自此投屏窗口客户区盖住的桌面，不包含投屏窗口自身。跨屏时自动拼接各屏幕画面，超出屏幕的部分补黑。最小化或锁屏期间暂停发送，还原或解锁后自动恢复；手动暂停需手动继续。关闭应用窗口后停止采集。",TextWrapping=TextWrapping.Wrap};
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
