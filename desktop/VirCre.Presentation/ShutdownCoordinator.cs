namespace VirCre.Presentation;

public sealed record ShutdownWork(string Name,Func<Task> Stop);
public sealed record ShutdownFailure(string Name,Exception Error);
public sealed record ShutdownResult(bool CanClose,IReadOnlyList<ShutdownFailure> Failures);

/// <summary>Coordinates the UI-thread close attempt while preserving ownership of live app windows.</summary>
public sealed class ShutdownCoordinator {
    public bool HasStarted {get;private set;}
    public bool IsStopping {get;private set;}
    private Task<ShutdownResult>? attempt;
    private Task<ShutdownFailure[]>? backgroundCompletion;
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
            backgroundCompletion??=ObserveAllAsync(background);
            var windowCompletion=ObserveAsync(new("app-windows",stopWindows));
            var failures=(await backgroundCompletion).ToList();
            if(await windowCompletion is { } windowFailure)failures.Add(windowFailure);
            result=new(!hasWindows(),failures);
        } catch(Exception error) {unexpected=error;}
        finally {
            IsStopping=false;
            try {setInteraction(true);} catch(Exception error) {unexpected??=error;}
        }
        if(unexpected!=null)completion.TrySetException(unexpected);
        else completion.TrySetResult(result!);
    }
    private static async Task<ShutdownFailure[]> ObserveAllAsync(IReadOnlyList<ShutdownWork> work) {
        var failures=await Task.WhenAll(work.Select(ObserveAsync));
        return failures.OfType<ShutdownFailure>().ToArray();
    }
    private static async Task<ShutdownFailure?> ObserveAsync(ShutdownWork work) {
        try {await work.Stop();return null;}
        catch(OperationCanceledException) {return null;}
        catch(Exception error) {return new(work.Name,error);}
    }
}
