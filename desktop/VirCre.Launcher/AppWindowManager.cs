namespace VirCre.Launcher;

/// <summary>Owns only processes it started. Reservations exist before startup, so concurrent opens deduplicate.</summary>
public sealed class AppWindowManager(IAppWindowHost host, TimeSpan? startupTimeout = null, Action<Exception>? observerFailure = null) {
    private sealed class Entry(AppLaunchRequest request) {
        public Guid Id {get;} = Guid.NewGuid();
        public AppLaunchRequest Request {get;} = request;
        public AppWindowState State {get;set;} = AppWindowState.Starting;
        public IAppWindowProcess? Process {get;set;}
        public TaskCompletionSource<AppWindowInfo> Ready {get;} = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StartCompleted {get;} = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource CancelStart {get;} = new();
        public Task? CloseWork {get;set;}
    }
    private readonly object sync = new();
    private readonly Dictionary<(string Serial,string Package),Entry> entries = [];
    private readonly CancellationTokenSource shutdown = new();
    private bool stopped;
    public event EventHandler? Changed;
    public event Action<string>? Problem;
    public IReadOnlyList<AppWindowInfo> Windows {get {lock(sync)return entries.Values.Select(Info).ToArray();}}

    public async Task<AppOpenResult> OpenAsync(AppLaunchRequest request, CancellationToken token = default) {
        _ = ScrcpyWindowHost.Arguments(request); // Validate before reserving or starting anything.
        token.ThrowIfCancellationRequested();
        Entry entry;bool reused;
        lock(sync) {
            if(stopped)throw new InvalidOperationException("程序正在退出，不能再打开应用。");
            reused=entries.TryGetValue((request.Serial,request.PackageName),out entry!);
            if(reused && entry.Request.UseVirtualCamera!=request.UseVirtualCamera)
                throw new InvalidOperationException("此应用已用另一种模式打开，请先在运行中页面关闭，再切换模式。");
            if(reused && entry.State is AppWindowState.Closing or AppWindowState.CloseFailed)
                throw new InvalidOperationException("上一个应用窗口尚未关闭，请在运行中页面完成关闭后重试。");
            if(!reused) {entry=new(request);entries.Add((request.Serial,request.PackageName),entry);}
        }
        if(!reused) {Notify();_ = StartAsync(entry,token);}
        await entry.Ready.Task.WaitAsync(token).ConfigureAwait(false);
        IAppWindowProcess process;AppWindowInfo info;
        lock(sync) {
            if(entry.State!=AppWindowState.Running)throw new InvalidOperationException("应用窗口正在关闭，请稍后重新打开。");
            process=entry.Process is {HasExited:false} active?active:throw new IOException("应用窗口已经结束，请重新打开。");
            info=Info(entry);
        }
        return new(info,reused,process.Focus());
    }
    public bool Focus(Guid id) {
        IAppWindowProcess? process;
        lock(sync)process=entries.Values.FirstOrDefault(entry=>entry.Id==id)?.Process;
        return process is {HasExited:false} && process.Focus();
    }
    public async Task CloseAsync(Guid id) {
        Entry? entry;lock(sync)entry=entries.Values.FirstOrDefault(value=>value.Id==id);
        if(entry==null)return;
        entry.CancelStart.Cancel();
        await entry.StartCompleted.Task.ConfigureAwait(false);
        await CloseEntryAsync(entry).ConfigureAwait(false);
    }
    public async Task StopAsync() {
        Entry[] pending;
        lock(sync){stopped=true;pending=entries.Values.ToArray();}
        shutdown.Cancel();
        await Task.WhenAll(pending.Select(entry=>entry.StartCompleted.Task)).ConfigureAwait(false);
        lock(sync)pending=entries.Values.ToArray();
        await Task.WhenAll(pending.Select(CloseEntryAsync)).ConfigureAwait(false);
    }
    private async Task StartAsync(Entry entry,CancellationToken caller) {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token,caller,entry.CancelStart.Token);
        deadline.CancelAfter(startupTimeout??TimeSpan.FromSeconds(entry.Request.UseVirtualCamera ? 90 : 30));
        try {
            deadline.Token.ThrowIfCancellationRequested();
            var process=host.Start(entry.Request);
            lock(sync)entry.Process=process;
            _ = WatchExitAsync(entry,process);
            await process.WaitForReadyAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            lock(sync)entry.State=AppWindowState.Running;
            entry.Ready.TrySetResult(Info(entry));
            Notify();
        } catch(Exception error) {
            if(error is OperationCanceledException && !shutdown.IsCancellationRequested && !caller.IsCancellationRequested && !entry.CancelStart.IsCancellationRequested)
                error=new TimeoutException("应用窗口启动超时，请重试。");
            entry.Ready.TrySetException(error);
            try {await CloseEntryAsync(entry).ConfigureAwait(false);}
            catch(Exception cleanup) {ReportProblem($"{entry.Request.Name} 启动后未能清理：{cleanup.Message}");}
        } finally {
            entry.Ready.TrySetException(new IOException("应用窗口未完成启动，请重试。"));
            entry.StartCompleted.TrySetResult();
        }
    }
    private Task CloseEntryAsync(Entry entry) {
        TaskCompletionSource? completion=null;Task work;
        lock(sync) {
            if(entry.CloseWork is null || entry.CloseWork.IsFaulted || entry.CloseWork.IsCanceled) {
                completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
                entry.CloseWork=completion.Task;
                entry.State=AppWindowState.Closing;
            }
            work=entry.CloseWork;
        }
        // Reserve the task under the lock, but invoke hosts and subscribers only after releasing it.
        if(completion!=null)_=CloseCoreAsync(entry,completion);
        return work;
    }
    private async Task CloseCoreAsync(Entry entry,TaskCompletionSource completion) {
        try {
            Notify();
            if(entry.Process is { } process) {
                await process.CloseAsync().ConfigureAwait(false);
                if(!process.HasExited)throw new IOException("窗口进程仍未退出，请重试关闭。");
                process.Dispose();
            }
            Remove(entry);
            completion.TrySetResult();
        } catch(Exception error) {
            lock(sync)entry.State=AppWindowState.CloseFailed;
            Notify();completion.TrySetException(error);
        }
    }
    private async Task WatchExitAsync(Entry entry,IAppWindowProcess process) {
        try {
            await process.WaitForExitAsync().ConfigureAwait(false);
            AppWindowState state;lock(sync)state=entry.State;
            if(process.ExitCode!=0 && state==AppWindowState.Running)ReportProblem($"{entry.Request.Name} 的窗口已异常结束（{process.ExitCode}）。请检查设备连接后重新打开。");
        } catch(Exception error) {ReportProblem($"窗口状态读取失败：{error.Message}");}
        finally {
            Remove(entry);
            try {process.Dispose();}catch(Exception error) {ReportObserverFailure(error);}
        }
    }
    private void Remove(Entry entry) {
        lock(sync) {
            var key=(entry.Request.Serial,entry.Request.PackageName);
            if(entries.TryGetValue(key,out var current) && ReferenceEquals(current,entry))entries.Remove(key);
        }
        Notify();
    }
    private static AppWindowInfo Info(Entry entry)=>new(entry.Id,entry.Request,entry.State,entry.Process?.DisplayId);
    private void Notify() {
        foreach(var handler in Changed?.GetInvocationList()??[]) {
            try {((EventHandler)handler)(this,EventArgs.Empty);}
            catch(Exception error) {ReportObserverFailure(error);}
        }
    }
    private void ReportProblem(string message) {
        foreach(var handler in Problem?.GetInvocationList()??[]) {
            try {((Action<string>)handler)(message);}
            catch(Exception error) {ReportObserverFailure(error);}
        }
    }
    private void ReportObserverFailure(Exception error) {
        try {observerFailure?.Invoke(error);}catch(Exception) { /* Diagnostics must not alter process ownership. */ }
    }
}
