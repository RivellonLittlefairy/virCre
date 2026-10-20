using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VirCre.Launcher;
using VirCre.Presentation;

namespace VirCre.Desktop;

public sealed partial class MainWindow {
    private readonly CachedPhoneCatalog phone = CreatePhoneCatalog();
    private static CachedPhoneCatalog CreatePhoneCatalog() {
        var runner = new CommandRunner();
        var cache = new AppCatalogCache(Path.Combine(DataDirectory, "app-catalog"));
        var live = new AgentPhoneCatalog(new PhoneCatalog(runner), runner, Path.Combine(AppContext.BaseDirectory, "Assets", "icon-agent.jar"), cache.IconDirectory);
        return new(live, cache);
    }
    private readonly CatalogController catalogController;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? deviceScan;
    private int scanGeneration;
    private bool syncingDevices, scanning, closing;
    private Task refreshTask = Task.CompletedTask;

    private async void RefreshDevices_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync(forceRefresh: true);
    private async void DevicePicker_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (!ready || syncingDevices || closing || windowShutdown.HasStarted) return;
        await catalogController.SelectAsync(DevicePicker.SelectedItem as PhoneDevice, lifetime.Token);
    }
    private Task RefreshDevicesAsync(bool forceRefresh = false) {
        if(windowShutdown.HasStarted)return Task.CompletedTask;
        refreshTask = RefreshDevicesCoreAsync(forceRefresh);
        return refreshTask;
    }
    private async Task RefreshDevicesCoreAsync(bool forceRefresh) {
        if (closing || windowShutdown.HasStarted) return;
        var generation = ++scanGeneration;
        deviceScan?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deviceScan = request;
        var previous = (DevicePicker.SelectedItem as PhoneDevice)?.Serial;
        scanning = true;
        DevicePicker.IsEnabled = false;
        RefreshDevices.IsEnabled = false;
        try {
            catalogController.CancelActive();
            RenderState();
            var devices = await phone.GetDevicesAsync(request.Token);
            if (closing || windowShutdown.HasStarted || generation != scanGeneration || request.IsCancellationRequested) return;
            var selected = devices.FirstOrDefault(device => device.Serial == previous);
            if (selected == null && devices.Count == 1) selected = devices[0];
            syncingDevices = true;
            try { DevicePicker.ItemsSource = devices; DevicePicker.SelectedItem = selected; }
            finally { syncingDevices = false; }
            scanning = false;
            DevicePicker.IsEnabled = true;
            RefreshDevices.IsEnabled = true;
            if (selected?.State != DeviceState.Online && previous != null) {
                var message = selected?.State == DeviceState.Unauthorized ? "请在手机上允许调试授权后刷新。" : "手机连接未确认，请检查连接后刷新。";
                model.ReportDeviceError(message + "仍显示上次列表，暂不能启动应用。", preserveCatalog: true);
            } else await catalogController.SelectAsync(selected, request.Token, forceRefresh);
        } catch (OperationCanceledException) { }
        catch (Exception error) when (CatalogErrors.IsExpected(error)) {
            if (!closing && !windowShutdown.HasStarted && generation == scanGeneration) {
                syncingDevices = true;
                try { DevicePicker.SelectedItem = null; DevicePicker.ItemsSource = null; }
                finally { syncingDevices = false; }
                model.ReportDeviceError("设备读取失败，仍显示上次列表，暂不能启动应用。" + error.Message, preserveCatalog: true);
            }
        } finally {
            if (ReferenceEquals(deviceScan, request)) deviceScan = null;
            if (!closing && !windowShutdown.HasStarted && generation == scanGeneration) {
                scanning = false;
                DevicePicker.IsEnabled = true;
                RefreshDevices.IsEnabled = true;
                RenderState();
            }
        }
    }
    internal bool TryRecoverCatalogFormatError(Exception error) {
        if(!CatalogErrors.IsFormatFailure(error) || !ready || closing || windowShutdown.HasStarted || !(scanning || model.IsLoading))return false;
        ++scanGeneration;deviceScan?.Cancel();scanning=false;
        var reset=catalogController.SelectAsync(null,lifetime.Token);
        if(!reset.IsCompletedSuccessfully)return false;
        syncingDevices=true;
        try {DevicePicker.SelectedItem=null;DevicePicker.ItemsSource=null;}
        finally {syncingDevices=false;}
        refreshTask=Task.CompletedTask;
        model.ReportDeviceError(error.Message);
        ShowAction("设备数据格式异常，请刷新设备后重试。",true);
        RenderState();return true;
    }
}
