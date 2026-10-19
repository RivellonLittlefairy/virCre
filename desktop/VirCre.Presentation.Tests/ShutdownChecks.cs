using VirCre.Launcher;
using VirCre.Presentation;

internal static class ShutdownChecks {
    public static async Task RunAsync() {
        var rootEnabled=true;var navigationEnabled=true;
        void SetInteraction(bool enabled) {rootEnabled=enabled;navigationEnabled=enabled;}
        var completed=new ShutdownCoordinator();
        var backgroundFailure=new InvalidDataException("old refresh failure");
        var result=await completed.TryStopAsync([new("refresh",()=>Task.FromException(backgroundFailure))],()=>Task.CompletedTask,()=>false,SetInteraction);
        Check(result.CanClose && result.Failures.Single().Error==backgroundFailure,"a completed failed refresh is reported without blocking shutdown");
        Check(rootEnabled && navigationEnabled,"shutdown always restores both root and navigation interaction");

        var retry=new ShutdownCoordinator();var owned=true;var stopCalls=0;var readerCalls=0;
        IReadOnlyList<ShutdownWork> work=[new("reader",()=>{readerCalls++;return Task.FromException(new IOException("historical reader failure"));})];
        Task StopWindows() {stopCalls++;if(stopCalls==1)throw new IOException("owned process remains alive");owned=false;return Task.CompletedTask;}
        var failed=await retry.TryStopAsync(work,StopWindows,()=>owned,SetInteraction);
        Check(!failed.CanClose && rootEnabled && navigationEnabled && !retry.IsStopping && retry.HasStarted,"failed window cleanup restores management controls and keeps the shutdown state explicit");
        var retried=await retry.TryStopAsync(work,StopWindows,()=>owned,SetInteraction);
        Check(retried.CanClose && stopCalls==2 && readerCalls==1,"retry closes remaining windows without repeating historical background failures");

        var concurrent=new ShutdownCoordinator();var reader=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var windows=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var windowStops=0;
        IReadOnlyList<ShutdownWork> pending=[new("write",()=>reader.Task)];
        Task FinishWindows() {windowStops++;return windows.Task;}
        var first=concurrent.TryStopAsync(pending,FinishWindows,()=>false,SetInteraction);
        var second=concurrent.TryStopAsync(pending,FinishWindows,()=>false,SetInteraction);
        Check(ReferenceEquals(first,second) && !rootEnabled && !navigationEnabled && windowStops==1,"concurrent close requests share one cleanup attempt");
        windows.SetResult();
        Check(!first.IsCompleted,"shutdown still waits for outstanding writes after windows close");
        reader.SetResult();await first;
        Check(rootEnabled && navigationEnabled,"interaction is restored after the shared attempt completes");

        var independent=new ShutdownCoordinator();var siblingObserved=false;var ownedObserved=false;
        var independentResult=await independent.TryStopAsync([
            new("synchronous failure",()=>throw new IOException("reader callback failed")),
            new("cancelled",()=>Task.FromCanceled(new CancellationToken(true))),
            new("sibling",()=>{siblingObserved=true;return Task.CompletedTask;})],
            ()=>{ownedObserved=true;return Task.FromException(new IOException("process already exited"));},()=>false,SetInteraction);
        Check(independentResult.CanClose && independentResult.Failures.Count==2 && siblingObserved && ownedObserved,"one failed cleanup never skips siblings and exited windows do not block closing");
        var retained=await new ShutdownCoordinator().TryStopAsync([],()=>Task.CompletedTask,()=>true,SetInteraction);
        Check(!retained.CanClose,"a still-owned window prevents closing even when the cleanup task reports success");

        var host=new RetryWindowHost();var manager=new AppWindowManager(host);
        var opened=await manager.OpenAsync(new("phone","Phone","com.android.settings","Settings"));
        var integrated=new ShutdownCoordinator();
        IReadOnlyList<ShutdownWork> historical=[new("refresh",()=>Task.FromException(new InvalidDataException("old format error")))];
        var blocked=await integrated.TryStopAsync(historical,manager.StopAsync,()=>manager.Windows.Count>0,SetInteraction);
        Check(!blocked.CanClose && rootEnabled && navigationEnabled && manager.Windows.Single().State==AppWindowState.CloseFailed,"real window manager retains a failed process while shutdown restores interaction");
        Check(manager.Focus(opened.Window.Id),"the retained application can still be focused after shutdown failure");
        try {await manager.OpenAsync(new("phone","Phone","com.other.app","Other"));throw new Exception("Shutdown manager accepted new work");}
        catch(InvalidOperationException) { }
        await manager.CloseAsync(opened.Window.Id);
        var finished=await integrated.TryStopAsync(historical,manager.StopAsync,()=>manager.Windows.Count>0,SetInteraction);
        Check(finished.CanClose && host.Process.CloseCalls==2 && manager.Windows.Count==0,"manual retry releases real manager ownership and historical refresh failure does not prevent exit");
    }
    private static void Check(bool condition,string message) {if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message);}

    private sealed class RetryWindowHost:IAppWindowHost {
        public RetryWindow Process {get;}=new();
        public IAppWindowProcess Start(AppLaunchRequest request)=>Process;
    }
    private sealed class RetryWindow:IAppWindowProcess {
        private readonly TaskCompletionSource exited=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CloseCalls {get;private set;}
        public bool HasExited=>exited.Task.IsCompleted;
        public int? DisplayId=>42;
        public int ExitCode=>0;
        public Task WaitForReadyAsync(CancellationToken token)=>Task.CompletedTask;
        public Task WaitForExitAsync()=>exited.Task;
        public bool Focus()=>!HasExited;
        public Task CloseAsync() {
            if(++CloseCalls==1)throw new IOException("process still alive");
            exited.TrySetResult();return Task.CompletedTask;
        }
        public void Dispose() { }
    }
}
