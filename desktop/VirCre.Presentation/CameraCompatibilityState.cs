using VirCre.Launcher;

namespace VirCre.Presentation;

/// <summary>
/// Camera settings depend on the selected phone, not the other way round: an
/// unsupported phone disables the controls without rewriting the saved default.
/// </summary>
public sealed record CameraCompatibilityState(bool ToggleChecked, bool ToggleEnabled, bool FrameRateEnabled, string Description) {
    public static CameraCompatibilityState Create(PhoneDevice? device, bool preferredEnabled, bool hasCameraWindow, bool stopping) {
        var online=device is {State:DeviceState.Online};
        var unavailable=online && !device!.SupportsVirtualCamera;
        return new(
            ToggleChecked:preferredEnabled && !unavailable,
            ToggleEnabled:!stopping && !unavailable,
            FrameRateEnabled:!stopping && (!unavailable || hasCameraWindow),
            Description:!online ? "普通应用窗口需要 Android 10 或以上；虚拟摄像头需要 Android 16 或以上。" :
                device!.ApiLevel is null ? "未能确认当前手机的 Android 版本，请刷新设备后重试。" :
                device.ApiLevel<AndroidCompatibility.MinimumAppApi ? $"当前手机为 {device.AndroidVersionLabel}，不支持独立应用窗口；需要 Android 10 或以上。" :
                unavailable ? $"当前手机为 {device.AndroidVersionLabel}，已自动禁用虚拟摄像头；应用列表、普通窗口和快捷方式仍可使用。" :
                $"当前手机为 {device.AndroidVersionLabel}，支持虚拟摄像头。此开关保留为支持设备的默认设置。");
    }
}
