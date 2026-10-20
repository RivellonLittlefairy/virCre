using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace VirCre.Launcher;

public sealed partial class ScrcpyWindowHost(BundledTools? tools=null, Action<string>? inputDiagnostic=null, Action<string>? sessionEvents=null, Action<int>? processExited=null) : IAppWindowHost {
    public static int? ParseDisplayId(string line) {
        var match=Regex.Match(line,@"New display:.*?\bid=(\d+)\)",RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value,out var id) && id>0 ? id : null;
    }
    public static IReadOnlyList<string> Arguments(AppLaunchRequest request) {
        if(!SerialPattern().IsMatch(request.Serial) || !PackagePattern().IsMatch(request.PackageName))
            throw new ArgumentException("设备或应用标识无效，请刷新列表后重试。");
        // Omitted dimensions let the server read this phone's main display size and DPI.
        // Omitted window dimensions let the client preserve that aspect ratio within the PC work area.
        // Keep virtual displays active; primary-screen activity is opt-in via scrcpy's panel-off command.
        return ["-s",request.Serial,"--new-display","--start-app="+request.PackageName,
            "--window-title="+request.Name+" · "+request.DeviceName+" · virCre","--no-audio","--display-ime-policy=local","--keep-active"];
    }
    public IAppWindowProcess Start(AppLaunchRequest request) => StartCore(request, null);
    public IAppWindowProcess StartWithCamera(AppLaunchRequest request, string cameraConfig) {
        if(!CameraConfigPattern().IsMatch(cameraConfig))throw new ArgumentException("虚拟摄像头会话无效。");
        return StartCore(request,cameraConfig);
    }
    private IAppWindowProcess StartCore(AppLaunchRequest request, string? cameraConfig) {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("独立窗口需要 Windows。");
        var bundledTools=tools??new BundledTools();
        var start=bundledTools.CreateStartInfo("scrcpy");
        start.UseShellExecute=false;start.CreateNoWindow=true;
        start.RedirectStandardOutput=true;start.RedirectStandardError=true;
        start.StandardOutputEncoding=Encoding.UTF8;start.StandardErrorEncoding=Encoding.UTF8;
        var process=new Process {StartInfo=start};
        foreach(var argument in Arguments(request))process.StartInfo.ArgumentList.Add(argument);
        if(cameraConfig!=null) {
            process.StartInfo.ArgumentList.Add("--vircre-camera="+cameraConfig);
            process.StartInfo.ArgumentList.Add("--window-exclude-from-capture");
            // Keep the video viewport aligned with the registered camera's natural orientation.
            process.StartInfo.ArgumentList.Add("--capture-orientation=@0");
        }
        if(bundledTools.SupportsWindowsIme)process.StartInfo.ArgumentList.Add("--windows-ime");
        try {process.Start();return new ScrcpyProcess(process,inputDiagnostic,sessionEvents,processExited);}
        catch(System.ComponentModel.Win32Exception) {process.Dispose();throw new IOException("无法启动内置 scrcpy，请检查完整发布目录中的手机连接组件。");}
    }
    [GeneratedRegex(@"^[a-zA-Z0-9._:-]+$")]private static partial Regex SerialPattern();
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*(?:\.[a-zA-Z_][a-zA-Z0-9_]*)+$")]private static partial Regex PackagePattern();
    [GeneratedRegex(@"^[1-9][0-9]*:[a-f0-9]{32}$")]private static partial Regex CameraConfigPattern();

    private sealed class ScrcpyProcess : IAppWindowProcess {
        private readonly Process process;
        private readonly int processId;
        private readonly Task completion;
        private readonly object logSync=new();
        private readonly Queue<string> recent=[];
        private readonly HashSet<string> reportedInputFailures = new(StringComparer.Ordinal);
        private readonly Action<string>? inputDiagnostic;
        private readonly Action<string>? sessionEvents;
        private readonly Action<int>? processExited;
        private volatile bool exited;
        private int disposed,displayId;
        private int exitCode;
        private IntPtr window;
        public ScrcpyProcess(Process process, Action<string>? inputDiagnostic, Action<string>? sessionEvents, Action<int>? processExited) {
            this.inputDiagnostic=inputDiagnostic;
            this.sessionEvents=sessionEvents;
            this.processExited=processExited;
            this.process=process;processId=process.Id;
            var output=DrainAsync(process.StandardOutput);
            var error=DrainAsync(process.StandardError);
            completion=CompleteAsync(output,error);
        }
        public bool HasExited {
            get {if(exited)return true;try{return process.HasExited;}catch(InvalidOperationException){return true;}}
        }
        public int? DisplayId=>Volatile.Read(ref displayId)>0?Volatile.Read(ref displayId):null;
        public int ExitCode=>exitCode;
        public IntPtr NativeHandle {
            get {
                if(HasExited || window==IntPtr.Zero)return IntPtr.Zero;
                GetWindowThreadProcessId(window,out var owner);
                return owner==(uint)processId ? window : IntPtr.Zero;
            }
        }
        private async Task DrainAsync(StreamReader reader) {
            while(await reader.ReadLineAsync().ConfigureAwait(false) is { } line) {
                lock(logSync){recent.Enqueue(line);while(recent.Count>24)recent.Dequeue();}
                RecordInputFailure(line);
                sessionEvents?.Invoke(line);
                if(ParseDisplayId(line) is { } id)Volatile.Write(ref displayId,id);
            }
        }
        private void RecordInputFailure(string line) {
            // Only fixed stage names, once per session/stage. Never persist arbitrary
            // scrcpy output, submitted text, key codes, clipboard data or sequences.
            string? stage=null;
            foreach(var candidate in new[]{"display","clipboard-lock","clipboard","paste-key"}) {
                if(line.EndsWith("IME commit rejected at "+candidate,StringComparison.Ordinal)) {stage=candidate;break;}
            }
            if(line.EndsWith("Text key injection failed",StringComparison.Ordinal)) stage="native-key";
            foreach(var candidate in new[]{"touch-size-mismatch","touch-pointer-limit","touch-inject-rejected",
                "touch-down-accepted","touch-up-zero","touch-up-short","touch-up-normal","touch-up-long"}) {
                if(line.EndsWith("VirCre touch stage: "+candidate,StringComparison.Ordinal)) {stage=candidate;break;}
            }
            foreach(var candidate in new[]{"power-main-activity-enabled","power-main-asleep","power-panel-restored"}) {
                if(line.EndsWith("VirCre power stage: "+candidate,StringComparison.Ordinal)) {stage=candidate;break;}
            }
            if(stage==null)return;
            lock(logSync) {if(!reportedInputFailures.Add(stage))return;}
            inputDiagnostic?.Invoke(stage);
        }
        private async Task CompleteAsync(Task output,Task error) {
            await process.WaitForExitAsync().ConfigureAwait(false);
            exitCode=process.ExitCode;exited=true;
            try {await Task.WhenAll(output,error).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);}
            finally {
                try {processExited?.Invoke(exitCode);}
                catch(Exception) { /* Diagnostics must not change process ownership or cleanup. */ }
            }
        }
        public async Task WaitForReadyAsync(CancellationToken token) {
            while(!HasExited) {
                token.ThrowIfCancellationRequested();
                try {process.Refresh();window=process.MainWindowHandle;}
                catch(InvalidOperationException) {break;}
                if(window!=IntPtr.Zero)return;
                await Task.Delay(100,token).ConfigureAwait(false);
            }
            string detail;lock(logSync)detail=recent.LastOrDefault(line=>line.Contains("ERROR",StringComparison.OrdinalIgnoreCase))??"请检查设备连接与应用兼容性。";
            throw new IOException("scrcpy 未能打开窗口："+detail);
        }
        public Task WaitForExitAsync()=>completion;
        public bool Focus() {
            if(HasExited || window==IntPtr.Zero)return false;
            GetWindowThreadProcessId(window,out var owner);
            if(owner!=(uint)processId)return false;
            if(IsIconic(window))ShowWindow(window,9);
            return SetForegroundWindow(window);
        }
        public async Task CloseAsync() {
            if(!HasExited) {
                try {process.CloseMainWindow();}catch(InvalidOperationException){}
                try {await completion.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);return;}
                catch(TimeoutException) {
                    if(!HasExited)try{process.Kill(true);}catch(InvalidOperationException){}
                }
            }
            await completion.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        public void Dispose() {if(Interlocked.Exchange(ref disposed,1)==0)process.Dispose();}
    }
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr handle,int command);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr handle,out uint processId);
}
