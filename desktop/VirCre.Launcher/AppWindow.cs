namespace VirCre.Launcher;

public sealed record AppLaunchRequest(string Serial, string DeviceName, string PackageName, string Name,
    bool UseVirtualCamera = false, int CameraFrameRate = CameraFrameRates.Default) {
    public string HardwareId {get;init;} = "";
    public string DeviceIdentity => HardwareId.Length>0?"hardware:"+HardwareId:"transport:"+Serial;
    public bool IsForDevice(PhoneDevice device) => HardwareId.Length>0 && device.HardwareId.Length>0
        ? HardwareId==device.HardwareId : device.HasConnection(Serial);
}
public enum AppWindowState { Starting, Running, Closing, CloseFailed }
public sealed record AppWindowInfo(Guid Id, AppLaunchRequest Request, AppWindowState State, int? DisplayId) {
    public string Name => Request.Name;
    public string PackageName => Request.PackageName;
    public string DeviceName => Request.DeviceName;
    public bool IsVirtualCamera => Request.UseVirtualCamera;
    public string ModeLabel => IsVirtualCamera ? "虚拟摄像头" : "独立窗口";
    public string StateLabel => State switch {
        AppWindowState.Starting => "正在启动", AppWindowState.Closing => "正在关闭", AppWindowState.CloseFailed => "关闭失败", _ => "运行中"
    };
    public bool CanFocus => State is AppWindowState.Running or AppWindowState.CloseFailed;
    public bool CanClose => State != AppWindowState.Closing;
}
public sealed record AppOpenResult(AppWindowInfo Window, bool Reused, bool Activated);

public interface IAppWindowHost { IAppWindowProcess Start(AppLaunchRequest request); }
public interface IAppWindowProcess : IDisposable {
    IntPtr NativeHandle => IntPtr.Zero;
    bool HasExited {get;}
    int? DisplayId {get;}
    int ExitCode {get;}
    Task WaitForReadyAsync(CancellationToken token);
    Task WaitForExitAsync();
    bool Focus();
    Task CloseAsync();
}
