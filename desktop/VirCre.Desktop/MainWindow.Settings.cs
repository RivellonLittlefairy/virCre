using VirCre.Launcher;
using VirCre.Presentation;
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
    private SettingsSaveQueue? saveQueue;
    private SettingsSaveQueue SaveQueue => saveQueue??=new(settingsStore.Save);
    private bool syncingCameraSettings;
    private void VirtualCamera_Toggled(object sender,RoutedEventArgs args) {
        if(!ready || syncingCameraSettings || windowShutdown.HasStarted)return;
        model.SetVirtualCameraEnabled(VirtualCameraToggle.IsOn);
    }
    private void RenderCameraCompatibility(bool cleanupOnly) {
        // A camera on another connected phone may still be running, so its FPS stays adjustable.
        var state=CameraCompatibilityState.Create(devices.State.Selected,model.VirtualCameraEnabled,
            appWindows.Windows.Any(window=>window.IsVirtualCamera),cleanupOnly);
        syncingCameraSettings=true;
        try {VirtualCameraToggle.IsOn=state.ToggleChecked;}
        finally {syncingCameraSettings=false;}
        VirtualCameraToggle.IsEnabled=state.ToggleEnabled;
        CameraFrameRateBox.IsEnabled=state.FrameRateEnabled;
        VirtualCameraCompatibility.Text=state.Description;
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
        _=ObserveSaveAsync(SaveQueue.EnqueueAsync(snapshot));
    }
    private async Task ObserveSaveAsync(Task save) {
        try {await save;}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) {ShowAction("设置保存失败，本次更改暂留内存："+error.Message,true);}
    }
}
