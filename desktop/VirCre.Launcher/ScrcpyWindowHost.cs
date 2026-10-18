using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace VirCre.Launcher;

public sealed partial class ScrcpyWindowHost : IAppWindowHost {
    public static int? ParseDisplayId(string line) {
        var match=Regex.Match(line,@"New display:.*?\bid=(\d+)\)",RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value,out var id) && id>0 ? id : null;
    }
    public static IReadOnlyList<string> Arguments(AppLaunchRequest request) {
        if(!SerialPattern().IsMatch(request.Serial) || !PackagePattern().IsMatch(request.PackageName))
            throw new ArgumentException("设备或应用标识无效，请刷新列表后重试。");
        return ["-s",request.Serial,"--new-display=1080x1920/420","--start-app="+request.PackageName,
            "--window-title="+request.Name+" · "+request.DeviceName+" · virCre","--no-audio","--display-ime-policy=local",
            "--window-width=420","--window-height=760"];
    }
    public IAppWindowProcess Start(AppLaunchRequest request) {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("独立窗口需要 Windows。");
        var process=new Process {StartInfo=new ProcessStartInfo("scrcpy") {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
        }};
        foreach(var argument in Arguments(request))process.StartInfo.ArgumentList.Add(argument);
        try {process.Start();return new ScrcpyProcess(process);}
        catch(System.ComponentModel.Win32Exception) {process.Dispose();throw new IOException("无法启动 scrcpy，请检查工具路径。");}
    }
    [GeneratedRegex(@"^[a-zA-Z0-9._:-]+$")]private static partial Regex SerialPattern();
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*(?:\.[a-zA-Z_][a-zA-Z0-9_]*)+$")]private static partial Regex PackagePattern();

    private sealed class ScrcpyProcess : IAppWindowProcess {
        private readonly Process process;
        private readonly int processId;
        private readonly Task completion;
        private readonly object logSync=new();
        private readonly Queue<string> recent=[];
        private volatile bool exited;
        private int disposed,displayId;
        private int exitCode;
        private IntPtr window;
        public ScrcpyProcess(Process process) {
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
        private async Task DrainAsync(StreamReader reader) {
            while(await reader.ReadLineAsync().ConfigureAwait(false) is { } line) {
                lock(logSync){recent.Enqueue(line);while(recent.Count>24)recent.Dequeue();}
                if(ParseDisplayId(line) is { } id)Volatile.Write(ref displayId,id);
            }
        }
        private async Task CompleteAsync(Task output,Task error) {
            await process.WaitForExitAsync().ConfigureAwait(false);
            exitCode=process.ExitCode;exited=true;
            await Task.WhenAll(output,error).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
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
