using VirCre.Launcher;
using System.Diagnostics;

if(args.Length == 1 && args[0] == "--child-wait") {await Task.Delay(30000);return;}

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
var timer = Stopwatch.StartNew();
try {await bounded.RunAsync(Environment.ProcessPath!, ["--child-wait"], TimeSpan.FromMilliseconds(200), CancellationToken.None);throw new Exception("Timeout accepted");}
catch(TimeoutException) {Check(timer.Elapsed < TimeSpan.FromSeconds(4), "actual child process is bounded by timeout");}
using(var cancel = new CancellationTokenSource(200)) {
    try {await bounded.RunAsync(Environment.ProcessPath!, ["--child-wait"], TimeSpan.FromSeconds(20), cancel.Token);throw new Exception("Cancellation accepted");}
    catch(OperationCanceledException) {Console.WriteLine("PASS: actual child process responds to cancellation");}
}

if(args.Length == 2 && args[0] == "--device") {
    var live = new PhoneCatalog(new CommandRunner());
    var liveDevices = await live.GetDevicesAsync(CancellationToken.None);
    Check(liveDevices.Any(d => d.Serial == args[1] && d.State == DeviceState.Online), "integration device is online");
    var liveApps = await live.GetAppsAsync(args[1], CancellationToken.None);
    Check(liveApps.Any(a => a.PackageName == "com.android.settings"), "integration includes exact Settings package");
    Console.WriteLine($"LIVE: total={liveApps.Count}; system={liveApps.Count(a=>a.IsSystem)}; user={liveApps.Count(a=>!a.IsSystem)}");
}
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
