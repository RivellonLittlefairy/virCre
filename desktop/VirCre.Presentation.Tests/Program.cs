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
Console.WriteLine("PASS: running windows remain independent of selected device and update empty state");static void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }

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
