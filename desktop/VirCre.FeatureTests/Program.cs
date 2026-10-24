using VirCre.Launcher;
using System.Runtime.InteropServices;

if(args is ["--diagnose-io"]) {
    Console.WriteLine("runtime="+typeof(object).Assembly.Location);
    Console.WriteLine("executable="+Environment.ProcessPath);
    foreach(var parent in new[]{Path.GetTempPath(),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),AppContext.BaseDirectory}) {
        var directory=Path.Combine(parent,"vircre-io-probe-"+Guid.NewGuid().ToString("N"));
        try {Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"probe.txt"),"probe");Console.WriteLine("WRITE_OK="+parent);}
        catch(Exception error) {Console.WriteLine("WRITE_FAIL="+parent+";"+error.GetType().Name+";"+error.HResult.ToString("X"));}
        finally {if(File.Exists(Path.Combine(directory,"probe.txt")))File.Delete(Path.Combine(directory,"probe.txt"));if(Directory.Exists(directory))Directory.Delete(directory);}
    }
    await Task.Delay(15000);
    return;
}

var root = Path.Combine(AppContext.BaseDirectory, "vircre-features-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try {
    var ownership=new VirCre.Presentation.GestureOwnership();
    var previousGesture=ownership.Begin();var currentGesture=ownership.Begin();
    var releases=0;
    ownership.Release(previousGesture,()=>releases++);
    Check(releases==0 && ownership.IsCurrent(currentGesture),"late drag preparation cannot release a newer gesture");
    ownership.Release(currentGesture,()=>releases++);
    Check(releases==1 && !ownership.IsCurrent(currentGesture),"the owning gesture releases its capture exactly once");
    var unavailable=Path.Combine(root,"unavailable");File.WriteAllText(unavailable,"not a writable directory");
    var recovery=DataLocation.Resolve(unavailable,Path.Combine(root,"portable"));
    Check(recovery.UsedFallback && Directory.Exists(recovery.Path) && recovery.Warning!=null,"unwritable preferred data directory falls back to a writable location");
    var normalLocation=DataLocation.Resolve(Path.Combine(root,"normal"),Path.Combine(root,"unused"));
    Check(!normalLocation.UsedFallback && normalLocation.Warning==null,"normal writable AppData remains the preferred location");
    File.WriteAllText(Path.Combine(recovery.Path,"settings.json"),"{\"favorites\":[],\"theme\":\"Dark\",\"version\":1}");
    var restoredLocation=DataLocation.Resolve(Path.Combine(root,"restored-appdata"),recovery.Path);
    Check(restoredLocation.Path==recovery.Path,"saved portable preferences remain selected after AppData becomes writable again");
    var path = Path.Combine(root, "settings.json");
    var store = new SettingsStore(path);
    Check(store.Load().Settings.Favorites.Length == 0, "missing settings use defaults");
    store.Save(new(["com.example.中文", "com.android.settings"], "Dark"));
    var restored = new SettingsStore(path).Load();
    Check(restored.Warning == null && restored.Settings.Theme == "Dark" && restored.Settings.Favorites.Length == 2, "settings survive reopening with Unicode");
    File.WriteAllText(path, "{broken");
    Check(store.Load().Warning != null && File.ReadAllText(path) == "{broken", "damaged settings recover without destroying source");
    File.WriteAllText(path, "{\"version\":999,\"theme\":\"Dark\"}");
    Check(store.Load().Warning != null, "unknown settings version is reported");
    store.Save(new(["com.android.settings", "com.android.settings"], "unknown"));
    Check(store.Load().Settings.Favorites.Length == 1 && store.Load().Settings.Theme == "System", "settings normalize duplicates and theme");
    await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(() => store.Save(new(["com.app.n" + i], "Light")))));
    Check(store.Load().Warning == null && store.Load().Settings.Favorites.Length == 1 && !Directory.GetFiles(root, "*.tmp").Any(), "concurrent atomic saves leave valid file and no temp files");

    var request = new ShortcutRequest("phone.one", "hardware-one", "Vivo", "com.android.settings", "设置 & \"工具\"");
    var encoded = request.Encode();
    Check(ShortcutRequest.Decode(encoded) == request && encoded.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'), "shortcut payload round trips safely");
    Reject(() => ShortcutRequest.Decode("%%%"), "malformed shortcut arguments rejected");
    Reject(() => (request with {PackageName = "com.app;reboot"}).Encode(), "shortcut package cannot contain shell syntax");
    var online = new[] {new PhoneDevice("phone.other", "Other", DeviceState.Online), new PhoneDevice("phone.new", "Vivo", DeviceState.Online)};
    var matched = await request.ResolveAsync(online, (s, _) => Task.FromResult(s == "phone.new" ? "hardware-one" : "another"), default);
    Check(matched.Serial == "phone.new", "hardware identity survives wireless ADB address changes");
    await RejectAsync(() => request.ResolveAsync(online, (_, _) => Task.FromResult("another"), default), "never fall back to the wrong online phone");
    await RejectAsync(() => request.ResolveAsync([new("phone.one", "Vivo", DeviceState.Unauthorized)], (_, _) => Task.FromResult("hardware-one"), default), "unauthorized phone is not launched");
    var metadataRunner=new IdentityCatalogRunner();
    var catalogDevice=(await new PhoneCatalog(metadataRunner).GetDevicesAsync(default)).Single();
    Check(catalogDevice.HardwareId=="verified-phone-id","device discovery captures verified hardware identity for later shortcut creation");
    var cachedDevice=await (request with {HardwareId="verified-phone-id"}).ResolveAsync([catalogDevice],(_,_)=>throw new Exception("Fresh device metadata was ignored"),default);
    Check(cachedDevice.Serial=="phone.one","verified device metadata avoids redundant identity queries");
    await RejectAsync(() => (request with {HardwareId=""}).ResolveAsync([new("phone.one","Other phone",DeviceState.Online)],(_,_)=>Task.FromResult(""),default),"missing hardware identity never trusts a reused network address");
    var pruning=Path.Combine(root,"prune");Directory.CreateDirectory(pruning);
    for(var i=0;i<5;i++) {var old=Directory.CreateDirectory(Path.Combine(pruning,Guid.NewGuid().ToString("N"))); File.WriteAllText(Path.Combine(old.FullName,"icon.png"),"png");Directory.SetCreationTimeUtc(old.FullName,DateTime.UtcNow.AddHours(-10+i));}
    var unrelated=Directory.CreateDirectory(Path.Combine(pruning,"user-data"));File.WriteAllText(Path.Combine(unrelated.FullName,"keep.txt"),"keep");
    CacheMaintenance.Prune(pruning,2,TimeSpan.FromDays(7));
    Check(Directory.GetDirectories(pruning).Length==3 && File.Exists(Path.Combine(unrelated.FullName,"keep.txt")),"cache pruning retains two generations and ignores unrelated directories");

    var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");
    var line = System.Text.Json.JsonSerializer.Serialize(new {package = "com.android.settings", png = Convert.ToBase64String(png)});
    var icons = IconCatalog.ParseIcons(line + "\n{\"type\":\"complete\"}\n", ["com.android.settings"]);
    Check(icons.Single().Value.SequenceEqual(png), "icon protocol accepts bounded PNG");
    Reject(() => IconCatalog.ParseIcons(line, ["com.android.settings"]), "incomplete icon transfer rejected");
    Reject(() => IconCatalog.ParseIcons(line + "\n{\"type\":\"complete\"}", ["com.other.app"]), "unexpected packages cannot write cache entries");
    Reject(() => IconCatalog.ParseIcons("{\"package\":\"com.android.settings\",\"png\":\"AA==\"}\n{\"type\":\"complete\"}", ["com.android.settings"]), "non-PNG icon rejected");
    var ico = IconFile.FromPng(png);
    Check(ico[2] == 1 && BitConverter.ToInt32(ico, 18) == 22 && ico.AsSpan(22).SequenceEqual(png), "Windows icon contains real PNG with correct directory offset");
    var stubAgent=Path.Combine(root,"agent.jar");File.WriteAllText(stubAgent,"test agent");
    var fallbackRunner=new IconRunner("broken protocol");
    var fallback=new IconCatalog(new OneAppCatalog(),fallbackRunner,stubAgent,Path.Combine(root,"fallback"));
    var warned=false;fallback.Warning+=_=>warned=true;
    Check((await fallback.GetAppsAsync("phone.one",default)).Single().IconPath==null && warned && fallbackRunner.Cleaned,"broken icon protocol keeps real app directory and cleans remote helper");
    using(var cancellation=new CancellationTokenSource()) {
        var cancelledRunner=new IconRunner(line+"\n{\"type\":\"complete\"}",cancellation);
        var cancelled=new IconCatalog(new OneAppCatalog(),cancelledRunner,stubAgent,Path.Combine(root,"cancelled"));
        try {await cancelled.GetAppsAsync("phone.one",cancellation.Token);throw new Exception("Cancelled icons were published");}
        catch(OperationCanceledException) {Check(cancelledRunner.Cleaned,"icon cancellation still cleans remote helper with independent token");}
    }
    var successRunner=new IconRunner(line+"\n{\"type\":\"complete\"}");
    var isolated=new IconCatalog(new OneAppCatalog(),successRunner,stubAgent,Path.Combine(root,"separate"));
    var firstPhone=(await isolated.GetAppsAsync("phone.one",default)).Single().IconPath!;
    var secondPhone=(await isolated.GetAppsAsync("phone.two",default)).Single().IconPath!;
    Check(firstPhone!=secondPhone && File.Exists(firstPhone) && File.Exists(secondPhone),"same package on different phones has isolated icon cache");
    var saveOrder=new List<int>();
    var settingsQueue=new SettingsSaveQueue(settings=>{
        lock(saveOrder)saveOrder.Add(settings.CameraFrameRate);
        if(settings.CameraFrameRate==1)Thread.Sleep(50);
    });
    var firstSave=settingsQueue.EnqueueAsync(new([],CameraFrameRate:1));
    var secondSave=settingsQueue.EnqueueAsync(new([],CameraFrameRate:2));
    await Task.WhenAll(firstSave,secondSave);
    Check(saveOrder.SequenceEqual([1,2]),"settings writes keep the order they were accepted in");
    var failureWrites=new List<int>();
    var failingQueue=new SettingsSaveQueue(settings=>{
        if(settings.CameraFrameRate==3)throw new IOException("disk full");
        failureWrites.Add(settings.CameraFrameRate);
    });
    var failingSave=failingQueue.EnqueueAsync(new([],CameraFrameRate:3));
    var followingSave=failingQueue.EnqueueAsync(new([],CameraFrameRate:4));
    var saveFailureSeen=false;
    try {await failingSave;} catch(IOException) {saveFailureSeen=true;}
    await followingSave;
    Check(saveFailureSeen && failureWrites.SequenceEqual([4]),"one failed write is reported without blocking the next snapshot");
    var stoppingWriter=new SettingsSaveQueue(_=>Thread.Sleep(50));
    var stoppingWrite=stoppingWriter.EnqueueAsync(new([]));
    var stopping=stoppingWriter.StopAsync();
    Check(!stopping.IsCompleted,"stop waits for accepted settings writes");
    await stopping;await stoppingWrite;
    var rejectedAfterStop=false;
    try {await stoppingWriter.EnqueueAsync(new([]));}catch(InvalidOperationException) {rejectedAfterStop=true;}
    await stoppingWriter.StopAsync();
    Check(rejectedAfterStop,"a stopped queue rejects new snapshots and its second stop is harmless");
    Console.WriteLine("PASS: settings save queue serializes, isolates failures and drains on shutdown");

    var shortcutHost=new ShortcutHost();
    var shortcutWindows=new AppWindowManager(shortcutHost);
    var shortcutLaunchService=new AppLaunchService(shortcutWindows);
    var presentLauncher=new ShortcutLauncher(
        new DeviceCatalog([new PhoneDevice("10.0.0.9:5555","Vivo",DeviceState.Online,"hw-vivo",36)]),
        new GetPropRunner(),shortcutLaunchService,(serial,packageName,token)=>Task.FromResult(packageName=="com.present"));
    var shortcutPayload=new ShortcutRequest("192.168.1.2:5555","hw-vivo","Vivo","com.present","Present").Encode();
    var shortcutOpen=presentLauncher.OpenAsync(shortcutPayload,new([],CameraFrameRate:20),default);
    shortcutHost.Processes[0].Ready.SetResult();
    var shortcutResult=await shortcutOpen;
    Check(shortcutResult.Window.Request.Serial=="10.0.0.9:5555" && shortcutResult.Window.Request.CameraFrameRate==20,
        "a shortcut follows the hardware identity and applies the settings snapshot");
    await shortcutWindows.StopAsync();

    var reusedHost=new ShortcutHost();
    var reusedWindows=new AppWindowManager(reusedHost);
    var reusedLauncher=new ShortcutLauncher(
        new DeviceCatalog([new("192.168.1.2:5555","Old phone",DeviceState.Online,"hw-old",36),
            new("10.0.0.9:5555","Vivo",DeviceState.Online,"hw-vivo",36)]),
        new GetPropRunner(),new AppLaunchService(reusedWindows),(_,_,_)=>Task.FromResult(true));
    var reusedOpen=reusedLauncher.OpenAsync(shortcutPayload,new([]),default);
    reusedHost.Processes[0].Ready.SetResult();
    var reusedResult=await reusedOpen;
    Check(reusedResult.Window.Request.Serial=="10.0.0.9:5555","a reused network address never wins over the hardware identity");
    await reusedWindows.StopAsync();

    var lockedHost=new ShortcutHost();
    var lockedWindows=new AppWindowManager(lockedHost);
    var lockedLauncher=new ShortcutLauncher(
        new DeviceCatalog([new("10.0.0.9:5555","Vivo",DeviceState.Unauthorized,"hw-vivo",36)]),
        new GetPropRunner(),new AppLaunchService(lockedWindows),(_,_,_)=>Task.FromResult(true));
    var lockedRejected=false;
    try {await lockedLauncher.OpenAsync(shortcutPayload,new([]),default);}catch(IOException) {lockedRejected=true;}
    Check(lockedRejected && lockedHost.Processes.Count==0,"an unauthorized phone is never launched from a shortcut");
    await lockedWindows.StopAsync();

    var missingHost=new ShortcutHost();
    var missingWindows=new AppWindowManager(missingHost);
    var missingLauncher=new ShortcutLauncher(
        new DeviceCatalog([new("10.0.0.9:5555","Vivo",DeviceState.Online,"hw-vivo",36)]),
        new GetPropRunner(),new AppLaunchService(missingWindows),(_,_,_)=>Task.FromResult(false));
    var missingRejected=false;
    try {await missingLauncher.OpenAsync(shortcutPayload,new([]),default);}catch(IOException) {missingRejected=true;}
    Check(missingRejected && missingHost.Processes.Count==0,"a shortcut whose app was uninstalled is rejected before starting");
    await missingWindows.StopAsync();
    Console.WriteLine("PASS: shortcut launches run without the shell window and re-check the phone");

    if (args.Length is 3 or 5 && args[0] == "--device") {
        var liveCache=args.Length==5 && args[3]=="--cache-root"?Path.GetFullPath(args[4]):Path.Combine(root,"icons");
        var liveTools=new BundledTools(Path.GetFullPath("out/desktop-release"));
        var live = new IconCatalog(new PhoneCatalog(new CommandRunner(liveTools)), new CommandRunner(liveTools), Path.GetFullPath(args[2]), liveCache);
        live.Warning += message => Console.WriteLine("WARNING: " + message);
        var liveApps = await live.GetAppsAsync(args[1],default);
        Check(liveApps.Any(a => a.PackageName=="com.android.settings" && a.IconPath!=null),"live Settings icon retrieved without installation");
        foreach(var app in liveApps.Where(a => a.IconPath!=null)) IconFile.ValidatePng(File.ReadAllBytes(app.IconPath!));
        Check(liveApps.Where(a => a.IconPath!=null).Select(a=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(a.IconPath!)))).Distinct().Count()>5,"live application icons are distinct");
        Console.WriteLine($"LIVE: icons={liveApps.Count(a=>a.IconPath!=null)}/{liveApps.Count}");
    }
    Console.WriteLine("PASS: feature checks");
} finally {Directory.Delete(root, true);}
static void Check(bool value, string name) {if (!value) throw new Exception(name); Console.WriteLine("PASS: " + name);}
static void Reject(Action action, string name) {try {action();} catch (Exception e) when(e is ArgumentException or InvalidDataException or IOException) {Console.WriteLine("PASS: " + name);return;} throw new Exception(name);}
static async Task RejectAsync(Func<Task<PhoneDevice>> action, string name) {try {await action();} catch (IOException) {Console.WriteLine("PASS: " + name);return;} throw new Exception(name);}
sealed class OneAppCatalog : IPhoneCatalog {
    public Task<IReadOnlyList<PhoneDevice>> GetDevicesAsync(CancellationToken token)=>Task.FromResult<IReadOnlyList<PhoneDevice>>([]);
    public Task<IReadOnlyList<PhoneApp>> GetAppsAsync(string serial,CancellationToken token)=>Task.FromResult<IReadOnlyList<PhoneApp>>([new("设置","com.android.settings",true)]);
}
sealed class IconRunner(string output,CancellationTokenSource? cancellation=null) : ICommandRunner {
    public bool Cleaned {get;private set;}
    public Task<CommandResult> RunAsync(string file,IReadOnlyList<string> arguments,TimeSpan timeout,CancellationToken token) {
        if(arguments.Contains("rm")) {Cleaned=!token.IsCancellationRequested;return Task.FromResult(new CommandResult(0,"",""));}
        if(arguments.Contains("app_process")) {cancellation?.Cancel();token.ThrowIfCancellationRequested();return Task.FromResult(new CommandResult(0,output,""));}
        return Task.FromResult(new CommandResult(0,"",""));
    }
}
sealed class IdentityCatalogRunner : ICommandRunner {
    public Task<CommandResult> RunAsync(string file,IReadOnlyList<string> arguments,TimeSpan timeout,CancellationToken token)=>Task.FromResult(arguments.Contains("getprop")?
        new CommandResult(0,"verified-phone-id\n",""):new CommandResult(0,"List of devices attached\nphone.one device model:Phone\n",""));
}
sealed class DeviceCatalog(IReadOnlyList<PhoneDevice> devices) : IPhoneCatalog {
    public Task<IReadOnlyList<PhoneDevice>> GetDevicesAsync(CancellationToken token)=>Task.FromResult(devices);
    public Task<IReadOnlyList<PhoneApp>> GetAppsAsync(string serial,CancellationToken token)=>throw new NotSupportedException();
}
sealed class GetPropRunner : ICommandRunner {
    public Task<CommandResult> RunAsync(string file,IReadOnlyList<string> arguments,TimeSpan timeout,CancellationToken token)=>
        Task.FromResult(new CommandResult(0,"other-hardware\n",""));
}
sealed class ShortcutHost : IAppWindowHost {
    public List<ShortcutProcess> Processes {get;}=[];
    public IAppWindowProcess Start(AppLaunchRequest request){var process=new ShortcutProcess();Processes.Add(process);return process;}
}
sealed class ShortcutProcess : IAppWindowProcess {
    private readonly TaskCompletionSource exited=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Ready {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool HasExited=>exited.Task.IsCompleted;
    public int? DisplayId=>null;
    public int ExitCode=>0;
    public Task WaitForReadyAsync(CancellationToken token)=>Ready.Task.WaitAsync(token);
    public Task WaitForExitAsync()=>exited.Task;
    public bool Focus()=>true;
    public Task CloseAsync(){exited.TrySetResult();return Task.CompletedTask;}
    public void Dispose(){}
}
