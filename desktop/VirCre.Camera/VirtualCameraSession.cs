using VirCam.Core;
using VirCre.Launcher;
using System.Diagnostics;
using System.Globalization;

namespace VirCre.Camera;

public sealed record CameraStatus(bool Paused,string Message);

/// <summary>The completion signal is published only after resources have actually been released.</summary>
internal sealed class VirtualCameraSession : IAppWindowProcess {
    private readonly CameraDeviceSession phone;
    private readonly ScrcpyWindowHost scrcpy;
    private readonly AppLaunchRequest request;
    private readonly Action<string>? notice;
    private readonly CancellationTokenSource stop=new();
    private readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim cleanupGate=new(1,1);
    private readonly Task lifetime;
    private IAppWindowProcess? window;
    private FrameTransport? frames;
    private volatile bool exited;
    private int exitCode,paused,resumeVersion,disposed;
    private int targetFrameRate;
    private string transferStatus="等待投屏分辨率";
    private int inputWidth,inputHeight;
    private CameraStatus status=new(false,"正在准备窗口下方画面…");
    internal CameraStatus Status {
        get {
            var current=Volatile.Read(ref status);bool isPaused=Volatile.Read(ref paused)!=0;
            var message=isPaused && !current.Paused?"已暂停采集。":current.Message;
            return current with {Paused=isPaused,Message=message+"\n目标"+Volatile.Read(ref targetFrameRate)+"fps · "+Volatile.Read(ref transferStatus)+"\n手机相机："+phone.CameraState};
        }
    }
    public bool HasExited=>exited;
    public int ExitCode=>exitCode;
    public int? DisplayId=>window?.DisplayId;
    public IntPtr NativeHandle=>window?.NativeHandle??IntPtr.Zero;

    internal VirtualCameraSession(AppLaunchRequest request,BundledTools tools,Action<string>? inputDiagnostic,Action<string>? notice,Action<string>? diagnostic) {
        this.request=request;this.notice=notice;
        targetFrameRate=CameraFrameRates.Validate(request.CameraFrameRate);
        phone=new(request,tools,diagnostic,ClientQuit);scrcpy=new(tools,inputDiagnostic,phone.ObserveServerLine,phone.ObserveProcessExit);
        lifetime=Task.Run(RunAsync);
    }
    private void ClientQuit() {
        phone.RecordStage("desktop-client-quit-observed");
        // Stop capture and pending frame ACK reads before normal server teardown closes
        // their socket. Otherwise its EOF can incorrectly become session exit code 1.
        try {stop.Cancel();}catch(ObjectDisposedException) when(exited) { }
    }
    public Task WaitForReadyAsync(CancellationToken token)=>ready.Task.WaitAsync(token);
    public Task WaitForExitAsync()=>completion.Task;
    public bool Focus()=>window?.Focus()??false;
    internal void SetTargetFrameRate(int frameRate)=>Interlocked.Exchange(ref targetFrameRate,CameraFrameRates.Validate(frameRate));
    internal void SetPaused(bool value) {
        Interlocked.Exchange(ref paused,value?1:0);
        if(!value)Interlocked.Increment(ref resumeVersion);
        Volatile.Write(ref status,new(value,value?"已暂停，手机相机显示暂停画面。":"正在重新采集窗口下方画面…"));
    }
    private async Task RunAsync() {
        using var startup=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        startup.CancelAfter(TimeSpan.FromSeconds(85));
        try {
            var cameraConfig=await phone.PrepareAsync(startup.Token).ConfigureAwait(false);
            startup.Token.ThrowIfCancellationRequested();
            window=scrcpy.StartWithCamera(request,cameraConfig);
            await window.WaitForReadyAsync(startup.Token).ConfigureAwait(false);
            var connection=await phone.ConnectAsync(startup.Token).ConfigureAwait(false);
            inputWidth=connection.Width;inputHeight=connection.Height;
            Volatile.Write(ref transferStatus,$"{inputWidth}×{inputHeight} · 正在统计");
            frames=new(connection,message=>{
                if(Volatile.Read(ref paused)==0)Volatile.Write(ref status,new(false,message));
                Volatile.Write(ref transferStatus,$"{inputWidth}×{inputHeight} · 等待输入恢复");
                phone.RecordStage("desktop-input-reconnecting");
            },()=>window is {HasExited:false},()=>Volatile.Read(ref paused)!=0);
            await frames.ConnectAsync(startup.Token).ConfigureAwait(false);
            ready.TrySetResult();
            await PumpAsync(stop.Token).ConfigureAwait(false);
            phone.RecordStage("desktop-pump-ended");
        } catch(OperationCanceledException) when(stop.IsCancellationRequested) {ready.TrySetCanceled();}
        catch(Exception e) when(stop.IsCancellationRequested && (e is IOException or System.Net.Sockets.SocketException or ObjectDisposedException)) {ready.TrySetCanceled();}
        catch(OperationCanceledException e) {
            exitCode=1;
            phone.RecordStage(ready.Task.IsCompletedSuccessfully?"desktop-frame-timeout":"desktop-startup-timeout");
            var message=ready.Task.IsCompletedSuccessfully?"PC 画面传输超时，请检查设备连接后重新打开。":"虚拟摄像头启动超时，请检查设备连接。";
            ready.TrySetException(new TimeoutException(message,e));notice?.Invoke(message);
        }
        catch(Exception e) {
            exitCode=1;ready.TrySetException(e);
            phone.RecordStage(e switch {
                EndOfStreamException=>"desktop-input-eof",
                System.Net.Sockets.SocketException=>"desktop-input-socket-error",
                _=>"desktop-session-error"
            });
            notice?.Invoke("虚拟摄像头已停止："+e.Message);
        } finally {
            try {await CleanupAsync().ConfigureAwait(false);}
            catch(Exception e) {notice?.Invoke(e.Message);}
        }
    }
    private async Task PumpAsync(CancellationToken token) {
        WindowBackgroundCapture? capture=null;
        byte[]? lastSentImage=null;
        int version=Volatile.Read(ref resumeVersion);
        int sampleRate=Volatile.Read(ref targetFrameRate);
        long sampleStart=Stopwatch.GetTimestamp(),sampleBytes=0;
        int sampleFrames=0;
        bool recovering=false,inputRecoveryPending=false;
        int connectionGeneration=frames!.Generation;
        try {
            while(!token.IsCancellationRequested && window is {HasExited:false}) {
                long frameStart=Stopwatch.GetTimestamp();
                int frameRate=Volatile.Read(ref targetFrameRate);
                if(sampleRate!=frameRate) {
                    sampleRate=frameRate;sampleFrames=0;sampleBytes=0;sampleStart=Stopwatch.GetTimestamp();
                    Volatile.Write(ref transferStatus,$"{inputWidth}×{inputHeight} · 正在统计");
                }
                int current=Volatile.Read(ref resumeVersion);
                if(current!=version || frames!.Generation!=connectionGeneration) {
                    if(frames.Generation!=connectionGeneration)inputRecoveryPending=true;
                    connectionGeneration=frames.Generation;
                    capture?.Dispose();capture=null;version=current;
                    lastSentImage=null;
                    sampleFrames=0;sampleBytes=0;sampleStart=Stopwatch.GetTimestamp();
                    Volatile.Write(ref transferStatus,$"{inputWidth}×{inputHeight} · 正在统计");
                }
                if(Volatile.Read(ref paused)!=0) {
                    capture?.Dispose();capture=null;
                    lastSentImage=null;
                    sampleFrames=0;sampleBytes=0;sampleStart=Stopwatch.GetTimestamp();
                    Volatile.Write(ref transferStatus,$"{inputWidth}×{inputHeight} · 已暂停发送");
                    if(!await frames!.PauseAsync(token).ConfigureAwait(false))continue;
                    await Task.Delay(500,token).ConfigureAwait(false);
                    continue;
                }
                byte[]? image;
                try {
                    capture??=new(inputWidth,inputHeight);
                    image=await capture.CaptureAsync(NativeHandle,token).ConfigureAwait(false);
                } catch(OperationCanceledException) {throw;}
                catch(Exception e) {
                    token.ThrowIfCancellationRequested();
                    // Oversized unchanged content remembers its failed quality attempts.
                    // Other capture failures rebuild the source before reporting healthy input again.
                    if(e is not JpegPayloadTooLargeException) {capture?.Dispose();capture=null;}
                    lastSentImage=null;
                    var previousStatus=Volatile.Read(ref status);
                    if(Volatile.Read(ref paused)!=0 || Volatile.Read(ref resumeVersion)!=version)continue;
                    // A manual pause/resume published during error handling owns its
                    // status. Never replace that newer state with an automatic retry.
                    var retryStatus=new CameraStatus(false,(frames!.SupportsHoldLastFrame?"采集暂时失败，保持上一帧，等待自动恢复：":"正在等待自动恢复：")+e.Message);
                    if(!ReferenceEquals(Interlocked.CompareExchange(ref status,retryStatus,previousStatus),previousStatus))continue;
                    Volatile.Write(ref transferStatus,$"{inputWidth}×{inputHeight} · 等待采集恢复");
                    if(!recovering) {recovering=true;phone.RecordStage("desktop-capture-recovering");notice?.Invoke("虚拟摄像头暂时无法采集，将自动恢复："+e.Message);}
                    if(!await frames!.HoldAsync(token).ConfigureAwait(false))continue;
                    await Task.Delay(500,token).ConfigureAwait(false);
                    continue;
                }
                // A pause clicked while an asynchronous copy/encode was underway must win.
                if(Volatile.Read(ref paused)!=0 || Volatile.Read(ref resumeVersion)!=version)continue;
                if(image==null) {
                    lastSentImage=null;recovering=true;
                    Volatile.Write(ref status,new(false,frames!.SupportsHoldLastFrame?"暂未取得有效采集区域，保持已有画面，等待新完整帧。":"暂未取得有效采集区域，等待自动恢复。"));
                    Volatile.Write(ref transferStatus,$"{inputWidth}×{inputHeight} · 等待采集恢复");
                    if(!await frames.HoldAsync(token).ConfigureAwait(false))continue;
                    await Task.Delay(500,token).ConfigureAwait(false);
                }
                else {
                    bool repeated=ReferenceEquals(image,lastSentImage);
                    bool transferred=repeated
                        ? await frames!.RepeatFrameAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),token).ConfigureAwait(false)
                        : await frames!.SendFrameAsync(image,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),token).ConfigureAwait(false);
                    if(!transferred)continue;
                    if(Volatile.Read(ref paused)!=0 || Volatile.Read(ref resumeVersion)!=version)continue;
                    lastSentImage=image;
                    if(recovering) {recovering=false;phone.RecordStage("desktop-capture-recovered");}
                    if(inputRecoveryPending) {inputRecoveryPending=false;phone.RecordStage("desktop-input-recovered");}
                    if(!repeated || !frames!.SupportsRepeat) {sampleFrames++;sampleBytes+=image.Length;}
                    double seconds=Stopwatch.GetElapsedTime(sampleStart).TotalSeconds;
                    if(seconds>=2) {
                        Volatile.Write(ref transferStatus,string.Create(CultureInfo.InvariantCulture,$"{inputWidth}×{inputHeight} · 近{seconds:F1}秒画面更新 {sampleFrames/seconds:F1}fps / {sampleBytes*8.0/seconds/1000000:F1}Mbps"));
                        sampleFrames=0;sampleBytes=0;sampleStart=Stopwatch.GetTimestamp();
                    }
                    Volatile.Write(ref status,new(false,repeated && frames!.SupportsRepeat ? "桌面画面未变化，保持当前画面；内容或窗口位置变化后自动更新。" : "正在发送投屏画面实际覆盖的下层桌面，随窗口移动和缩放，不含窗口黑边。"));
                }
                double remaining=1000.0/Volatile.Read(ref targetFrameRate)-Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
                if(remaining>0)await Task.Delay(TimeSpan.FromMilliseconds(remaining),token).ConfigureAwait(false);
            }
        } finally {capture?.Dispose();}
    }
    public async Task CloseAsync() {
        if(exited)return;
        phone.RecordStage("desktop-close-requested");
        try {stop.Cancel();}catch(ObjectDisposedException) when(exited) {return;}
        await lifetime.ConfigureAwait(false);
        if(!exited)await CleanupAsync().ConfigureAwait(false);
    }
    private async Task CleanupAsync() {
        await cleanupGate.WaitAsync().ConfigureAwait(false);
        try {
            if(exited)return;
            frames?.Dispose();frames=null;
            var failures=new List<Exception>();
            if(window!=null) {
                try {
                    await window.CloseAsync().ConfigureAwait(false);
                    // Preserve a real client failure even if a quit signal preceded it.
                    if(window.ExitCode!=0)exitCode=window.ExitCode;
                    window.Dispose();window=null;
                }
                catch(Exception e) {failures.Add(e);}
            }
            try {await phone.CleanupAsync().ConfigureAwait(false);}catch(Exception e) {failures.Add(e);}
            if(failures.Count>0)throw new IOException(string.Join(" ",failures.Select(e=>e.Message)));
            exited=true;completion.TrySetResult();
        } finally {cleanupGate.Release();}
    }
    public void Dispose() {
        if(!exited || Interlocked.Exchange(ref disposed,1)!=0)return;
        stop.Dispose(); // cleanupGate may still be unwinding on the completion continuation.
    }
}
