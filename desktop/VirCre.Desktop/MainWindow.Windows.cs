using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VirCre.Launcher;
using VirCre.Presentation;

namespace VirCre.Desktop;

public sealed partial class MainWindow {
    private readonly AppWindowManager appWindows = new(new ScrcpyWindowHost());

    private void AppWindows_Changed(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() => {
        if (!closing) model.UpdateWindows(appWindows.Windows);
    });
    private void AppWindows_Problem(string message) => DispatcherQueue.TryEnqueue(() => ShowAction(message, true));
    private void ShowAction(string message, bool error = false) {
        if (closing) return;
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
    private async Task OpenCardAsync(AppCard card) {
        if (closing || windowShutdown.HasStarted) return;
        if (scanning || model.IsLoading || !model.HasDevice || DevicePicker.SelectedItem is not PhoneDevice { State: DeviceState.Online } device ||
            card.SourceSerial != device.Serial || model.CurrentSerial != device.Serial || !model.Apps.Any(app => app.PackageName == card.PackageName)) {
            ShowAction("设备或应用列表已经变化，请刷新后重新选择。", true);return;
        }
        ShowAction($"正在打开 {card.Name}…");
        try {
            var result = await appWindows.OpenAsync(new(device.Serial, device.Model, card.PackageName, card.Name), lifetime.Token);
            ShowAction(result.Activated ? result.Reused ? $"已切回 {card.Name}。" : $"{card.Name} 的独立窗口已打开。" : $"{card.Name} 窗口已存在，请从任务栏切回。");
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
}
