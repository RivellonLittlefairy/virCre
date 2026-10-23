using VirCre.Launcher;
using System.Diagnostics;

if(args.Length == 1 && args[0] == "--child-wait") {await Task.Delay(30000);return;}
if(args is ["--runtime-probe",var applicationRoot]) {
    var runtime=new CommandRunner(new BundledTools(applicationRoot));
    var oldPath=Environment.GetEnvironmentVariable("PATH");
    var oldAdb=Environment.GetEnvironmentVariable("ADB");
    var oldServer=Environment.GetEnvironmentVariable("SCRCPY_SERVER_PATH");
    try {
        Environment.SetEnvironmentVariable("PATH","");
        Environment.SetEnvironmentVariable("ADB","missing-external-adb.exe");
        Environment.SetEnvironmentVariable("SCRCPY_SERVER_PATH","missing-external-server");
        var version=await runtime.RunAsync("scrcpy",["--version"],TimeSpan.FromSeconds(15),default);
        Check(version.ExitCode==0 && (version.Output+version.Error).Contains("scrcpy 5.0.1"),"packaged scrcpy runs with an empty PATH");
        var adb=await runtime.RunAsync("adb",["version"],TimeSpan.FromSeconds(15),default);
        Check(adb.ExitCode==0 && adb.Output.Contains("Android Debug Bridge"),"packaged ADB runs with an empty PATH");
        return;
    } finally {Environment.SetEnvironmentVariable("PATH",oldPath);Environment.SetEnvironmentVariable("ADB",oldAdb);Environment.SetEnvironmentVariable("SCRCPY_SERVER_PATH",oldServer);}
}

var devices = PhoneCatalog.ParseDevices("List of devices attached\nphone.one device product:p model:V2458A device:p\nphone.two unauthorized\nphone.three offline\n");
Check(devices.Count == 3 && devices[0].Model == "V2458A" && devices[1].State == DeviceState.Unauthorized && devices[2].State == DeviceState.Offline, "device authorization states");
var listing = "[server] INFO: List of apps:\n * 设置 com.android.settings\n - A label-with spaces 应用 com.example.test\n - A label-with spaces 应用 com.example.test\n";
var apps = PhoneCatalog.ParseApps(listing);
Check(apps.Count == 2 && apps.Single(a => a.PackageName == "com.example.test").Name == "A label-with spaces 应用" && apps.Single(a => a.IsSystem).Name == "设置", "Unicode labels, spaces, flags and deduplication");
Reject(() => PhoneCatalog.ParseApps("[server] ERROR: no list"), "missing list header");
Reject(() => PhoneCatalog.ParseApps("List of apps:\n - Broken app ???"), "malformed app must not silently disappear");
Check(PhoneCatalog.ParseApps("List of apps:\n").Count == 0, "valid empty list");
Check(PhoneCatalog.ParseApps("* daemon not running; starting now\n" + listing).Count == 2, "ADB startup noise before list header");
var runner = new FakeRunner(new CommandResult(0, listing, ""));
var catalog = new PhoneCatalog(runner);
await catalog.GetAppsAsync("phone.one", CancellationToken.None);
Check(runner.File == "scrcpy" && runner.Arguments.SequenceEqual(new[]{"-s", "phone.one", "--list-apps"}) && runner.Timeout == TimeSpan.FromSeconds(45), "explicit serial and bounded listing");
runner.Result = new CommandResult(1, listing, "failed");
try { await catalog.GetAppsAsync("phone.one", CancellationToken.None); throw new Exception("Nonzero result was accepted"); } catch(IOException) { }
Console.WriteLine("PASS: nonzero exit never publishes partial apps");
try { await catalog.GetAppsAsync("bad serial", CancellationToken.None); throw new Exception("Invalid serial accepted"); } catch(ArgumentException) { }
Console.WriteLine("PASS: serial validation");
var bounded = new CommandRunner();
try {
    await bounded.RunAsync("scrcpy",["--version"],TimeSpan.FromSeconds(10),CancellationToken.None);
    throw new Exception("A missing bundled scrcpy must never silently use the system PATH version.");
} catch(FileNotFoundException error) {
    Check(error.FileName==Path.Combine(AppContext.BaseDirectory,"runtime","scrcpy","scrcpy.exe"),"missing bundled scrcpy never falls back to PATH");
}
var toolsRoot=Path.Combine(AppContext.BaseDirectory,"runtime-test-"+Guid.NewGuid().ToString("N"));
try {
    var files=new[]{"scrcpy.exe","scrcpy-server","adb.exe","AdbWinApi.dll","AdbWinUsbApi.dll","SDL3.dll","avcodec-63.dll","avformat-63.dll","avutil-61.dll","swresample-7.dll","libusb-1.0.dll"};
    var bundledDirectory=Path.Combine(toolsRoot,"runtime","scrcpy");Directory.CreateDirectory(bundledDirectory);
    foreach(var file in files) File.WriteAllText(Path.Combine(bundledDirectory,file),"fixture");
    var tools=new BundledTools(toolsRoot);
    var start=tools.CreateStartInfo("scrcpy");
    Check(start.FileName==Path.Combine(bundledDirectory,"scrcpy.exe"),"scrcpy executable is resolved under the application directory");
    Check(start.Environment["ADB"]==Path.Combine(bundledDirectory,"adb.exe") && start.Environment["SCRCPY_SERVER_PATH"]==Path.Combine(bundledDirectory,"scrcpy-server"),"child process pins its matching server and ADB without changing global environment");
    Check(tools.CreateStartInfo(Environment.ProcessPath!).FileName==Environment.ProcessPath,"explicit non-tool programs keep their original path");
    File.Delete(Path.Combine(bundledDirectory,"SDL3.dll"));
    try {tools.CreateStartInfo("scrcpy");throw new Exception("Incomplete bundle accepted");}
    catch(FileNotFoundException error) {Check(error.FileName==Path.Combine(bundledDirectory,"SDL3.dll"),"missing runtime DLL is reported before starting a process");}
} finally {if(Directory.Exists(toolsRoot))Directory.Delete(toolsRoot,true);}
var timer = Stopwatch.StartNew();
try {await bounded.RunAsync(Environment.ProcessPath!, ["--child-wait"], TimeSpan.FromMilliseconds(200), CancellationToken.None);throw new Exception("Timeout accepted");}
catch(TimeoutException) {Check(timer.Elapsed < TimeSpan.FromSeconds(4), "actual child process is bounded by timeout");}
using(var cancel = new CancellationTokenSource(200)) {
    try {await bounded.RunAsync(Environment.ProcessPath!, ["--child-wait"], TimeSpan.FromSeconds(20), cancel.Token);throw new Exception("Cancellation accepted");}
    catch(OperationCanceledException) {Console.WriteLine("PASS: actual child process responds to cancellation");}
}

if((args.Length==2 || args.Length==4 && args[2]=="--runtime-root") && args[0] == "--device") {
    var live = new PhoneCatalog(new CommandRunner(new BundledTools(args.Length==4?args[3]:Path.GetFullPath("out/desktop-release"))));
    var liveDevices = await live.GetDevicesAsync(CancellationToken.None);
    Check(liveDevices.Any(d => d.Serial == args[1] && d.State == DeviceState.Online), "integration device is online");
    var liveApps = await live.GetAppsAsync(args[1], CancellationToken.None);
    Check(liveApps.Any(a => a.PackageName == "com.android.settings"), "integration includes exact Settings package");
    Console.WriteLine($"LIVE: total={liveApps.Count}; system={liveApps.Count(a=>a.IsSystem)}; user={liveApps.Count(a=>!a.IsSystem)}");
}
await AgentCatalogChecks.RunAsync();
Console.WriteLine("PASS: launcher catalog checks");
static void Check(bool value,string name) {if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
static void Reject(Action action,string name) {try{action();}catch(InvalidDataException){Console.WriteLine("PASS: "+name);return;}throw new Exception(name);}
sealed class FakeRunner(CommandResult result) : ICommandRunner {
    public CommandResult Result {get;set;} = result;
    public string? File {get;private set;}
    public IReadOnlyList<string> Arguments {get;private set;} = [];
    public TimeSpan Timeout {get;private set;}
    public Task<CommandResult> RunAsync(string file,IReadOnlyList<string> arguments,TimeSpan timeout,CancellationToken token) {
        File=file;Arguments=arguments;Timeout=timeout;return Task.FromResult(Result);
    }
}
