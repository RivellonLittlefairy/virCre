using System.Collections.ObjectModel;
using System.ComponentModel;
using VirCre.Launcher;

namespace VirCre.Presentation;

/// <summary>Owns the visible directory, selected-device state and in-memory favorites.</summary>
public sealed class ShellViewModel : INotifyPropertyChanged {
    private readonly List<AppCard> catalog;
    private readonly HashSet<string> favorites = new(StringComparer.Ordinal);
    private readonly ObservableCollection<AppCard> visible = [];
    public ShellViewModel(bool preview = false) {
        IsPreview = preview;
        catalog = preview ? [.. DemoCatalog.Create()] : [];
        Apps = new(visible);Refresh();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ReadOnlyObservableCollection<AppCard> Apps {get;}
    public bool IsPreview {get;private set;}
    public bool HasDevice {get;private set;}
    public bool IsLoading {get;private set;}
    public string DeviceLabel {get;private set;} = "未选择设备";
    public string ErrorMessage {get;private set;} = "";
    public string Query {get;private set;} = "";
    public string Page {get;private set;} = "apps";
    public bool IsEmpty => visible.Count == 0;
    public string ResultCount => $"{visible.Count} 个{(IsPreview ? "示例" : "")}应用";
    public string DeviceStatus => IsLoading ? "读取中" : ErrorMessage.Length > 0 ? "需要重试" : HasDevice ? "已连接" : "未连接";
    public string FooterText => IsPreview ? "示例应用 · 收藏仅本次有效，尚未连接手机" :
        IsLoading ? $"正在从 {DeviceLabel} 读取应用列表…" : ErrorMessage.Length > 0 ? ErrorMessage :
        HasDevice ? $"来自 {DeviceLabel} · {catalog.Count} 个应用 · 收藏仅本次有效" : "连接手机后，读取可启动的应用列表。";
    public string Title => Page switch {"favorites" => "收藏", "running" => "运行中", "settings" => "设置", _ => "全部应用"};
    public string Description => Page switch {
        "favorites" => "把常用的应用放在手边。",
        "running" => "在这里管理独立打开的手机应用。",
        "settings" => "让桌面工具更合你的习惯。",
        _ => HasDevice ? $"来自 {DeviceLabel} 的手机应用。" : "手机上的应用，在桌面从容使用。"
    };
    public string EmptyTitle => Page == "running" ? "还没有打开的应用" : IsLoading ? "正在读取手机应用" :
        ErrorMessage.Length > 0 ? "暂时无法读取应用" : !HasDevice && !IsPreview ? "连接你的手机" :
        Query.Trim().Length > 0 ? "没有找到匹配的应用" : Page == "favorites" ? "把常用应用放在这里" : "没有可启动的应用";
    public string EmptyDescription => Page == "running" ? "独立开窗将在下一阶段接入，当前尚未打开任何手机应用。" :
        IsLoading ? "首次读取可能需要一些时间，请保持手机连接。" : ErrorMessage.Length > 0 ? ErrorMessage :
        !HasDevice && !IsPreview ? "在左侧选择已授权的手机，或点击刷新设备。" : Query.Trim().Length > 0 ? "试试其他名称，或清空搜索内容。" :
        Page == "favorites" ? "点击应用卡片右上角的星标，即可在本次运行中收藏。" : "点击刷新，重新读取手机上的应用。";

    public void Navigate(string page) {
        if(page is not ("apps" or "favorites" or "running" or "settings"))return;
        Page=page;Query="";Refresh();
    }
    public void Search(string query) {Query=query;Refresh();}
    public void ToggleFavorite(string packageName) {
        var index=catalog.FindIndex(app=>app.PackageName==packageName);if(index<0)return;
        if(!favorites.Add(packageName))favorites.Remove(packageName);
        catalog[index]=catalog[index] with {IsFavorite=favorites.Contains(packageName)};Refresh();
    }
    internal void BeginLoad(PhoneDevice? device) {
        IsPreview=false;catalog.Clear();Query="";
        DeviceLabel=device?.Model??"未选择设备";
        HasDevice=device?.State==DeviceState.Online;
        IsLoading=HasDevice;
        ErrorMessage=device?.State switch {
            DeviceState.Unauthorized=>"请在手机上允许此电脑的 USB/无线调试授权，然后刷新设备。",
            DeviceState.Offline=>"设备已离线，请检查连接后刷新设备。", _=>""
        };
        Refresh();
    }
    internal void CompleteLoad(IReadOnlyList<PhoneApp> apps) {
        catalog.Clear();
        foreach(var app in apps) catalog.Add(new(app.Name,app.PackageName,"\uE8A9",AppColor(app.PackageName),favorites.Contains(app.PackageName),app.IsSystem));
        IsLoading=false;ErrorMessage="";Refresh();
    }
    internal void FailLoad(string error) {catalog.Clear();IsLoading=false;ErrorMessage=error;Refresh();}
    public void ReportDeviceError(string error) {BeginLoad(null);FailLoad(error);}
    private static string AppColor(string packageName) {
        string[] colors=["#507C93","#8576A6","#548D8A","#A86F73","#B58A4C","#697E99","#768B6F"];
        uint hash=2166136261;foreach(var ch in packageName)hash=unchecked((hash^ch)*16777619);
        return colors[(int)(hash%(uint)colors.Length)];
    }
    private void Refresh() {
        var query=Query.Trim();visible.Clear();
        if(Page is "apps" or "favorites")foreach(var app in catalog.Where(app=>(Page!="favorites"||app.IsFavorite)&&
            (app.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||app.PackageName.Contains(query,StringComparison.OrdinalIgnoreCase))))visible.Add(app);
        foreach(var property in new[]{nameof(Page),nameof(Query),nameof(Title),nameof(Description),nameof(IsEmpty),nameof(ResultCount),nameof(EmptyTitle),nameof(EmptyDescription),
            nameof(IsPreview),nameof(HasDevice),nameof(IsLoading),nameof(DeviceLabel),nameof(DeviceStatus),nameof(ErrorMessage),nameof(FooterText)})
            PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(property));
    }
}
