using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using VirCam.Core;
using VirCre.Launcher;

namespace VirCre.Camera;

/// <summary>Owns one association and forward; identifies only its GUID-tagged bundled server for cleanup.</summary>
internal sealed class CameraDeviceSession(AppLaunchRequest request,BundledTools tools,Action<string>? diagnostic=null) {
    private const string MainClass="com.genymobile.scrcpy.Server";
    private readonly ICommandRunner runner=new CommandRunner(tools);
    private readonly string session=Guid.NewGuid().ToString("N");
    private readonly string mac=CreateMac();
    private readonly TaskCompletionSource<(int Device,int Display,int Width,int Height,int Format)> ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool associationAttempted,forwardAttempted,agentAttempted;
    private int remotePid;
    private string cameraState="等待应用打开虚拟相机";
    private readonly object diagnosticSync=new();
    private readonly HashSet<string> reported=[];
    internal string CameraState=>Volatile.Read(ref cameraState);
    private string Socket=>"localabstract:vircre-"+session;

    internal async Task<string> PrepareAsync(CancellationToken token) {
        var sdk=await AdbAsync(["shell","getprop","ro.build.version.sdk"],token).ConfigureAwait(false);
        if(!int.TryParse(sdk.Trim(),out var version) || version<36)throw new IOException("虚拟摄像头需要 Android 16 或以上。");
        if((await AdbAsync(["shell","am","get-current-user"],token).ConfigureAwait(false)).Trim()!="0")
            throw new IOException("此实验版本仅支持手机的主用户（用户0）。");
        var before=await AdbAsync(["shell","cmd","companiondevice","list","0"],token).ConfigureAwait(false);
        if(before.Contains(mac,StringComparison.OrdinalIgnoreCase))throw new IOException("临时关联地址冲突，请重新打开。");
        associationAttempted=true;
        await AdbAsync(["shell","cmd","companiondevice","associate","0","com.android.shell",mac,"android.app.role.COMPANION_DEVICE_APP_STREAMING","true"],token).ConfigureAwait(false);
        int association=0;
        for(int i=0;i<12;i++) {
            var list=await AdbAsync(["shell","cmd","companiondevice","list","0"],token).ConfigureAwait(false);
            var match=Regex.Match(list,@"(?im)^\s*(\d+)\s*\|\s*com\.android\.shell\s*\|\s*"+Regex.Escape(mac)+@"\s*$");
            if(match.Success && int.TryParse(match.Groups[1].Value,out association))break;
            await Task.Delay(250,token).ConfigureAwait(false);
        }
        if(association<=0)throw new IOException("手机未创建可核对的虚拟设备关联。");
        agentAttempted=true; // Covers cancellation immediately after the scrcpy process is launched.
        return association.ToString(CultureInfo.InvariantCulture)+":"+session;
    }
    internal async Task<ConnectionInfo> ConnectAsync(CancellationToken token) {
        var metadata=await ready.Task.WaitAsync(token).ConfigureAwait(false);
        forwardAttempted=true;
        var portText=await AdbAsync(["forward","tcp:0",Socket],token).ConfigureAwait(false);
        if(!int.TryParse(portText.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out var port) || port<1 || port>65535)
            throw new IOException("无法取得摄像头数据通道端口。");
        return new(session,port,metadata.Device,metadata.Display,request.Serial,metadata.Width,metadata.Height,metadata.Format);
    }
    internal void ObserveServerLine(string line) {
        try {
                int start=line.IndexOf("{\"event\":",StringComparison.Ordinal);
                if(start<0)return;
                using var json=JsonDocument.Parse(line[start..]);
                var root=json.RootElement;
                if(!root.TryGetProperty("event",out var kind))return;
                if(kind.GetString()=="started" && root.GetProperty("session").GetString()==session)
                    Volatile.Write(ref remotePid,root.GetProperty("pid").GetInt32());
                if(kind.GetString()=="ready") {
                    if(root.GetProperty("session").GetString()!=session || root.GetProperty("inputSocket").GetString()!="vircre-"+session)
                        throw new IOException("手机返回的摄像头会话标识无效。");
                    int device=root.GetProperty("deviceId").GetInt32(),display=root.GetProperty("displayId").GetInt32();
                    if(device<=0 || display<=0)throw new IOException("手机返回的虚拟设备信息无效。");
                    int width=root.GetProperty("inputWidth").GetInt32(),height=root.GetProperty("inputHeight").GetInt32(),format=root.GetProperty("inputFormat").GetInt32();
                    if(!FrameProtocol.IsSupportedImage(width,height,format) || format!=FrameProtocol.FormatJpeg ||
                        width!=root.GetProperty("displayWidth").GetInt32() || height!=root.GetProperty("displayHeight").GetInt32())
                        throw new IOException("内置相机组件画面格式不匹配，请使用完整新版目录。");
                    ready.TrySetResult((device,display,width,height,format));
                }
                if(kind.GetString()=="failed")ready.TrySetException(new IOException("手机无法创建虚拟摄像头，请检查 Android 版本和系统支持。"));
                string? state=kind.GetString() switch {
                    "stream-configured"=>"应用已打开虚拟相机，画面流已配置",
                    "desktop-camera-first-frame"=>root.GetProperty("hasInput").GetBoolean()?"虚拟相机已提交桌面首帧":"虚拟相机已提交首帧，当时尚无 PC 输入",
                    "stream-closed"=>"应用已关闭虚拟相机流",
                    "render-error"=>"虚拟相机渲染失败，请关闭并重新打开应用",
                    _=>null
                };
                if(state!=null)Volatile.Write(ref cameraState,state);
                string? stage=kind.GetString() switch {
                    "started"=>"server-started", "ready"=>"device-ready", "encoder-attached"=>"encoder-attached",
                    "input-connected"=>"desktop-connected", "input-frame"=>"desktop-frame-received",
                    "stream-configured"=>"camera-stream-configured", "stream-closed"=>"camera-stream-closed",
                    "desktop-camera-first-frame"=>root.GetProperty("hasInput").GetBoolean()?"camera-first-frame-with-input":"camera-first-frame-without-input",
                    "render-error"=>"camera-render-failed", _=>null
                };
                if(stage!=null) {
                    bool first;lock(diagnosticSync)first=reported.Add(stage);
                    if(first)diagnostic?.Invoke(stage); // Fixed stage only, never arbitrary process output or pixels.
                }
        } catch(Exception e) {ready.TrySetException(new IOException("虚拟摄像头初始化信息读取失败。",e));}
    }
    private async Task<string> AdbAsync(IReadOnlyList<string> args,CancellationToken token) {
        var result=await runner.RunAsync("adb",new[]{"-s",request.Serial}.Concat(args).ToArray(),TimeSpan.FromSeconds(12),token).ConfigureAwait(false);
        if(result.ExitCode!=0)throw new IOException("手机连接命令失败（"+args[0]+"），请检查 ADB 连接。");
        return result.Output;
    }
    internal async Task CleanupAsync() {
        var failures=new List<string>();
        async Task Attempt(string name,Func<Task> work) {try {await work().ConfigureAwait(false);}catch(Exception) {failures.Add(name);}}
        if(agentAttempted)await Attempt("手机摄像头进程",async ()=>{
            // A cancellation may arrive before 'started'. Discover only our GUID-tagged command.
            var processes=await AdbAsync(["shell","ps","-A","-o","PID,ARGS"],CancellationToken.None).ConfigureAwait(false);
            var pids=processes.Split('\n').Where(line=>line.Contains(MainClass,StringComparison.Ordinal) && line.Contains(session,StringComparison.Ordinal))
                .Select(line=>Regex.Match(line,@"^\s*(\d+)\s+")).Where(match=>match.Success).Select(match=>int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture)).ToHashSet();
            if(Volatile.Read(ref remotePid)>0)pids.Add(Volatile.Read(ref remotePid));
            foreach(var pid in pids) {
                // Revalidate immediately before signalling to protect against PID reuse.
                var result=await runner.RunAsync("adb",["-s",request.Serial,"shell",$"if [ -r /proc/{pid}/cmdline ]; then cat /proc/{pid}/cmdline; fi"],TimeSpan.FromSeconds(8),CancellationToken.None).ConfigureAwait(false);
                if(result.ExitCode!=0)throw new IOException();
                if(!result.Output.Contains(MainClass,StringComparison.Ordinal) || !result.Output.Contains(session,StringComparison.Ordinal))continue;
                await AdbAsync(["shell","kill","-TERM",pid.ToString(CultureInfo.InvariantCulture)],CancellationToken.None).ConfigureAwait(false);
                await Task.Delay(300).ConfigureAwait(false);
                var check=await AdbAsync(["shell",$"if [ -r /proc/{pid}/cmdline ]; then cat /proc/{pid}/cmdline; fi"],CancellationToken.None).ConfigureAwait(false);
                if(check.Contains(MainClass,StringComparison.Ordinal) && check.Contains(session,StringComparison.Ordinal))
                    await AdbAsync(["shell","kill","-KILL",pid.ToString(CultureInfo.InvariantCulture)],CancellationToken.None).ConfigureAwait(false);
            }
            agentAttempted=false;
        }).ConfigureAwait(false);
        if(forwardAttempted)await Attempt("ADB数据通道",async ()=>{
            var list=await AdbAsync(["forward","--list"],CancellationToken.None).ConfigureAwait(false);
            foreach(var line in list.Split('\n')) {
                var fields=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                if(fields.Length==3 && fields[0]==request.Serial && fields[2]==Socket)
                    await AdbAsync(["forward","--remove",fields[1]],CancellationToken.None).ConfigureAwait(false);
            }
            forwardAttempted=false;
        }).ConfigureAwait(false);
        if(associationAttempted)await Attempt("临时设备关联",async ()=>{
            var list=await AdbAsync(["shell","cmd","companiondevice","list","0"],CancellationToken.None).ConfigureAwait(false);
            if(list.Contains(mac,StringComparison.OrdinalIgnoreCase))
                await AdbAsync(["shell","cmd","companiondevice","disassociate","0","com.android.shell",mac],CancellationToken.None).ConfigureAwait(false);
            list=await AdbAsync(["shell","cmd","companiondevice","list","0"],CancellationToken.None).ConfigureAwait(false);
            if(list.Contains(mac,StringComparison.OrdinalIgnoreCase))throw new IOException();
            associationAttempted=false;
        }).ConfigureAwait(false);
        if(failures.Count>0)throw new IOException("未能清理："+string.Join("、",failures)+"。请恢复原设备连接后重试关闭；手机摄像头进程最多615秒后自行退出。");
    }
    private static string CreateMac() {
        var bytes=System.Security.Cryptography.RandomNumberGenerator.GetBytes(6);bytes[0]=(byte)((bytes[0]|2)&0xfe);
        return string.Join(":",bytes.Select(value=>value.ToString("X2",CultureInfo.InvariantCulture)));
    }
}
