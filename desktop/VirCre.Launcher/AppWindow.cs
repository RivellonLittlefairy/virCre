namespace VirCre.Launcher;

public sealed record AppLaunchRequest(string Serial, string DeviceName, string PackageName, string Name);
public enum AppWindowState { Starting, Running, Closing, CloseFailed }
public sealed record AppWindowInfo(Guid Id, AppLaunchRequest Request, AppWindowState State, int? DisplayId) {
    public string Name => Request.Name;
    public string PackageName => Request.PackageName;
    public string DeviceName => Request.DeviceName;
    public string StateLabel => State switch {
        AppWindowState.Starting => "正在启动", AppWindowState.Closing => "正在关闭", AppWindowState.CloseFailed => "关闭失败", _ => "运行中"
    };
    public bool CanFocus => State is AppWindowState.Running or AppWindowState.CloseFailed;
    public bool CanClose => State != AppWindowState.Closing;
}
public sealed record AppOpenResult(AppWindowInfo Window, bool Reused, bool Activated);

public interface IAppWindowHost { IAppWindowProcess Start(AppLaunchRequest request); }
public interface IAppWindowProcess : IDisposable {
    bool HasExited {get;}
    int? DisplayId {get;}
    int ExitCode {get;}
    Task WaitForReadyAsync(CancellationToken token);
    Task WaitForExitAsync();
    bool Focus();
    Task CloseAsync();
}
