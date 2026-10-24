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
    private readonly DeviceController devices;
    private readonly CancellationTokenSource lifetime = new();
    private bool syncingDevices, closing;

    /// <summary>Mirrors the controller snapshot into the picker; the controller stays the only source.</summary>
    private void Devices_Changed() {
        if (closing) return;
        var state = devices.State;
        model.UpdateDeviceSelection(state);
        syncingDevices = true;
        try {
            if (!ReferenceEquals(DevicePicker.ItemsSource, state.Devices)) DevicePicker.ItemsSource = state.Devices;
            if (!Equals(DevicePicker.SelectedItem, state.Selected)) DevicePicker.SelectedItem = state.Selected;
        } finally { syncingDevices = false; }
        RenderState();
    }
    private async void RefreshDevices_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync(forceRefresh: true);
    private async void DevicePicker_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (!ready || syncingDevices || closing || windowShutdown.HasStarted) return;
        await devices.SelectAsync(DevicePicker.SelectedItem as PhoneDevice, lifetime.Token);
    }
    private Task RefreshDevicesAsync(bool forceRefresh = false) =>
        windowShutdown.HasStarted ? Task.CompletedTask : devices.RefreshAsync(forceRefresh, lifetime.Token);
    internal bool TryRecoverCatalogFormatError(Exception error) {
        if(!ready || closing || windowShutdown.HasStarted || !devices.TryRecoverFormatError(error))return false;
        ShowAction("设备数据格式异常，请刷新设备后重试。",true);
        RenderState();
        return true;
    }
}
