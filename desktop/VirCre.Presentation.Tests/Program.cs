using VirCre.Presentation;

var checks = new List<(string Name, Action Check)> {
    ("shell starts with explicitly identified sample data", () => {
        var shell = new ShellViewModel();
        Require(shell.IsPreview && shell.Apps.Count == 8, "Expected eight sample apps, never a connected phone");
        Require(shell.Apps.All(app => app.PackageName.StartsWith("demo.", StringComparison.Ordinal)), "Sample identities must not look installed");
    }),
    ("search matches Chinese names and case insensitive packages", () => {
        var shell = new ShellViewModel();
        shell.Search(" 相册 ");
        Require(shell.Apps.Single().Name == "相册", "Trimmed Chinese search failed");
        shell.Search("DEMO.CALENDAR");
        Require(shell.Apps.Single().Name == "日历", "Package search failed");
        shell.Search("");
        Require(shell.Apps.Count == 8, "Clearing the query must restore the catalog");
    }),
    ("favorites are session scoped and immediately refilter", () => {
        var shell = new ShellViewModel();
        shell.Navigate("favorites");
        Require(shell.IsEmpty && shell.EmptyTitle == "把常用应用放在这里", "Expected favorite empty state");
        shell.ToggleFavorite("demo.gallery");
        Require(shell.Apps.Single().IsFavorite, "Favorite did not appear");
        shell.ToggleFavorite("demo.gallery");
        Require(shell.IsEmpty, "Removing the last favorite must update the empty state");
        Require(!new ShellViewModel().Apps.Any(app => app.IsFavorite), "Preview favorites must not persist");
    }),
    ("page changes clear search and preserve the right empty state", () => {
        var shell = new ShellViewModel();
        shell.Search("does-not-exist");
        Require(shell.IsEmpty && shell.EmptyTitle == "没有找到匹配的应用", "Expected search empty state");
        shell.Navigate("running");
        Require(shell.Query == "" && shell.IsEmpty && shell.EmptyTitle == "还没有打开的应用", "Running state inherited a search or app card");
        shell.Navigate("apps");
        Require(shell.Apps.Count == 8 && !shell.IsEmpty, "Navigating back must restore the apps");
    }),
    ("observable state updates counts and unknown actions are harmless", () => {
        var shell = new ShellViewModel();
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

static void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }
