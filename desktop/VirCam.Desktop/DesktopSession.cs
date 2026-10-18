using System.Diagnostics;
using System.Text.Json;
using VirCam.Core;

namespace VirCam.Desktop;

internal sealed class DesktopSession(string root,string powershell) {
    private Process? bootstrap;
    private string? stopFile;
    private Task? outputLog,errorLog;
    public string? LogDirectory {get;private set;}
    public string? ConnectionPath {get;private set;}
    public int MirrorPid {get;private set;}
    public bool HasExited=>bootstrap?.HasExited??true;
    public async Task<ConnectionInfo> StartAsync(string serial,bool install,CancellationToken token) {
        var folder=Path.Combine(root,"out","desktop-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);LogDirectory=folder;
        ConnectionPath=Path.Combine(folder,"connection.json");stopFile=Path.Combine(folder,"stop.request");
        var start=new ProcessStartInfo(powershell) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root};
        foreach(var arg in new[]{"-NoProfile","-File",Path.Combine(root,"tools","Start-P0.ps1"),"-Serial",serial,"-Seconds","600","-InputMode","desktop","-StateFile",ConnectionPath,"-StopFile",stopFile}) start.ArgumentList.Add(arg);
        if(!install) start.ArgumentList.Add("-SkipInstall");
        bootstrap=Process.Start(start)??throw new IOException("无法启动实验服务");
        outputLog=SaveAsync(bootstrap.StandardOutput,Path.Combine(folder,"runner.log"));
        errorLog=SaveAsync(bootstrap.StandardError,Path.Combine(folder,"runner.stderr.log"));
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(150));
        while(!File.Exists(ConnectionPath)) {
            timeout.Token.ThrowIfCancellationRequested();
            if(bootstrap.HasExited) throw new IOException("会话启动失败，请查看 "+folder);
            await Task.Delay(250,timeout.Token);
        }
        // The handoff file may have been observed between creation and the final write.
        for(int i=0;;i++) {
            try {
                var info=ConnectionInfo.Load(ConnectionPath);
                using var json=JsonDocument.Parse(File.ReadAllText(ConnectionPath));
                MirrorPid=json.RootElement.GetProperty("mirrorPid").GetInt32();
                LogDirectory=json.RootElement.GetProperty("evidenceDirectory").GetString()??folder;return info;
            } catch(Exception e) when(i<10 && (e is IOException || e is JsonException)) { await Task.Delay(100,timeout.Token); }
        }
    }
    public async Task StopAsync() {
        if(stopFile!=null) await File.WriteAllTextAsync(stopFile,"stop");
        if(bootstrap!=null && !bootstrap.HasExited) {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(50));
            await bootstrap.WaitForExitAsync(timeout.Token);
        }
        if(bootstrap!=null) {
            await Task.WhenAll(outputLog??Task.CompletedTask,errorLog??Task.CompletedTask);
            var launch=ConnectionPath+".launch.json";
            if(File.Exists(launch)) {using var json=JsonDocument.Parse(File.ReadAllText(launch));LogDirectory=json.RootElement.GetProperty("evidenceDirectory").GetString()??LogDirectory;}
            var resultPath=Path.Combine(LogDirectory??root,"result.json");
            try {SessionCompletion.EnsureClean(bootstrap.ExitCode,File.Exists(resultPath)?File.ReadAllText(resultPath):null);}
            catch(IOException e) {throw new IOException(e.Message+"；日志："+LogDirectory,e);}
        }
    }
    public IntPtr MirrorHandle() {
        try { return MirrorPid>0?Process.GetProcessById(MirrorPid).MainWindowHandle:IntPtr.Zero; } catch(ArgumentException) { return IntPtr.Zero; }
    }
    private static async Task SaveAsync(StreamReader reader,string path) { await File.WriteAllTextAsync(path,await reader.ReadToEndAsync()); }
}
