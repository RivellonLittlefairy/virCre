using System.Collections.ObjectModel;
using System.ComponentModel;

namespace VirCre.Presentation;

/// <summary>Phone-independent state for the first UI milestone.</summary>
public sealed class ShellViewModel : INotifyPropertyChanged {
    private readonly List<AppCard> catalog = [.. DemoCatalog.Create()];
    private readonly ObservableCollection<AppCard> visible = [];
    public ShellViewModel() { Apps = new(visible); Refresh(); }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ReadOnlyObservableCollection<AppCard> Apps { get; }
    public bool IsPreview => true;
    public string Query { get; private set; } = "";
    public string Page { get; private set; } = "apps";
    public bool IsEmpty => visible.Count == 0;
    public string ResultCount => $"{visible.Count} 个示例应用";
    public string Title => Page switch { "favorites" => "收藏", "running" => "运行中", "settings" => "设置", _ => "全部应用" };
    public string Description => Page switch {
        "favorites" => "把常用的应用放在手边。",
        "running" => "在这里管理独立打开的手机应用。",
        "settings" => "让桌面工具更合你的习惯。",
        _ => "手机上的应用，在桌面从容使用。"
    };
    public string EmptyTitle => Page == "running" ? "还没有打开的应用" :
        Query.Trim().Length > 0 ? "没有找到匹配的应用" : "把常用应用放在这里";
    public string EmptyDescription => Page == "running" ? "接入手机并打开应用后，窗口会显示在这里。" :
        Query.Trim().Length > 0 ? "试试其他名称，或清空搜索内容。" : "点击应用卡片右上角的星标，即可在本次预览中收藏。";

    public void Navigate(string page) {
        if (page is not ("apps" or "favorites" or "running" or "settings")) return;
        Page = page;
        Query = "";
        Refresh();
    }
    public void Search(string query) { Query = query; Refresh(); }
    public void ToggleFavorite(string packageName) {
        var index = catalog.FindIndex(app => app.PackageName == packageName);
        if (index < 0) return;
        catalog[index] = catalog[index] with { IsFavorite = !catalog[index].IsFavorite };
        Refresh();
    }
    private void Refresh() {
        var query = Query.Trim();
        visible.Clear();
        if (Page is "apps" or "favorites") {
            foreach (var app in catalog.Where(app =>
                (Page != "favorites" || app.IsFavorite) &&
                (app.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || app.PackageName.Contains(query, StringComparison.OrdinalIgnoreCase))))
                visible.Add(app);
        }
        foreach (var property in new[] { nameof(Page), nameof(Query), nameof(Title), nameof(Description), nameof(IsEmpty), nameof(ResultCount), nameof(EmptyTitle), nameof(EmptyDescription) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
