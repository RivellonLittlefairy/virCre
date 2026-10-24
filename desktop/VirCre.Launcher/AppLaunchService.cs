namespace VirCre.Launcher;

/// <summary>
/// Resolves the launch mode once for every entry point. An existing window for the
/// same phone and package wins over the current default, which only affects new windows.
/// </summary>
public sealed class AppLaunchService(AppWindowManager windows) {
    public Task<AppOpenResult> OpenAsync(PhoneDevice device,string packageName,string name,
        bool virtualCameraEnabled,int frameRate,CancellationToken token) {
        var request=windows.Windows.FirstOrDefault(window=>window.Request.IsForDevice(device) && window.PackageName==packageName)?.Request
            ?? AndroidCompatibility.CreateLaunchRequest(device,packageName,name,virtualCameraEnabled,frameRate);
        return windows.OpenAsync(request,token);
    }
}
