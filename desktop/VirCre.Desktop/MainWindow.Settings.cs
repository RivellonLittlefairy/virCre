using VirCre.Launcher;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VirCre.Desktop;

public sealed partial class MainWindow {
    private static readonly string PreferredDataDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"virCre");
    internal static readonly DataLocation StorageLocation=DataLocation.Resolve(PreferredDataDirectory,Path.Combine(AppContext.BaseDirectory,"virCre-data"));
    internal static readonly string DataDirectory=StorageLocation.Path;
    // Explorer consumes these files. Keep them outside legacy portable folders that may inherit Low integrity.
    internal static readonly string ShortcutDataDirectory=Path.Combine(PreferredDataDirectory,"shell");
    private readonly SettingsStore settingsStore=new(Path.Combine(DataDirectory,"settings.json"));
    private Task settingsTask=Task.CompletedTask;
    private bool syncingCameraSettings;
    private void VirtualCamera_Toggled(object sender,RoutedEventArgs args) {
        if(!ready || syncingCameraSettings || windowShutdown.HasStarted)return;
        model.SetVirtualCameraEnabled(VirtualCameraToggle.IsOn);
    }
    private void RenderCameraCompatibility(bool cleanupOnly) {
        var device=DevicePicker.SelectedItem as PhoneDevice;
        var online=device?.State==DeviceState.Online;
        var unavailable=online && !device!.SupportsVirtualCamera;
        syncingCameraSettings=true;
        try {VirtualCameraToggle.IsOn=model.VirtualCameraEnabled && !unavailable;}
        finally {syncingCameraSettings=false;}
        VirtualCameraToggle.IsEnabled=!cleanupOnly && !unavailable;
        // A camera on another connected phone may still be running, so keep its FPS adjustable.
        CameraFrameRateBox.IsEnabled=!cleanupOnly && (!unavailable || appWindows.Windows.Any(window=>window.IsVirtualCamera));
        VirtualCameraCompatibility.Text=!online ? "普通应用窗口需要 Android 10 或以上；虚拟摄像头需要 Android 16 或以上。" :
            device!.ApiLevel is null ? "未能确认当前手机的 Android 版本，请刷新设备后重试。" :
            device.ApiLevel<AndroidCompatibility.MinimumAppApi ? $"当前手机为 {device.AndroidVersionLabel}，不支持独立应用窗口；需要 Android 10 或以上。" :
            unavailable ? $"当前手机为 {device.AndroidVersionLabel}，已自动禁用虚拟摄像头；应用列表、普通窗口和快捷方式仍可使用。" :
            $"当前手机为 {device.AndroidVersionLabel}，支持虚拟摄像头。此开关保留为支持设备的默认设置。";
    }
    private void CameraFrameRate_ValueChanged(NumberBox sender,NumberBoxValueChangedEventArgs args) {
        if(!ready || windowShutdown.HasStarted || !double.IsFinite(args.NewValue))return;
        int frameRate=(int)Math.Clamp(Math.Round(args.NewValue,MidpointRounding.AwayFromZero),CameraFrameRates.Minimum,CameraFrameRates.Maximum);
        if(sender.Value!=frameRate)sender.Value=frameRate;
        windowHost.SetTargetFrameRate(frameRate);
        model.SetCameraFrameRate(frameRate);
    }
    private void CameraFrameRate_LostFocus(object sender,RoutedEventArgs args) {
        if(!ready || windowShutdown.HasStarted)return;
        if(!double.IsFinite(CameraFrameRateBox.Value))CameraFrameRateBox.Value=model.CameraFrameRate;
    }
    private SettingsLoad LoadSettings() {
        if(StorageLocation.UsedFallback && !File.Exists(Path.Combine(DataDirectory,"settings.json")))
            return new SettingsStore(Path.Combine(PreferredDataDirectory,"settings.json")).Load();
        return settingsStore.Load();
    }
    private void SaveSettings() {
        if(windowShutdown.HasStarted)return;
        var snapshot=model.ExportSettings();
        settingsTask=SaveAfterAsync(settingsTask,snapshot);
    }
    private async Task SaveAfterAsync(Task previous, UserSettings settings) {
        await previous;
        try {await Task.Run(()=>settingsStore.Save(settings));}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) {ShowAction("设置保存失败，本次更改暂留内存："+error.Message,true);}
    }
}
