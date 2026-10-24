namespace VirCre.Presentation;

public sealed record ShutdownWork(string Name,Func<Task> Stop);
public sealed record ShutdownFailure(string Name,Exception Error);
public sealed record ShutdownResult(bool CanClose,IReadOnlyList<ShutdownFailure> Failures);

/// <summary>Coordinates the UI-thread close attempt while preserving ownership of live app windows.</summary>
public sealed class ShutdownCoordinator(TimeSpan? timeout=null, IShellText? text=null) {
    private readonly IShellText wording=text??ShellText.Default;
    public bool HasStarted {get;private set;}
    public bool IsStopping {get;private set;}
    private Task<ShutdownResult>? attempt;
    public TimeSpan WaitLimit {get;}=timeout is { } value && value>TimeSpan.Zero?value:TimeSpan.FromSeconds(15);
    private (string Name,Task<ShutdownFailure?> Completion)[]? backgroundTasks;
    private Task<ShutdownFailure?>? windowCompletion;
    public Task<ShutdownResult> TryStopAsync(IReadOnlyList<ShutdownWork> background,Func<Task> stopWindows,Func<bool> hasWindows,Action<bool> setInteraction) {
        if(attempt is {IsCompleted:false})return attempt;
        HasStarted=true;IsStopping=true;
        var completion=new TaskCompletionSource<ShutdownResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        attempt=completion.Task;
        _=StopCoreAsync(background,stopWindows,hasWindows,setInteraction,completion);
        return attempt;
    }
    private async Task StopCoreAsync(IReadOnlyList<ShutdownWork> background,Func<Task> stopWindows,Func<bool> hasWindows,Action<bool> setInteraction,TaskCompletionSource<ShutdownResult> completion) {
        ShutdownResult? result=null;Exception? unexpected=null;
        try {
            setInteraction(false);
            // Stop accepting work once. Historical task faults remain observable but cannot poison retries.
            backgroundTasks??=background.Select(work=>(Name:work.Name,Completion:ObserveAsync(work))).ToArray();
            if(windowCompletion is not {IsCompleted:false})windowCompletion=ObserveAsync(new("app-windows",stopWindows));
            var tracked=backgroundTasks.Append((Name:"app-windows",Completion:windowCompletion)).ToArray();
            try {await Task.WhenAll(tracked.Select(work=>work.Completion)).WaitAsync(WaitLimit);}
            catch(TimeoutException) { /* Work retains ownership and may finish before the next close attempt. */ }
            var failures=new List<ShutdownFailure>();
            var pending=false;
            foreach(var work in tracked) {
                if(!work.Completion.IsCompleted) {
                    pending=true;
                    failures.Add(new(work.Name,new TimeoutException(wording.Format(TextKeys.ShutdownTimeout, work.Name, WaitLimit.TotalSeconds))));
                } else if(await work.Completion is { } failure)failures.Add(failure);
            }
            result=new(!pending && !hasWindows(),failures);
        } catch(Exception error) {unexpected=error;}
        finally {
            IsStopping=false;
            try {setInteraction(true);} catch(Exception error) {unexpected??=error;}
        }
        if(unexpected!=null)completion.TrySetException(unexpected);
        else completion.TrySetResult(result!);
    }
    private static async Task<ShutdownFailure?> ObserveAsync(ShutdownWork work) {
        try {await work.Stop();return null;}
        catch(OperationCanceledException) {return null;}
        catch(Exception error) {return new(work.Name,error);}
    }
}
