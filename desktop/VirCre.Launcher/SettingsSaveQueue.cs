namespace VirCre.Launcher;

/// <summary>
/// Serializes settings writes so the last accepted snapshot wins. Each caller keeps
/// its own failure; a failed write never skips the snapshots queued behind it.
/// </summary>
public sealed class SettingsSaveQueue(Action<UserSettings> save) {
    private readonly object sync=new();
    private readonly List<Task> pending=[];
    private Task tail=Task.CompletedTask;
    private bool stopped;
    public Task EnqueueAsync(UserSettings settings) {
        lock(sync) {
            pending.RemoveAll(task=>task.IsCompleted);
            if(stopped)return Task.FromException(new InvalidOperationException("设置保存队列已停止。"));
            var work=SaveAfterAsync(tail,settings);
            tail=work;
            pending.Add(work);
            return work;
        }
    }
    public async Task StopAsync() {
        Task[] accepted;
        lock(sync) {
            stopped=true;
            pending.RemoveAll(task=>task.IsCompleted);
            accepted=pending.ToArray();
        }
        try {await Task.WhenAll(accepted).ConfigureAwait(false);}
        catch(Exception) { /* The caller that enqueued a failed write observes the error. */ }
    }
    private async Task SaveAfterAsync(Task previous,UserSettings settings) {
        try {await previous.ConfigureAwait(false);}
        catch(Exception) { /* A failed write must not drop later snapshots. */ }
        await Task.Run(()=>save(settings)).ConfigureAwait(false);
    }
}
