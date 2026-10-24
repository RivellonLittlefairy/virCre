namespace VirCre.Launcher;

/// <summary>
/// Runs a shortcut payload without the launcher window: decode, resolve the phone by
/// hardware identity, verify the entry point, then reuse the shared launch service.
/// </summary>
public sealed class ShortcutLauncher(IPhoneCatalog phones,ICommandRunner runner,AppLaunchService launches,
    Func<string,string,CancellationToken,Task<bool>> canLaunch) {
    public async Task<AppOpenResult> OpenAsync(string payload,UserSettings settings,CancellationToken token) {
        var request=ShortcutRequest.Decode(payload);
        var devices=await phones.GetDevicesAsync(token).ConfigureAwait(false);
        var device=await request.ResolveAsync(devices,(serial,requestToken)=>ShortcutRequest.GetHardwareIdAsync(runner,serial,requestToken),token).ConfigureAwait(false);
        if(!await canLaunch(device.Serial,request.PackageName,token).ConfigureAwait(false))
            throw new IOException("手机中已找不到此应用的启动入口，请刷新列表并重新创建快捷方式。");
        return await launches.OpenAsync(device,request.PackageName,request.Name,settings.VirtualCameraEnabled,settings.CameraFrameRate,token).ConfigureAwait(false);
    }
}
