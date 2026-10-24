using VirCre.Presentation;

var checks = new List<(string Name, Action Check)> {
    ("shell starts with explicitly identified sample data", () => {
        var shell = new ShellViewModel(preview: true);
        Require(shell.IsPreview && shell.Apps.Count == 8, "Expected eight sample apps, never a connected phone");
        Require(shell.Apps.All(app => app.PackageName.StartsWith("demo.", StringComparison.Ordinal)), "Sample identities must not look installed");
    }),
    ("search matches Chinese names and case insensitive packages", () => {
        var shell = new ShellViewModel(preview: true);
        shell.Search(" 相册 ");
        Require(shell.Apps.Single().Name == "相册", "Trimmed Chinese search failed");
        shell.Search("DEMO.CALENDAR");
        Require(shell.Apps.Single().Name == "日历", "Package search failed");
        shell.Search("");
        Require(shell.Apps.Count == 8, "Clearing the query must restore the catalog");
    }),
    ("favorites are session scoped and immediately refilter", () => {
        var shell = new ShellViewModel(preview: true);
        shell.Navigate("favorites");
        Require(shell.IsEmpty && shell.EmptyTitle == "把常用应用放在这里", "Expected favorite empty state");
        shell.ToggleFavorite("demo.gallery");
        Require(shell.Apps.Single().IsFavorite, "Favorite did not appear");
        shell.ToggleFavorite("demo.gallery");
        Require(shell.IsEmpty, "Removing the last favorite must update the empty state");
        Require(!new ShellViewModel(preview: true).Apps.Any(app => app.IsFavorite), "Preview favorites must not persist");
    }),
    ("page changes clear search and preserve the right empty state", () => {
        var shell = new ShellViewModel(preview: true);
        shell.Search("does-not-exist");
        Require(shell.IsEmpty && shell.EmptyTitle == "没有找到匹配的应用", "Expected search empty state");
        shell.Navigate("running");
        Require(shell.Query == "" && shell.IsEmpty && shell.EmptyTitle == "还没有打开的应用", "Running state inherited a search or app card");
        shell.Navigate("apps");
        Require(shell.Apps.Count == 8 && !shell.IsEmpty, "Navigating back must restore the apps");
    }),
    ("observable state updates counts and unknown actions are harmless", () => {
        var shell = new ShellViewModel(preview: true);
        var notifications = new HashSet<string?>();
        shell.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);
        shell.Search("demo.notes");
        Require(shell.ResultCount == "1 个示例应用", "Count did not follow filtering");
        Require(notifications.Contains(nameof(ShellViewModel.IsEmpty)) && notifications.Contains(nameof(ShellViewModel.ResultCount)), "UI would not receive state changes");
        shell.ToggleFavorite("unknown");
        shell.Navigate("unknown");
        Require(shell.Page == "apps", "Unknown navigation changed the page");
    })
};

foreach (var (name, check) in checks) { check(); Console.WriteLine("PASS: " + name); }
Console.WriteLine($"PASS: {checks.Count} shell behavior checks");
var real = new ShellViewModel();
Require(!real.IsPreview && real.Apps.Count == 0 && !real.HasDevice, "Normal startup must never display sample apps");
var source = new ControlledCatalog();
using var controller = new CatalogController(source, real);
var oldLoad = controller.SelectAsync(new("old", "Old phone", VirCre.Launcher.DeviceState.Online));
var newLoad = controller.SelectAsync(new("new", "New phone", VirCre.Launcher.DeviceState.Online));
source.Pending["new"].SetResult([new("真实 中文应用", "com.real.app", false)]);
await newLoad;
source.Pending["old"].SetResult([new("Old app", "com.old.app", true)]);
await oldLoad;
Require(real.DeviceLabel == "New phone" && real.Apps.Single().Name == "真实 中文应用" && !real.IsLoading, "A stale completed request overwrote the selected phone");
Console.WriteLine("PASS: normal startup has no demo and late old loads cannot overwrite current device");
var failedLoad = controller.SelectAsync(new("bad", "Failed phone", VirCre.Launcher.DeviceState.Online));
source.Pending["bad"].SetException(new IOException("读取失败"));
await failedLoad;
Require(real.Apps.Count == 0 && !real.IsLoading && real.ErrorMessage == "读取失败", "Failed refresh retained old apps or loading state");
await controller.SelectAsync(new("locked", "Locked phone", VirCre.Launcher.DeviceState.Unauthorized));
Require(!source.Pending.ContainsKey("locked") && real.EmptyDescription.Contains("授权"), "Unauthorized device was queried");
await controller.SelectAsync(null);
Require(!real.HasDevice && real.Apps.Count == 0 && real.EmptyTitle == "连接你的手机", "Disconnect did not clear the directory");
Console.WriteLine("PASS: failed, unauthorized and disconnected states cannot display old apps");
var cancelledLoad = controller.SelectAsync(new("cancelled", "Cancelled", VirCre.Launcher.DeviceState.Online));
source.Pending["cancelled"].SetCanceled();
await cancelledLoad;
Require(!real.IsLoading && real.Apps.Count == 0, "Cancelled listing got stuck loading");
Console.WriteLine("PASS: cancelled listing ends loading without partial data");
foreach(var malformed in new[]{"missing app list header","List of apps:\n - Broken ???","List of apps:\n * Valid com.valid.app\n - Broken ???"}) {
    var malformedLoad=controller.SelectAsync(new("malformed","Malformed",VirCre.Launcher.DeviceState.Online));
    try {VirCre.Launcher.PhoneCatalog.ParseApps(malformed);throw new Exception("Malformed catalog was accepted");}
    catch(InvalidDataException error) {source.Pending["malformed"].SetException(error);}
    await malformedLoad;
    Require(!real.IsLoading && real.Apps.Count==0 && !string.IsNullOrEmpty(real.ErrorMessage),"A format failure escaped or published a partial app list");
    source.Pending.Remove("malformed");
}
var recoveryLoad=controller.SelectAsync(new("recovered","Recovered",VirCre.Launcher.DeviceState.Online));
source.Pending["recovered"].SetResult([new("Recovered app","com.recovered.app",false)]);
await recoveryLoad;
Require(real.Apps.Single().PackageName=="com.recovered.app" && real.ErrorMessage.Length==0,"Format failure prevented a later refresh");
Require(VirCre.Launcher.CatalogErrors.IsExpected(new InvalidDataException()) && !VirCre.Launcher.CatalogErrors.IsExpected(new InvalidOperationException()),"Device and app read boundaries must handle invalid data without hiding programmer errors");
try {VirCre.Launcher.PhoneCatalog.ParseDevices("unexpected adb output");throw new Exception("Malformed device list was accepted");}
catch(InvalidDataException error) {Require(VirCre.Launcher.CatalogErrors.IsExpected(error),"Device parser errors must be accepted by the shared UI error filter");}
Console.WriteLine("PASS: real parser failures are shown as recoverable errors and a later refresh succeeds");

var saved = new ShellViewModel(settings: new(["com.real.app","com.absent.app"],"Dark"));
using(var persistedController=new CatalogController(new ImmediateCatalog(),saved)) {
    await persistedController.SelectAsync(new("phone","Phone",VirCre.Launcher.DeviceState.Online));
    Require(saved.Apps.Single().IsFavorite && saved.Apps.Single().IconPath=="C:\\icons\\real.png", "Saved favorite or real icon was not restored");
    saved.Navigate("favorites");
    Require(saved.Apps.Count==1 && saved.ExportSettings().Favorites.Contains("com.absent.app"),"Uninstalled favorite was displayed or lost");
    saved.ToggleFavorite("com.real.app");
    Require(saved.IsEmpty && !saved.ExportSettings().Favorites.Contains("com.real.app"),"Removing favorite was not persisted");
    saved.SetTheme("Light");
    Require(saved.ExportSettings().Theme=="Light","Theme was not included in persistent settings");
}
Console.WriteLine("PASS: saved favorites, real icons and theme survive catalog replacement");

var preferenceModel = new ShellViewModel(settings: new([], "System"));
var preferenceNotifications = new List<string?>();
var preferenceSaves = 0;
string? themeSeen = null;
bool? cameraSeen = null;
int? frameRateSeen = null;
preferenceModel.PropertyChanged += (_, change) => {
    preferenceNotifications.Add(change.PropertyName);
    if (change.PropertyName == nameof(ShellViewModel.Theme)) themeSeen = preferenceModel.Theme;
    else if (change.PropertyName == nameof(ShellViewModel.VirtualCameraEnabled)) cameraSeen = preferenceModel.VirtualCameraEnabled;
    else if (change.PropertyName == nameof(ShellViewModel.CameraFrameRate)) frameRateSeen = preferenceModel.CameraFrameRate;
};
preferenceModel.SettingsChanged += () => preferenceSaves++;
preferenceModel.SetTheme("Dark");
preferenceModel.SetTheme("Dark");
Require(preferenceNotifications.Count == 1 && themeSeen == "Dark" && preferenceSaves == 1,
    "A theme change must notify once with the new value and save once");
preferenceModel.SetTheme("Sepia");
Require(preferenceModel.Theme == "System" && themeSeen == "System" && preferenceSaves == 2,
    "An unknown theme must normalize, notify and save");
preferenceModel.SetVirtualCameraEnabled(false);
preferenceModel.SetVirtualCameraEnabled(false);
Require(preferenceNotifications.Count == 3 && cameraSeen == false && preferenceSaves == 3,
    "A camera toggle must notify once without duplicating the save");
preferenceModel.SetCameraFrameRate(24);
preferenceModel.SetCameraFrameRate(24);
Require(preferenceNotifications.Count == 4 && frameRateSeen == 24 && preferenceSaves == 4,
    "A frame rate change must notify once without duplicating the save");
var rejectedFrameRate = false;
try { preferenceModel.SetCameraFrameRate(0); }
catch (ArgumentOutOfRangeException) { rejectedFrameRate = true; }
Require(rejectedFrameRate && preferenceModel.CameraFrameRate == 24 && preferenceSaves == 4,
    "An invalid frame rate must be rejected without notifying or saving");
Require(!preferenceNotifications.Contains(nameof(ShellViewModel.Page)), "Preference changes must not broadcast unrelated state");
Console.WriteLine("PASS: preference setters notify only real changes and save once each");

var guardModel = new ShellViewModel();
var guardDevice = new VirCre.Launcher.PhoneDevice("phone.guard","Guard",VirCre.Launcher.DeviceState.Online);
using (var guardController = new CatalogController(new ImmediateCatalog(), guardModel)) {
    await guardController.SelectAsync(guardDevice);
    var guardCard = guardModel.Apps.Single();
    Require(AppActionGuard.CanActOn(guardCard,guardDevice,guardModel,false,false),"A live card on the selected online phone must be actionable");
    Require(!AppActionGuard.CanActOn(guardCard,guardDevice,guardModel,true,false),"Scanning must block card actions");
    Require(!AppActionGuard.CanActOn(guardCard,guardDevice,guardModel,false,true),"Shutdown must block card actions");
    Require(!AppActionGuard.CanActOn(guardCard,guardDevice with {State=VirCre.Launcher.DeviceState.Offline},guardModel,false,false),"An offline phone must block its cards");
    Require(!AppActionGuard.CanActOn(guardCard,guardDevice with {State=VirCre.Launcher.DeviceState.Unauthorized},guardModel,false,false),"An unauthorized phone must block its cards");
    Require(!AppActionGuard.CanActOn(guardCard,guardDevice with {Serial="phone.other"},guardModel,false,false),"A card must not act on a different phone");
    var otherDevice = new VirCre.Launcher.PhoneDevice("phone.other","Other",VirCre.Launcher.DeviceState.Online);
    await guardController.SelectAsync(otherDevice);
    Require(!AppActionGuard.CanActOn(guardCard,otherDevice,guardModel,false,false),"A card from the previous phone must not act after switching");
}
var loadingModel = new ShellViewModel();
var loadingSource = new ControlledCatalog();
using (var loadingController = new CatalogController(loadingSource, loadingModel)) {
    var loadingDevice = new VirCre.Launcher.PhoneDevice("phone.loading","Loading",VirCre.Launcher.DeviceState.Online);
    var loading = loadingController.SelectAsync(loadingDevice);
    Require(loadingModel.IsLoading,"Fixture must keep the catalog loading");
    Require(!AppActionGuard.CanActOn(new AppCard("App","com.real.app","","#000000",SourceSerial:"phone.loading"),loadingDevice,loadingModel,false,false),"Loading must block card actions");
    loadingSource.Pending["phone.loading"].SetResult([]);
    await loading;
}
Console.WriteLine("PASS: card actions require the live device, catalog, and idle launcher");

var noPhone = CameraCompatibilityState.Create(null,true,false,false);
Require(noPhone.ToggleChecked && noPhone.ToggleEnabled && noPhone.FrameRateEnabled && noPhone.Description.Contains("Android 10"),
    "No selected phone must leave the saved camera preference editable");
var unknownVersion = CameraCompatibilityState.Create(DeviceWithApi(null),true,false,false);
Require(!unknownVersion.ToggleChecked && !unknownVersion.ToggleEnabled && !unknownVersion.FrameRateEnabled && unknownVersion.Description.Contains("未能确认"),
    "An online phone with an unknown Android version must disable camera controls and ask the user to refresh");
var unknownVersionWithCamera = CameraCompatibilityState.Create(DeviceWithApi(null),true,true,false);
Require(!unknownVersionWithCamera.ToggleChecked && !unknownVersionWithCamera.ToggleEnabled && unknownVersionWithCamera.FrameRateEnabled && unknownVersionWithCamera.Description.Contains("未能确认"),
    "An existing camera window must keep its frame rate adjustable when the selected phone's Android version is unknown");
var android9 = CameraCompatibilityState.Create(DeviceWithApi(28),true,false,false);
Require(!android9.ToggleChecked && !android9.ToggleEnabled && !android9.FrameRateEnabled && android9.Description.Contains("不支持独立应用窗口"),
    "Android without multi-display support must disable app windows");
var android10 = CameraCompatibilityState.Create(DeviceWithApi(29),true,false,false);
Require(!android10.ToggleChecked && !android10.ToggleEnabled && !android10.FrameRateEnabled && android10.Description.Contains("已自动禁用虚拟摄像头"),
    "Android 10 keeps app windows but disables the virtual camera");
var android15 = CameraCompatibilityState.Create(DeviceWithApi(35),true,false,false);
Require(!android15.ToggleChecked && !android15.ToggleEnabled && !android15.FrameRateEnabled && android15.Description.Contains("已自动禁用虚拟摄像头"),
    "Android 15 still cannot provide the virtual camera");
var android15WithCamera = CameraCompatibilityState.Create(DeviceWithApi(35),true,true,false);
Require(!android15WithCamera.ToggleChecked && !android15WithCamera.ToggleEnabled && android15WithCamera.FrameRateEnabled,
    "An existing camera window on another phone must keep its frame rate adjustable");
var android16 = CameraCompatibilityState.Create(DeviceWithApi(36),true,false,false);
Require(android16.ToggleChecked && android16.ToggleEnabled && android16.FrameRateEnabled && android16.Description.Contains("支持虚拟摄像头"),
    "Android 16 must enable the virtual camera controls");
var disabledPreference = CameraCompatibilityState.Create(DeviceWithApi(36),false,false,false);
Require(!disabledPreference.ToggleChecked && disabledPreference.ToggleEnabled,"A saved off switch must stay off on a supported phone");
var stoppingState = CameraCompatibilityState.Create(DeviceWithApi(36),true,true,true);
Require(stoppingState.ToggleChecked && !stoppingState.ToggleEnabled && !stoppingState.FrameRateEnabled,
    "Shutdown must disable camera controls without rewriting the saved preference");
Console.WriteLine("PASS: camera compatibility reflects the phone without overwriting saved preferences");

var shutdownModel = new ShellViewModel();
var shutdownSource = new ControlledCatalog();
var shutdownController = new CatalogController(shutdownSource, shutdownModel);
var inflight = shutdownController.SelectAsync(new("shutdown", "Shutdown", VirCre.Launcher.DeviceState.Online));
var stopping = shutdownController.StopAsync();
Require(!stopping.IsCompleted, "Window shutdown returned before the in-flight reader finished cleanup");
shutdownSource.Pending["shutdown"].SetResult([new("late", "com.late.app", false)]);
await stopping;
await inflight;
Require(shutdownModel.Apps.Count == 0, "A late response was published after shutdown");
Console.WriteLine("PASS: shutdown waits for in-flight cleanup and rejects late data");
var windowsModel=new ShellViewModel();
windowsModel.UpdateWindows([new VirCre.Launcher.AppWindowInfo(Guid.NewGuid(),new("p","Phone","com.app","应用"),VirCre.Launcher.AppWindowState.Running,7)]);
windowsModel.Navigate("running");
Require(!windowsModel.IsEmpty && windowsModel.Running.Count==1,"Running windows must be shown even without the original device selected");
windowsModel.UpdateWindows([]);
Require(windowsModel.IsEmpty,"Closing last window must restore running empty state");
Console.WriteLine("PASS: running windows remain independent of selected device and update empty state");
await ShutdownChecks.RunAsync();
static void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }
static VirCre.Launcher.PhoneDevice DeviceWithApi(int? api) =>
    new("phone.camera","Camera",VirCre.Launcher.DeviceState.Online,ApiLevel:api);

sealed class ControlledCatalog : VirCre.Launcher.IPhoneCatalog {
    public Dictionary<string, TaskCompletionSource<IReadOnlyList<VirCre.Launcher.PhoneApp>>> Pending {get;} = new();
    public Task<IReadOnlyList<VirCre.Launcher.PhoneDevice>> GetDevicesAsync(CancellationToken token) => throw new NotSupportedException();
    public Task<IReadOnlyList<VirCre.Launcher.PhoneApp>> GetAppsAsync(string serial,CancellationToken token) {
        var completion = new TaskCompletionSource<IReadOnlyList<VirCre.Launcher.PhoneApp>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending.Add(serial,completion);return completion.Task; // Deliberately ignores cancellation to reproduce a late old request.
    }
}
sealed class ImmediateCatalog : VirCre.Launcher.IPhoneCatalog {
    public Task<IReadOnlyList<VirCre.Launcher.PhoneDevice>> GetDevicesAsync(CancellationToken token)=>throw new NotSupportedException();
    public Task<IReadOnlyList<VirCre.Launcher.PhoneApp>> GetAppsAsync(string serial,CancellationToken token)=>Task.FromResult<IReadOnlyList<VirCre.Launcher.PhoneApp>>([new("Real","com.real.app",false,"C:\\icons\\real.png")]);
}
