using System.Collections.ObjectModel;
using System.ComponentModel;
using VirCre.Launcher;

namespace VirCre.Presentation;

/// <summary>Owns the visible directory, selected-device state and favorite/theme preferences.</summary>
public sealed class ShellViewModel : INotifyPropertyChanged {
    private readonly List<AppCard> catalog;
    private readonly HashSet<string> favorites = new(StringComparer.Ordinal);
    private readonly ObservableCollection<AppCard> visible = [];
    private readonly ObservableCollection<AppWindowInfo> running = [];
    private readonly ObservableCollection<RunningWindowCard> runningCards = [];
    private readonly Dictionary<Guid,AppCard> windowArtwork = [];
    private PhoneDevice? currentDevice;
    public ShellViewModel(bool preview = false, UserSettings? settings = null, IDispatcher? dispatcher = null) {
        Dispatcher = dispatcher ?? ImmediateDispatcher.Instance;
        IsPreview = preview;
        catalog = preview ? [.. DemoCatalog.Create()] : [];
        if (settings != null) {
            favorites.UnionWith(settings.Favorites);Theme = settings.Theme;
            VirtualCameraEnabled=settings.VirtualCameraEnabled;CameraFrameRate=CameraFrameRates.Normalize(settings.CameraFrameRate);
        }
        Apps = new(visible);Running = new(running);RunningCards = new(runningCards);Refresh();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? SettingsChanged;
    /// <summary>Host-supplied UI thread; every state change is published through it.</summary>
    public IDispatcher Dispatcher {get;}
    public string Theme {get;private set;} = "System";
    public bool VirtualCameraEnabled {get;private set;} = true;
    public int CameraFrameRate {get;private set;} = CameraFrameRates.Default;
    public UserSettings ExportSettings() => new(favorites.Order(StringComparer.Ordinal).ToArray(),Theme,
        VirtualCameraEnabled:VirtualCameraEnabled,CameraFrameRate:CameraFrameRate);
    public void SetTheme(string theme) => Dispatcher.Post(() => {
        theme=theme is "Light" or "Dark"?theme:"System";
        if(Theme==theme)return;
        Theme=theme;Notify(nameof(Theme));SettingsChanged?.Invoke();
    });
    public void SetVirtualCameraEnabled(bool enabled) => Dispatcher.Post(() => {
        if(VirtualCameraEnabled==enabled)return;
        VirtualCameraEnabled=enabled;Notify(nameof(VirtualCameraEnabled));SettingsChanged?.Invoke();
    });
    public void SetCameraFrameRate(int frameRate) => Dispatcher.Post(() => {
        frameRate=CameraFrameRates.Validate(frameRate);
        if(CameraFrameRate==frameRate)return;
        CameraFrameRate=frameRate;Notify(nameof(CameraFrameRate));SettingsChanged?.Invoke();
    });
    public ReadOnlyObservableCollection<AppCard> Apps {get;}
    public ReadOnlyObservableCollection<AppWindowInfo> Running {get;}
    public ReadOnlyObservableCollection<RunningWindowCard> RunningCards {get;}
    public string CurrentSerial {get;private set;} = "";
    public bool IsPreview {get;private set;}
    public bool HasDevice {get;private set;}
    public bool IsLoading {get;private set;}
    /// <summary>Window shutdown has started and will not accept new work again.</summary>
    public bool IsCleanup {get;private set;}
    /// <summary>Shutdown is waiting for background work right now.</summary>
    public bool IsStopping {get;private set;}
    public DeviceSelectionState DeviceSelection {get;private set;} = new([], null, false);
    public string DeviceLabel {get;private set;} = "未选择设备";
    public string ErrorMessage {get;private set;} = "";
    public string Query {get;private set;} = "";
    public string Page {get;private set;} = "apps";
    public bool IsEmpty => Page=="running" ? running.Count==0 : visible.Count==0;
    public string ResultCount => $"{visible.Count} 个{(IsPreview ? "示例" : "")}应用";
    public int CatalogCount => catalog.Count;
    public int FavoriteCount => catalog.Count(app => app.IsFavorite);
    public int RunningCount => running.Count;
    public string DeviceStatus => IsLoading ? "读取中" : ErrorMessage.Length > 0 ? "需要重试" : HasDevice ? "已连接" : "未连接";
    public string ConnectionStatus => IsCleanup ? "等待退出" : DeviceStatus;
    public bool IsConnectedStatus => !IsCleanup && HasDevice && ErrorMessage.Length == 0;
    public string DeviceHint => IsCleanup ? "正在结束会话" : DeviceSelection.Scanning ? "正在检查设备…" :
        DeviceSelection.Selected is {State:DeviceState.Online} device ? $"{device.AndroidVersionLabel} · {(device.IsNetworkConnection?"无线连接":"USB 连接")}" : "连接后选择设备";
    public bool IsBusy => IsCleanup ? IsStopping : DeviceSelection.Scanning || IsLoading;
    public bool IsSearchEnabled => !IsCleanup && !IsLoading && HasDevice;
    public bool IsDevicePickerEnabled => !IsCleanup && !DeviceSelection.Scanning;
    public bool IsRefreshEnabled => IsDevicePickerEnabled;
    public bool IsAppGridEnabled => !IsCleanup && !DeviceSelection.Scanning && !IsLoading && HasDevice;
    public bool IsSettingsEnabled => !IsCleanup;
    public bool IsEmptyActionEnabled => !IsCleanup && !DeviceSelection.Scanning && !IsLoading;
    public bool ShowPageTitle => Page is "settings" or "running";
    public bool ShowSettings => Page == "settings";
    public bool ShowSearchRow => !ShowPageTitle && (HasDevice || CatalogCount > 0);
    public bool ShowAppGrid => Page is not ("settings" or "running") && !IsEmpty;
    public bool ShowRunningList => Page == "running" && !IsEmpty;
    public bool ShowEmptyState => Page != "settings" && IsEmpty;
    public bool ShowEmptySteps => !HasDevice && Page is ("apps" or "favorites");
    public string FooterText => IsPreview ? "示例应用 · 收藏仅本次有效，尚未连接手机" :
        IsLoading ? $"正在从 {DeviceLabel} 读取应用列表…" : ErrorMessage.Length > 0 ? ErrorMessage :
        HasDevice ? $"来自 {DeviceLabel} · {catalog.Count} 个应用 · 安装、卸载或更新应用后，请点击刷新" : "连接手机后，读取可启动的应用列表。";
    public string Title => Page switch {"favorites" => "收藏", "running" => "运行中", "settings" => "设置", _ => "全部应用"};
    public string Description => Page switch {
        "favorites" => "把常用的应用放在手边。",
        "running" => "在这里管理独立打开的手机应用。",
        "settings" => "让桌面工具更合你的习惯。",
        _ => HasDevice ? $"来自 {DeviceLabel} 的手机应用。" : "手机上的应用，在桌面从容使用。"
    };
    public string EmptyTitle => DeviceSelection.Scanning ? "正在查找手机" : Page == "running" ? "还没有打开的应用" : IsLoading ? "正在读取手机应用" :
        ErrorMessage.Length > 0 ? "暂时无法读取应用" : !HasDevice && !IsPreview ? "连接你的手机" :
        Query.Trim().Length > 0 ? "没有找到匹配的应用" : Page == "favorites" ? "把常用应用放在这里" : "没有可启动的应用";
    public string EmptyDescription => DeviceSelection.Scanning ? "正在检查已授权的 ADB 设备。" : Page == "running" ? "点击应用卡片打开后，即可在这里管理独立窗口。" :
        IsLoading ? "首次读取可能需要一些时间，请保持手机连接。" : ErrorMessage.Length > 0 ? ErrorMessage :
        !HasDevice && !IsPreview ? "连接并授权后，手机里的应用就会出现在这里。" : Query.Trim().Length > 0 ? "试试其他名称，或清空搜索内容。" :
        Page == "favorites" ? "点击应用卡片右侧的星标，收藏将在重启后保留。" : "点击刷新，重新读取手机上的应用。";
    public string EmptyGlyph => Page == "running" ? "" : !HasDevice ? "" : Page == "favorites" ? "" : "";
    public string EmptyActionText => Page == "running" ? "查看全部应用" : !HasDevice || ErrorMessage.Length > 0 ? "刷新设备" :
        Query.Length > 0 ? "清空搜索" : Page == "favorites" ? "查看全部应用" : "刷新设备与应用";

    /// <summary>Accepts the device controller snapshot; the model never reads the window.</summary>
    public void UpdateDeviceSelection(DeviceSelectionState selection) => Dispatcher.Post(() => {
        if(DeviceSelection==selection)return;
        DeviceSelection=selection;Refresh();
    });
    /// <summary>Window shutdown state, pushed explicitly by the view when it starts or re-enters cleanup.</summary>
    public void SetShutdown(bool started,bool stopping) => Dispatcher.Post(() => {
        if(IsCleanup==started && IsStopping==stopping)return;
        IsCleanup=started;IsStopping=stopping;Refresh();
    });

    public void Navigate(string page) => Dispatcher.Post(() => {
        if(page is not ("apps" or "favorites" or "running" or "settings"))return;
        Page=page;Query="";Refresh();
    });
    public void Search(string query) => Dispatcher.Post(() => {Query=query;Refresh();});
    public void ToggleFavorite(string packageName) => Dispatcher.Post(() => {
        var index=catalog.FindIndex(app=>app.PackageName==packageName);if(index<0)return;
        if(!favorites.Add(packageName))favorites.Remove(packageName);
        catalog[index]=catalog[index] with {IsFavorite=favorites.Contains(packageName)};Refresh();SettingsChanged?.Invoke();
    });
    internal void BeginLoad(PhoneDevice? device) => Dispatcher.Post(() => {
        currentDevice=device;
        IsPreview=false;catalog.Clear();Query="";
        DeviceLabel=device?.Model??"未选择设备";
        CurrentSerial=device?.Serial??"";
        HasDevice=device?.State==DeviceState.Online;
        IsLoading=HasDevice;
        ErrorMessage=device?.State switch {
            DeviceState.Unauthorized=>"请在手机上允许此电脑的 USB/无线调试授权，然后刷新设备。",
            DeviceState.Offline=>"设备已离线，请检查连接后刷新设备。", _=>""
        };
        Refresh();
    });
    internal void CompleteLoad(IReadOnlyList<PhoneApp> apps) => Dispatcher.Post(() => {
        catalog.Clear();
        foreach(var app in apps) catalog.Add(new(app.Name,app.PackageName,"\uE8A9",AppColor(app.PackageName),favorites.Contains(app.PackageName),app.IsSystem,CurrentSerial,app.IconPath));
        IsLoading=false;ErrorMessage="";Refresh();
    });
    internal void FailLoad(string error) => Dispatcher.Post(() => {catalog.Clear();IsLoading=false;ErrorMessage=error;Refresh();});
    public void ReportDeviceError(string error, bool preserveCatalog = false) => Dispatcher.Post(() => {
        if (preserveCatalog && catalog.Count > 0) {
            HasDevice=false;IsLoading=false;ErrorMessage=error;
            Refresh();
        } else {BeginLoad(null);FailLoad(error);}
    });
    public void UpdateWindows(IReadOnlyList<AppWindowInfo> windows) => Dispatcher.Post(() => {
        running.Clear();foreach(var window in windows)running.Add(window);
        Refresh();
    });
    private static string AppColor(string packageName) {
        string[] colors=["#507C93","#8576A6","#548D8A","#A86F73","#B58A4C","#697E99","#768B6F"];
        uint hash=2166136261;foreach(var ch in packageName)hash=unchecked((hash^ch)*16777619);
        return colors[(int)(hash%(uint)colors.Length)];
    }
    private void Refresh() {
        foreach(var id in windowArtwork.Keys.Where(id=>!running.Any(window=>window.Id==id)).ToArray())windowArtwork.Remove(id);
        var rows=new List<RunningWindowCard>();
        foreach(var window in running) {
            var artwork=currentDevice!=null && window.Request.IsForDevice(currentDevice)?catalog.FirstOrDefault(app=>app.PackageName==window.PackageName):null;
            if(artwork!=null)windowArtwork[window.Id]=artwork;
            windowArtwork.TryGetValue(window.Id,out var savedArtwork);
            rows.Add(new(window,savedArtwork?.IconPath,savedArtwork?.Color??AppColor(window.PackageName)));
        }
        if(runningCards.Count==rows.Count && runningCards.Select(card=>card.Window.Id).SequenceEqual(rows.Select(card=>card.Window.Id))) {
            for(var index=0;index<rows.Count;index++)if(runningCards[index]!=rows[index])runningCards[index]=rows[index];
        } else {
            runningCards.Clear();foreach(var card in rows)runningCards.Add(card);
        }
        for(var index=0;index<catalog.Count;index++) {
            var app=catalog[index];
            var window=currentDevice==null?null:running.FirstOrDefault(value=>value.PackageName==app.PackageName && value.Request.IsForDevice(currentDevice));
            catalog[index]=app with {IsRunning=window?.CanFocus==true,WindowState=window==null?"":window.CanFocus?"运行中 · 切回":window.StateLabel};
        }
        var query=Query.Trim();
        var next=Page is "apps" or "favorites"?catalog.Where(app=>(Page!="favorites"||app.IsFavorite)&&
            (app.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||app.PackageName.Contains(query,StringComparison.OrdinalIgnoreCase))).ToArray():[];
        // Window updates should not discard every realized card or reset scroll/focus.
        if(visible.Count==next.Length && visible.Select(app=>(app.SourceSerial,app.PackageName)).SequenceEqual(next.Select(app=>(app.SourceSerial,app.PackageName)))) {
            for(var index=0;index<next.Length;index++)if(visible[index]!=next[index])visible[index]=next[index];
        } else {
            visible.Clear();foreach(var app in next)visible.Add(app);
        }
        NotifyState();
    }
    private void Notify(string property) => PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(property));
    private void NotifyState() {
        foreach(var property in new[]{nameof(Page),nameof(Query),nameof(Title),nameof(Description),nameof(IsEmpty),nameof(ResultCount),nameof(EmptyTitle),nameof(EmptyDescription),
            nameof(EmptyGlyph),nameof(EmptyActionText),
            nameof(IsPreview),nameof(HasDevice),nameof(IsLoading),nameof(IsCleanup),nameof(IsStopping),nameof(DeviceSelection),nameof(DeviceLabel),nameof(DeviceStatus),nameof(ConnectionStatus),nameof(IsConnectedStatus),nameof(DeviceHint),
            nameof(ErrorMessage),nameof(FooterText),nameof(CatalogCount),nameof(FavoriteCount),nameof(RunningCount),
            nameof(IsBusy),nameof(IsSearchEnabled),nameof(IsDevicePickerEnabled),nameof(IsRefreshEnabled),nameof(IsAppGridEnabled),nameof(IsSettingsEnabled),nameof(IsEmptyActionEnabled),
            nameof(ShowPageTitle),nameof(ShowSettings),nameof(ShowSearchRow),nameof(ShowAppGrid),nameof(ShowRunningList),nameof(ShowEmptyState),nameof(ShowEmptySteps)})
            PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(property));
    }
}
