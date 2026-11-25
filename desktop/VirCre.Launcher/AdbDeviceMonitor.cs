using System.Diagnostics;
using System.Text;

namespace VirCre.Launcher;

/// <summary>Owns one ADB tracking client. Reconnects after server restarts without killing the shared ADB server.</summary>
public sealed class AdbDeviceMonitor(BundledTools? tools=null) {
    public async Task RunAsync(Action changed,Action<Exception> reportFailure,CancellationToken token) {
        var retrySeconds=1;
        while(!token.IsCancellationRequested) {
            try {
                await TrackAsync(()=>{retrySeconds=1;changed();},token).ConfigureAwait(false);
            } catch(OperationCanceledException) when(token.IsCancellationRequested) {break;}
            catch(Exception error) when(error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException) {
                if(token.IsCancellationRequested)break;
                reportFailure(error);
            }
            try {await Task.Delay(TimeSpan.FromSeconds(retrySeconds),token).ConfigureAwait(false);}
            catch(OperationCanceledException) when(token.IsCancellationRequested) {break;}
            retrySeconds=Math.Min(10,retrySeconds*2);
        }
    }

    private async Task TrackAsync(Action changed,CancellationToken token) {
        var start=(tools??new BundledTools()).CreateStartInfo("adb");
        start.UseShellExecute=false;start.CreateNoWindow=true;
        start.RedirectStandardOutput=true;start.RedirectStandardError=true;
        start.StandardOutputEncoding=Encoding.UTF8;start.StandardErrorEncoding=Encoding.UTF8;
        start.ArgumentList.Add("track-devices");start.ArgumentList.Add("-l");
        using var process=new Process {StartInfo=start};
        token.ThrowIfCancellationRequested();
        process.Start();
        using var cancellation=token.Register(()=>{
            try {if(!process.HasExited)process.Kill();}
            catch(InvalidOperationException) { /* The tracking client already exited. */ }
            catch(System.ComponentModel.Win32Exception) { /* Cleanup below observes the process state again. */ }
        });
        var errors=process.StandardError.ReadToEndAsync(token);
        var snapshots=new AdbDeviceSnapshot();
        try {
            while(!token.IsCancellationRequested) {
                if(await snapshots.ReadAsync(process.StandardOutput.BaseStream,token).ConfigureAwait(false))changed();
            }
            token.ThrowIfCancellationRequested();
            throw new IOException("ADB 设备监听已断开，将自动重新连接。");
        } finally {
            // Only this tracking client belongs to us. Its server may be shared with other applications.
            if(!process.HasExited) {
                try {process.Kill();}catch(InvalidOperationException) { /* The tracking client already exited. */ }
            }
            await process.WaitForExitAsync().ConfigureAwait(false);
            try {await errors.ConfigureAwait(false);}catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        }
    }
}
