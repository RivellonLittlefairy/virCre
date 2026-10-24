using System.Reflection;
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
    var idle = new DeviceSelectionState([guardDevice],guardDevice,false);
    var otherDevice = new VirCre.Launcher.PhoneDevice("phone.other","Other",VirCre.Launcher.DeviceState.Online);
    Require(AppActionGuard.CanActOn(guardCard,guardDevice,guardModel,idle,false),"A live card on the selected online phone must be actionable");
    Require(!AppActionGuard.CanActOn(guardCard,guardDevice,guardModel,idle with {Scanning=true},false),"Scanning must block card actions");
    Require(!AppActionGuard.CanActOn(guardCard,guardDevice,guardModel,idle,true),"Shutdown must block card actions");
    Require(!AppActionGuard.CanActOn(guardCard,guardDevice with {State=VirCre.Launcher.DeviceState.Offline},guardModel,idle,false),"An offline phone must block its cards");
    Require(!AppActionGuard.CanActOn(guardCard,guardDevice with {State=VirCre.Launcher.DeviceState.Unauthorized},guardModel,idle,false),"An unauthorized phone must block its cards");
    Require(!AppActionGuard.CanActOn(guardCard,guardDevice with {Serial="phone.other"},guardModel,idle,false),"A card must not act on a different phone");
    Require(!AppActionGuard.CanActOn(guardCard,guardDevice,guardModel,idle with {Selected=otherDevice},false),"A card must not act once another phone is selected");
    await guardController.SelectAsync(otherDevice);
    Require(!AppActionGuard.CanActOn(guardCard,otherDevice,guardModel,idle with {Selected=otherDevice},false),"A card from the previous phone must not act after switching");
}
var loadingModel = new ShellViewModel();
var loadingSource = new ControlledCatalog();
using (var loadingController = new CatalogController(loadingSource, loadingModel)) {
    var loadingDevice = new VirCre.Launcher.PhoneDevice("phone.loading","Loading",VirCre.Launcher.DeviceState.Online);
    var loading = loadingController.SelectAsync(loadingDevice);
    Require(loadingModel.IsLoading,"Fixture must keep the catalog loading");
    var loadingSelection = new DeviceSelectionState([loadingDevice],loadingDevice,false);
    Require(!AppActionGuard.CanActOn(new AppCard("App","com.real.app","","#000000",SourceSerial:"phone.loading"),loadingDevice,loadingModel,loadingSelection,false),"Loading must block card actions");
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

var scanSource = new ScriptedPhoneCatalog();
var scanModel = new ShellViewModel();
using (var scanCatalog = new CatalogController(scanSource, scanModel))
using (var scanDevices = new DeviceController(scanSource, scanCatalog, scanModel)) {
    var phoneA = Device("p.a", "Phone A", "hw-a");
    var phoneB = Device("p.b", "Phone B", "hw-b");
    var firstScan = scanDevices.RefreshAsync(false, CancellationToken.None);
    Require(scanDevices.State.Scanning, "A refresh must publish its scanning state");
    var secondScan = scanDevices.RefreshAsync(false, CancellationToken.None);
    scanSource.Scans[1].SetResult([phoneB]);
    await Until(() => scanSource.HasAppRequest("p.b"));
    scanSource.LastApps("p.b").SetResult([new("B app", "com.b.app", false)]);
    await secondScan;
    scanSource.Scans[0].SetResult([phoneA]);
    await firstScan;
    Require(scanDevices.State.Selected?.Serial == "p.b" && scanDevices.State.Devices.Count == 1 && !scanDevices.State.Scanning,
        "A stale scan published its devices after a newer scan finished");
    Require(scanModel.Apps.Single().PackageName == "com.b.app" && scanModel.CurrentSerial == "p.b",
        "A stale scan overwrote the selected phone's apps");

    // A wireless address change keeps the same phone when its hardware identity matches.
    var wirelessScan = scanDevices.RefreshAsync(true, CancellationToken.None);
    scanSource.Scans[2].SetResult([Device("p.b.wifi", "Phone B", "hw-b")]);
    await Until(() => scanSource.HasAppRequest("p.b.wifi"));
    scanSource.LastApps("p.b.wifi").SetResult([new("B app", "com.b.app", false)]);
    await wirelessScan;
    Require(scanDevices.State.Selected?.Serial == "p.b.wifi", "A known phone with a new transport was not reselected by hardware identity");
}
Console.WriteLine("PASS: device scans publish only the newest result and restore selection by hardware identity");

var switchSource = new ScriptedPhoneCatalog();
var switchModel = new ShellViewModel();
using (var switchCatalog = new CatalogController(switchSource, switchModel))
using (var switchDevices = new DeviceController(switchSource, switchCatalog, switchModel)) {
    var staleLoad = switchDevices.SelectAsync(Device("p.a", "Phone A", "hw-a"), CancellationToken.None);
    await Until(() => switchSource.HasAppRequest("p.a"));
    var currentLoad = switchDevices.SelectAsync(Device("p.b", "Phone B", "hw-b"), CancellationToken.None);
    await Until(() => switchSource.HasAppRequest("p.b"));
    Require(switchDevices.State.Selected?.Serial == "p.b", "Selecting another phone did not publish it");
    switchSource.LastApps("p.b").SetResult([new("B app", "com.b.app", false)]);
    await currentLoad;
    switchSource.LastApps("p.a").SetResult([new("A app", "com.a.app", false)]);
    await staleLoad;
    Require(switchModel.Apps.Single().PackageName == "com.b.app", "The previous phone's late app list overwrote the new selection");
}
Console.WriteLine("PASS: selecting another phone rejects the previous phone's late app list");

var offlineSource = new ScriptedPhoneCatalog();
var offlineModel = new ShellViewModel();
using (var offlineCatalog = new CatalogController(offlineSource, offlineModel))
using (var offlineDevices = new DeviceController(offlineSource, offlineCatalog, offlineModel)) {
    var phone = Device("p.keep", "Phone", "hw-keep");
    var firstLoad = offlineDevices.RefreshAsync(false, CancellationToken.None);
    offlineSource.Scans[0].SetResult([phone]);
    await Until(() => offlineSource.HasAppRequest("p.keep"));
    offlineSource.LastApps("p.keep").SetResult([new("Keep", "com.keep.app", false)]);
    await firstLoad;
    var card = offlineModel.Apps.Single();
    Require(AppActionGuard.CanActOn(card, offlineDevices.State.Selected, offlineModel, offlineDevices.State, false),
        "A card from a live selected phone must be actionable");

    var locked = offlineDevices.RefreshAsync(true, CancellationToken.None);
    offlineSource.Scans[1].SetResult([phone with {State=VirCre.Launcher.DeviceState.Unauthorized}]);
    await locked;
    Require(offlineDevices.State.Selected?.State == VirCre.Launcher.DeviceState.Unauthorized && offlineModel.Apps.Count == 1 && !offlineModel.HasDevice,
        "An unauthorized phone must keep the cached list but report itself disconnected");
    Require(!AppActionGuard.CanActOn(card, offlineDevices.State.Selected, offlineModel, offlineDevices.State, false),
        "A cached card stayed actionable while the phone was unauthorized");

    var recovered = offlineDevices.RefreshAsync(true, CancellationToken.None);
    offlineSource.Scans[2].SetResult([phone]);
    await Until(() => offlineSource.AppRequests("p.keep") >= 2);
    offlineSource.LastApps("p.keep").SetResult([new("Keep", "com.keep.app", false)]);
    await recovered;
    Require(offlineDevices.State.Selected?.State == VirCre.Launcher.DeviceState.Online && offlineModel.HasDevice && offlineModel.Apps.Count == 1,
        "Restoring authorization did not reload the selected phone's apps");
    Require(AppActionGuard.CanActOn(offlineModel.Apps.Single(), offlineDevices.State.Selected, offlineModel, offlineDevices.State, false),
        "Cards must be actionable again after authorization returns");
}
Console.WriteLine("PASS: an unauthorized phone keeps its cache but only publishes actions after recovery");

var stopSource = new ScriptedPhoneCatalog();
var stopModel = new ShellViewModel();
using (var stopCatalog = new CatalogController(stopSource, stopModel))
using (var stopDevices = new DeviceController(stopSource, stopCatalog, stopModel)) {
    var pendingScan = stopDevices.RefreshAsync(false, CancellationToken.None);
    Require(stopDevices.State.Scanning, "Fixture must keep a scan in flight");
    var stoppingScans = stopDevices.StopAsync();
    Require(!stoppingScans.IsCompleted, "Stopping returned before the in-flight scan finished");
    stopSource.Scans[0].SetResult([Device("p.a", "Phone A", "hw-a")]);
    await stoppingScans;
    await pendingScan;
    Require(stopDevices.State.Selected == null && stopDevices.State.Devices.Count == 0,
        "A device scan published after shutdown");
}
Console.WriteLine("PASS: shutdown waits for every in-flight device scan and rejects late results");

var stopAppsSource = new CancellableAppCatalog();
var stopAppsModel = new ShellViewModel();
using (var stopAppsCatalog = new CatalogController(stopAppsSource, stopAppsModel))
using (var stopAppsDevices = new DeviceController(stopAppsSource, stopAppsCatalog, stopAppsModel)) {
    var appRead = stopAppsDevices.SelectAsync(Device("p.stop", "Stopping", "hw-stop"), CancellationToken.None);
    var appStop = stopAppsDevices.StopAsync();
    try {
        await stopAppsSource.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Require(!appStop.IsCompleted, "Stopping must wait for the cancelled app reader to finish cleanup");
    } finally { stopAppsSource.Cleanup.TrySetResult(); }
    await appStop.WaitAsync(TimeSpan.FromSeconds(2));
    await appRead;
    Require(stopAppsModel.Apps.Count == 0, "Stopping a selected-device load must reject its late data");
}
Console.WriteLine("PASS: stopping a direct selection cancels app loading before waiting for cleanup");

var viewState = new ShellViewModel();
Require(viewState.ShowEmptyState && !viewState.ShowAppGrid && !viewState.ShowSearchRow && !viewState.ShowPageTitle && !viewState.IsSearchEnabled,
    "An empty start must show only the empty state");
Require(viewState.IsBusy == false && viewState.EmptyActionText == "刷新设备" && viewState.DeviceHint == "连接后选择设备",
    "An empty start must offer a refresh and explain the device hint");
var viewDevice = Device("p.view", "Phone", "hw-view");
viewState.UpdateDeviceSelection(new([viewDevice], viewDevice, true));
Require(viewState.IsBusy && !viewState.IsDevicePickerEnabled && !viewState.IsRefreshEnabled && !viewState.IsEmptyActionEnabled,
    "Scanning must disable the picker, refresh and the empty action");
Require(viewState.DeviceHint.Contains("正在检查设备") && viewState.EmptyTitle == "正在查找手机" && viewState.EmptyDescription.Contains("ADB"),
    "Scanning must win over the normal empty state text");
var viewSource = new ScriptedPhoneCatalog();
using (var viewCatalog = new CatalogController(viewSource, viewState)) {
    viewState.UpdateDeviceSelection(new([viewDevice], viewDevice, false));
    var viewLoad = viewCatalog.SelectAsync(viewDevice, CancellationToken.None);
    Require(viewState.IsBusy && !viewState.IsSearchEnabled && !viewState.IsAppGridEnabled && viewState.EmptyTitle == "正在读取手机应用",
        "Loading must disable search and the grid while keeping the loading empty state");
    viewSource.LastApps("p.view").SetResult([new("View app", "com.view.app", false)]);
    await viewLoad;
    Require(viewState.IsSearchEnabled && viewState.IsAppGridEnabled && viewState.ShowAppGrid && !viewState.ShowEmptyState && viewState.ShowSearchRow,
        "A loaded phone must show the grid and enable search");
    Require(viewState.EmptyActionText == "刷新设备与应用", "An app page without a query must offer a refresh");
    var failedView = viewCatalog.SelectAsync(viewDevice, CancellationToken.None);
    viewSource.LastApps("p.view").SetException(new IOException("读取失败"));
    await failedView;
    Require(viewState.ShowEmptyState && !viewState.ShowAppGrid && viewState.EmptyTitle == "暂时无法读取应用" && viewState.EmptyActionText == "刷新设备" && !viewState.IsConnectedStatus,
        "A failed read must show the error empty state and offer a device refresh");
    viewState.Navigate("running");
    Require(viewState.ShowPageTitle && !viewState.ShowSearchRow && !viewState.ShowAppGrid && viewState.ShowEmptyState && viewState.EmptyActionText == "查看全部应用",
        "An empty running page must show its own empty state");
    viewState.Navigate("settings");
    Require(viewState.ShowSettings && viewState.ShowPageTitle && !viewState.ShowEmptyState && !viewState.ShowSearchRow,
        "Settings must hide the directory states");
    var recoveredView = viewCatalog.SelectAsync(viewDevice, CancellationToken.None);
    viewSource.LastApps("p.view").SetResult([new("View app", "com.view.app", false)]);
    await recoveredView;
    Require(viewState.ErrorMessage.Length == 0 && viewState.IsConnectedStatus,
        "The normal page and cleanup fixtures require a successful recovery from the previous read error");
    viewState.Navigate("favorites");
    Require(viewState.ShowEmptyState && viewState.EmptyActionText == "查看全部应用",
        "An empty favorites page must offer the full directory");
    viewState.Navigate("apps");

    viewState.SetShutdown(started:true, stopping:true);
    Require(viewState.IsBusy && !viewState.IsSearchEnabled && !viewState.IsDevicePickerEnabled && !viewState.IsSettingsEnabled && !viewState.IsEmptyActionEnabled,
        "Cleanup must disable directory and settings controls");
    Require(viewState.ConnectionStatus == "等待退出" && viewState.DeviceHint == "正在结束会话" && !viewState.IsConnectedStatus,
        "Cleanup must replace the connection and device hints");
    viewState.SetShutdown(started:true, stopping:false);
    Require(!viewState.IsBusy && !viewState.IsSearchEnabled && viewState.ConnectionStatus == "等待退出",
        "A finished cleanup attempt must keep the controls disabled");
    viewState.SetShutdown(started:true, stopping:true);
    Require(viewState.IsBusy && !viewState.IsSearchEnabled, "A retried cleanup attempt must wait again without re-enabling controls");
    viewState.SetShutdown(started:true, stopping:false);
    viewState.SetShutdown(started:false, stopping:false);
    Require(viewState.IsSearchEnabled && viewState.ConnectionStatus == "已连接",
        "Restoring interaction must return the live device state");
}
Console.WriteLine("PASS: derived view state covers scanning, loading, pages and cleanup without UI types");

var marshalModel = new ShellViewModel(dispatcher: new ManualDispatcher());
var marshalDispatcher = (ManualDispatcher)marshalModel.Dispatcher;
var marshalSource = new ScriptedPhoneCatalog();
using (var marshalCatalog = new CatalogController(marshalSource, marshalModel)) {
    var marshalLoad = marshalCatalog.SelectAsync(Device("p.ui", "Phone UI", "hw-ui"), CancellationToken.None);
    Require(marshalModel.DeviceLabel != "Phone UI", "A load touched model state before the UI dispatcher ran");
    marshalDispatcher.RunPending();
    Require(marshalModel.DeviceLabel == "Phone UI" && marshalModel.IsLoading, "BeginLoad did not marshal to the UI thread");
    marshalSource.LastApps("p.ui").SetResult([new("UI app", "com.ui.app", false)]);
    await marshalLoad;
    Require(marshalModel.Apps.Count == 0, "A completed load published without the UI dispatcher");
    marshalDispatcher.RunPending();
    Require(marshalModel.Apps.Single().PackageName == "com.ui.app" && !marshalModel.IsLoading, "CompleteLoad did not marshal to the UI thread");
}
Console.WriteLine("PASS: model and catalog publish only through the host dispatcher");

foreach (var oldOutcome in new[] {"success", "failure"}) {
    var queuedDispatcher = new ManualDispatcher();
    var queuedModel = new ShellViewModel(dispatcher: queuedDispatcher);
    var queuedSource = new ScriptedPhoneCatalog();
    using var queuedCatalog = new CatalogController(queuedSource, queuedModel);
    var oldRead = queuedCatalog.SelectAsync(Device("p.old", "Old", "hw-old"), CancellationToken.None);
    queuedDispatcher.RunPending();
    if (oldOutcome == "success") queuedSource.LastApps("p.old").SetResult([new("Old app", "com.old.app", false)]);
    else queuedSource.LastApps("p.old").SetException(new IOException("Old read failed"));
    await oldRead;

    Task currentRead = Task.CompletedTask;
    queuedDispatcher.RunOnUi(() => currentRead = queuedCatalog.SelectAsync(Device("p.new", "New", "hw-new"), CancellationToken.None));
    queuedDispatcher.RunPending();
    Require(queuedModel.CurrentSerial == "p.new" && queuedModel.IsLoading && queuedModel.Apps.Count == 0 && queuedModel.ErrorMessage.Length == 0,
        "An old queued " + oldOutcome + " must not change the new device's pending load");
    queuedSource.LastApps("p.new").SetResult([new("New app", "com.new.app", false)]);
    await currentRead;
    queuedDispatcher.RunPending();
    Require(queuedModel.Apps.Single().PackageName == "com.new.app" && queuedModel.Apps.Single().SourceSerial == "p.new",
        "The current device must publish its own app list after rejecting old queued callbacks");
}

var stoppedDispatcher = new ManualDispatcher();
var stoppedModel = new ShellViewModel(dispatcher: stoppedDispatcher);
var stoppedSource = new ScriptedPhoneCatalog();
using (var stoppedCatalog = new CatalogController(stoppedSource, stoppedModel)) {
    var queuedRead = stoppedCatalog.SelectAsync(Device("p.ended", "Ended", "hw-ended"), CancellationToken.None);
    stoppedDispatcher.RunPending();
    stoppedSource.LastApps("p.ended").SetResult([new("Ended app", "com.ended.app", false)]);
    await queuedRead;
    await stoppedCatalog.StopAsync();
    stoppedDispatcher.RunPending();
    Require(stoppedModel.Apps.Count == 0, "A completed but still queued app list must not publish after shutdown");
}
Console.WriteLine("PASS: queued catalog results recheck ownership after device changes and shutdown");

var missingKeys = typeof(TextKeys).GetFields(BindingFlags.Public|BindingFlags.Static)
    .Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!)
    .Where(key => ShellText.Default[key] == key).ToArray();
Require(missingKeys.Length == 0, "Default wording is missing keys: " + string.Join(", ", missingKeys));
var customText = new ShellText(new Dictionary<string,string> {
    [TextKeys.HintConnect] = "Pick a phone",
    [TextKeys.CardFavorite] = "Star {0}"
});
var textModel = new ShellViewModel(preview: true, text: customText);
Require(textModel.DeviceHint == "Pick a phone", "Host wording did not reach the device hint");
Require(textModel.Apps[0].FavoriteLabel == "Star " + textModel.Apps[0].Name, "Card labels did not use the host wording");
Require(customText[TextKeys.TitleApps] == "全部应用", "A partial catalog must keep untouched defaults");
Require(customText["unknown.key"] == "unknown.key", "An unknown key must stay visible instead of returning empty text");
Console.WriteLine("PASS: wording is looked up by key and hosts can override single phrases");
await ShutdownChecks.RunAsync();
static void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }
static VirCre.Launcher.PhoneDevice DeviceWithApi(int? api) =>
    new("phone.camera","Camera",VirCre.Launcher.DeviceState.Online,ApiLevel:api);
static VirCre.Launcher.PhoneDevice Device(string serial,string model,string hardware) =>
    new(serial,model,VirCre.Launcher.DeviceState.Online,hardware);
static async Task Until(Func<bool> ready) {
    for(var attempt=0;attempt<400 && !ready();attempt++)await Task.Delay(5);
    Require(ready(),"Timed out waiting for a scripted catalog step");
}

/// <summary>Queues work like a UI dispatcher whose thread is not the caller's.</summary>
sealed class ManualDispatcher : IDispatcher {
    private readonly Queue<Action> pending = new();
    public bool HasAccess {get;private set;}
    public void Post(Action work) { if(HasAccess)work(); else pending.Enqueue(work); }
    public void RunOnUi(Action work) {
        var previous=HasAccess;HasAccess=true;
        try {work();} finally {HasAccess=previous;}
    }
    public void RunPending() => RunOnUi(() => { while(pending.Count>0)pending.Dequeue()(); });
}
sealed class CancellableAppCatalog : VirCre.Launcher.IPhoneCatalog {
    public TaskCompletionSource Cancelled {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Cleanup {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<IReadOnlyList<VirCre.Launcher.PhoneDevice>> GetDevicesAsync(CancellationToken token)=>throw new NotSupportedException();
    public async Task<IReadOnlyList<VirCre.Launcher.PhoneApp>> GetAppsAsync(string serial,CancellationToken token) {
        try {await Task.Delay(Timeout.Infinite,token);return [];}
        finally {Cancelled.TrySetResult();await Cleanup.Task;}
    }
}
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
/// <summary>Device and app reads complete only when the check says so, in any order.</summary>
sealed class ScriptedPhoneCatalog : VirCre.Launcher.IPhoneCatalog {
    private readonly Dictionary<string,List<TaskCompletionSource<IReadOnlyList<VirCre.Launcher.PhoneApp>>>> apps=new(StringComparer.Ordinal);
    public List<TaskCompletionSource<IReadOnlyList<VirCre.Launcher.PhoneDevice>>> Scans {get;}=[];
    public Task<IReadOnlyList<VirCre.Launcher.PhoneDevice>> GetDevicesAsync(CancellationToken token) {
        var completion=new TaskCompletionSource<IReadOnlyList<VirCre.Launcher.PhoneDevice>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Scans.Add(completion);return completion.Task; // Ignores cancellation so late results reach the controller.
    }
    public Task<IReadOnlyList<VirCre.Launcher.PhoneApp>> GetAppsAsync(string serial,CancellationToken token) {
        if(!apps.TryGetValue(serial,out var pending))apps[serial]=pending=[];
        var completion=new TaskCompletionSource<IReadOnlyList<VirCre.Launcher.PhoneApp>>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.Add(completion);return completion.Task;
    }
    public bool HasAppRequest(string serial)=>AppRequests(serial)>0;
    public int AppRequests(string serial)=>apps.TryGetValue(serial,out var pending)?pending.Count:0;
    public TaskCompletionSource<IReadOnlyList<VirCre.Launcher.PhoneApp>> LastApps(string serial)=>apps[serial][^1];
}
