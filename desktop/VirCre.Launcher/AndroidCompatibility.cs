using System.Globalization;

namespace VirCre.Launcher;

/// <summary>Keep ordinary virtual displays independent of Android 16 camera APIs.</summary>
public static class AndroidCompatibility {
    public const int MinimumAppApi = 29;
    public const int MinimumCameraApi = 36;

    public static async Task<int> ReadApiLevelAsync(ICommandRunner runner, string serial, CancellationToken token) {
        _ = new ShortcutRequest(serial, "", "device", "com.android.shell", "app").Encode();
        var result = await runner.RunAsync("adb", ["-s", serial, "shell", "getprop", "ro.build.version.sdk"],
            TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
        if (result.ExitCode != 0 || !int.TryParse(result.Output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var api) || api <= 0)
            throw new IOException("无法确认手机的 Android 版本，请检查调试连接后刷新设备。");
        return api;
    }

    public static AppLaunchRequest CreateLaunchRequest(PhoneDevice device, string packageName, string name,
        bool virtualCameraEnabled, int cameraFrameRate) {
        if (device.ApiLevel is not { } api)
            throw new IOException("尚未确认手机的 Android 版本，请刷新设备后重试。");
        if (api < MinimumAppApi)
            throw new IOException("独立应用窗口需要 Android 10 或以上版本。");
        return new(device.Serial, device.Model, packageName, name,
            virtualCameraEnabled && device.SupportsVirtualCamera, cameraFrameRate) {HardwareId=device.HardwareId};
    }
}
