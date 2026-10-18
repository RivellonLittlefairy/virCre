using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VirCre.Launcher;
using VirCre.Presentation;

namespace VirCre.Desktop;

public sealed partial class MainWindow {
    private readonly IPhoneCatalog phone = new PhoneCatalog(new CommandRunner());
    private readonly CatalogController catalogController;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? deviceScan;
    private int scanGeneration;
    private bool syncingDevices, scanning, closing;
    private Task refreshTask = Task.CompletedTask;

    private async void RefreshDevices_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();
    private async void DevicePicker_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (!ready || syncingDevices || closing) return;
        await catalogController.SelectAsync(DevicePicker.SelectedItem as PhoneDevice, lifetime.Token);
    }
    private Task RefreshDevicesAsync() {
        refreshTask = RefreshDevicesCoreAsync();
        return refreshTask;
    }
    private async Task RefreshDevicesCoreAsync() {
        if (closing) return;
        var generation = ++scanGeneration;
        deviceScan?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deviceScan = request;
        var previous = (DevicePicker.SelectedItem as PhoneDevice)?.Serial;
        scanning = true;
        DevicePicker.IsEnabled = false;
        RefreshDevices.IsEnabled = false;
        await catalogController.SelectAsync(null, request.Token);
        try {
            var devices = await phone.GetDevicesAsync(request.Token);
            if (closing || generation != scanGeneration || request.IsCancellationRequested) return;
            var selected = devices.FirstOrDefault(device => device.Serial == previous);
            if (selected == null && devices.Count == 1) selected = devices[0];
            syncingDevices = true;
            try { DevicePicker.ItemsSource = devices; DevicePicker.SelectedItem = selected; }
            finally { syncingDevices = false; }
            scanning = false;
            DevicePicker.IsEnabled = true;
            RefreshDevices.IsEnabled = true;
            await catalogController.SelectAsync(selected, request.Token);
        } catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or TimeoutException or ArgumentException) {
            if (!closing && generation == scanGeneration) {
                syncingDevices = true;
                try { DevicePicker.SelectedItem = null; DevicePicker.ItemsSource = null; }
                finally { syncingDevices = false; }
                model.ReportDeviceError(error.Message);
            }
        } finally {
            if (ReferenceEquals(deviceScan, request)) deviceScan = null;
            if (!closing && generation == scanGeneration) {
                scanning = false;
                DevicePicker.IsEnabled = true;
                RefreshDevices.IsEnabled = true;
                RenderState();
            }
        }
    }
}
