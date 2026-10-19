using VirCre.Launcher;

try {
var request = new AppLaunchRequest("phone", "Phone", "com.android.settings", "设置");
Check(ScrcpyWindowHost.ParseDisplayId("[server] INFO: New display: 1080x1920/420 (id=39)")==39,"display ID parsing never mistakes width for ID");
var arguments = ScrcpyWindowHost.Arguments(request);
Check(arguments.Contains("phone") && arguments.Contains("--start-app=com.android.settings") && arguments.Contains("--new-display=1440x2560/480") && !arguments.Any(a=>a.Contains("--start-app=+")), "exact device, package and larger new display without force-stop");
Check(arguments.Contains("--window-width=720") && arguments.Contains("--window-height=1280"),"app windows start at a larger 720 by 1280 size");
try {ScrcpyWindowHost.Arguments(request with {PackageName="+com.bad"});throw new Exception("Invalid package accepted");}catch(ArgumentException){}
var host = new FakeHost();
var manager = new AppWindowManager(host);
var first = manager.OpenAsync(request);
var duplicate = manager.OpenAsync(request);
Check(host.StartCount==1 && manager.Windows.Single().State==AppWindowState.Starting, "concurrent opens share one startup");
host.Processes[0].Ready.SetResult();
var firstResult = await first;
var reusedResult = await duplicate;
Check(firstResult.Window.Id==reusedResult.Window.Id && reusedResult.Reused, "duplicate caller receives the same window");
await manager.OpenAsync(request);
Check(host.StartCount==1 && host.Processes[0].FocusCount>=1, "running app is focused, never relaunched");
var second = manager.OpenAsync(request with {PackageName="com.other"});
host.Processes[1].Ready.SetResult();await second;
Check(manager.Windows.Count==2, "different apps have independent windows");
host.Processes[0].ExternalExit();
await Until(()=>manager.Windows.Count==1);
Check(manager.Windows.Single().Request.PackageName=="com.other", "manual exit removes only the owned entry");
await manager.StopAsync();
Check(host.Processes[1].CloseCount==1 && manager.Windows.Count==0, "shutdown closes and awaits owned windows");

var retryHost=new FakeHost();var retryManager=new AppWindowManager(retryHost);
var failed=retryManager.OpenAsync(request);retryHost.Processes[0].Ready.SetException(new IOException("start failed"));
try{await failed;throw new Exception("Start failure hidden");}catch(IOException){}
Check(retryHost.Processes[0].CloseCount==1 && retryManager.Windows.Count==0,"failed startup is cleaned");
var retry=retryManager.OpenAsync(request);retryHost.Processes[1].Ready.SetResult();await retry;
Check(retryHost.StartCount==2,"failed launch can be retried");await retryManager.StopAsync();

var cancelHost=new FakeHost();var cancelManager=new AppWindowManager(cancelHost);
var pending=cancelManager.OpenAsync(request);
await cancelManager.StopAsync();
try{await pending;throw new Exception("Stopped launch returned success");}catch(OperationCanceledException){}
Check(cancelHost.Processes[0].CloseCount==1 && cancelManager.Windows.Count==0,"shutdown cancels startup and waits for cleanup");
var timeoutHost=new FakeHost();var timeoutManager=new AppWindowManager(timeoutHost,TimeSpan.FromMilliseconds(50));
try{await timeoutManager.OpenAsync(request);throw new Exception("Startup deadline ignored");}catch(TimeoutException){}
Check(timeoutHost.Processes[0].CloseCount==1 && timeoutManager.Windows.Count==0,"startup timeout cleans the reserved process");
await timeoutManager.StopAsync();
var closeHost=new FakeHost();var closeManager=new AppWindowManager(closeHost);
var closeOpen=closeManager.OpenAsync(request);closeHost.Processes[0].Ready.SetResult();var closeWindow=await closeOpen;
closeHost.Processes[0].CloseFailures=1;
try{await closeManager.CloseAsync(closeWindow.Window.Id);throw new Exception("Close failure hidden");}catch(IOException){}
Check(closeManager.Windows.Count==1 && closeManager.Windows[0].State==AppWindowState.CloseFailed,"failed close remains owned and visible");
await closeManager.StopAsync();
Check(closeHost.Processes[0].CloseCount==2 && closeManager.Windows.Count==0,"shutdown retries failed cleanup");
var orphanHost=new FakeHost();var orphanManager=new AppWindowManager(orphanHost);
var orphanOpen=orphanManager.OpenAsync(request);orphanHost.Processes[0].CloseFailures=1;orphanHost.Processes[0].Ready.SetException(new IOException("startup failed"));
try{await orphanOpen;throw new Exception("Expected startup failure");}catch(IOException){}
Check(orphanManager.Windows.Single().State==AppWindowState.CloseFailed,"failed startup retains a live process whose cleanup failed");
orphanHost.Processes[0].ExternalExit();
await Until(()=>orphanManager.Windows.Count==0);
var recovered=orphanManager.OpenAsync(request);orphanHost.Processes[1].Ready.SetResult();await recovered;await orphanManager.StopAsync();
Check(orphanHost.StartCount==2,"external exit after failed startup cleanup permits a fresh launch");
var closingHost=new FakeHost();var closingManager=new AppWindowManager(closingHost);
var openBeforeClose=closingManager.OpenAsync(request);closingHost.Processes[0].Ready.SetResult();var beforeClose=await openBeforeClose;
closingHost.Processes[0].CloseRelease=new(TaskCreationOptions.RunContinuationsAsynchronously);
var closeInProgress=closingManager.CloseAsync(beforeClose.Window.Id);
try {
    try{await closingManager.OpenAsync(request);throw new Exception("A closing window was falsely reported as successfully reused");}
    catch(InvalidOperationException){Console.WriteLine("PASS: opening while closing reports the transition, never false reuse");}
} finally {closingHost.Processes[0].CloseRelease!.SetResult();await closeInProgress;await closingManager.StopAsync();}
Console.WriteLine("PASS: window lifecycle checks");

if((args.Length==2 || args.Length==4 && args[2]=="--runtime-root") && args[0]=="--device") {
    var tools=new BundledTools(args.Length==4?args[3]:Path.GetFullPath("out/desktop-release"));
    var serial=args[1];var commands=new CommandRunner(tools);var directory=new PhoneCatalog(commands);
    var apps=await directory.GetAppsAsync(serial,CancellationToken.None);
    var calculator=apps.FirstOrDefault(a=>a.Name=="计算器")??apps.FirstOrDefault(a=>a.PackageName.Contains("calculator",StringComparison.OrdinalIgnoreCase))??throw new Exception("No calculator app available for the live test");
    var live=new AppWindowManager(new ScrcpyWindowHost(tools));
    try {
        var one=await live.OpenAsync(new(serial,"V2458A","com.android.settings","设置"));
        var two=await live.OpenAsync(new(serial,"V2458A",calculator.PackageName,calculator.Name));
        Check(live.Windows.Count==2,"LIVE: two independent Windows windows");
        var again=await live.OpenAsync(new(serial,"V2458A","com.android.settings","设置"));
        Check(again.Reused && again.Window.Id==one.Window.Id && live.Windows.Count==2,"LIVE: reopening reuses the Settings window");
        await Task.Delay(1000);
        var activity=await commands.RunAsync("adb",["-s",serial,"shell","dumpsys","activity","activities"],TimeSpan.FromSeconds(10),CancellationToken.None);
        Check(activity.ExitCode==0,"LIVE: Android activity metadata is readable");
        var displays=System.Text.RegularExpressions.Regex.Matches(activity.Output,@"(?ms)^\s*Display #(?<id>\d+).*?(?=^\s*Display #|\z)");
        var packages=new[]{"com.android.settings",calculator.PackageName};var ids=new List<int>();
        foreach(var package in packages) {
            var display=displays.Cast<System.Text.RegularExpressions.Match>().FirstOrDefault(m=>m.Groups["id"].Value!="0" && m.Value.Contains(package,StringComparison.Ordinal));
            Check(display!=null,"LIVE: "+package+" runs on a non-default display");
            ids.Add(int.Parse(display!.Groups["id"].Value));
        }
        Check(ids.Distinct().Count()==2,"LIVE: Android app displays are distinct");
        Check(live.Windows.All(w=>w.DisplayId.HasValue) && ids.ToHashSet().SetEquals(live.Windows.Select(w=>w.DisplayId!.Value)),"LIVE: reported display IDs match Android activity metadata");
        Console.WriteLine("LIVE: displayIds="+string.Join(',',ids)+"; reported="+string.Join(',',live.Windows.Select(w=>w.DisplayId?.ToString()??"pending")));
        await live.CloseAsync(one.Window.Id);
        Check(live.Windows.Count==1,"LIVE: closing one window preserves the other");
    } finally {await live.StopAsync();}
    Check(live.Windows.Count==0,"LIVE: shutdown released all owned window processes");
}
} catch(Exception error) { Console.Error.WriteLine(error); Environment.ExitCode=1; }

static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
static async Task Until(Func<bool> condition){for(int i=0;i<100;i++){if(condition())return;await Task.Delay(10);}throw new Exception("State did not converge");}
sealed class FakeHost:IAppWindowHost {
 public List<FakeWindow> Processes {get;}=[];
 public int StartCount=>Processes.Count;
 public IAppWindowProcess Start(AppLaunchRequest request){var process=new FakeWindow();Processes.Add(process);return process;}
}
sealed class FakeWindow:IAppWindowProcess {
 public TaskCompletionSource Ready {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
 private readonly TaskCompletionSource exited=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public bool HasExited=>exited.Task.IsCompleted;
 public int? DisplayId=>42;
 public int ExitCode=>0;
 public int FocusCount {get;private set;}
 public int CloseCount {get;private set;}
 public int CloseFailures {get;set;}
 public TaskCompletionSource? CloseRelease {get;set;}
 public Task WaitForReadyAsync(CancellationToken token)=>Ready.Task.WaitAsync(token);
 public Task WaitForExitAsync()=>exited.Task;
 public bool Focus(){FocusCount++;return true;}
 public async Task CloseAsync(){CloseCount++;if(CloseFailures-- > 0)throw new IOException("close failed");if(CloseRelease!=null)await CloseRelease.Task;exited.TrySetResult();}
 public void ExternalExit()=>exited.TrySetResult();
 public void Dispose(){}
}
