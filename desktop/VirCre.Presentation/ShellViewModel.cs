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
    public ShellViewModel(bool preview = false, UserSettings? settings = null, IDispatcher? dispatcher = null, IShellText? text = null) {
        Dispatcher = dispatcher ?? ImmediateDispatcher.Instance;
        Text = text ?? ShellText.Default;
        DeviceLabel = Text[TextKeys.NoDevice];
        IsPreview = preview;
        catalog = preview ? [.. DemoCatalog.Create().Select(card => card with {Text=Text})] : [];
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
    /// <summary>Host-supplied wording; the model owns no user-visible literals.</summary>
    public IShellText Text {get;}
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
    public string DeviceLabel {get;private set;} = "";
    public string ErrorMessage {get;private set;} = "";
    public string Query {get;private set;} = "";
    public string Page {get;private set;} = "apps";
    public bool IsEmpty => Page=="running" ? running.Count==0 : visible.Count==0;
    public string ResultCount => Text.Format(IsPreview ? TextKeys.AppCountPreview : TextKeys.AppCount, visible.Count);
    public int CatalogCount => catalog.Count;
    public int FavoriteCount => catalog.Count(app => app.IsFavorite);
    public int RunningCount => running.Count;
    public string DeviceStatus => Text[IsLoading ? TextKeys.StatusLoading : ErrorMessage.Length > 0 ? TextKeys.StatusRetry :
        HasDevice ? TextKeys.StatusConnected : TextKeys.StatusDisconnected];
    public string ConnectionStatus => IsCleanup ? Text[TextKeys.ConnectionWaitingExit] : DeviceStatus;
    public bool IsConnectedStatus => !IsCleanup && HasDevice && ErrorMessage.Length == 0;
    public string DeviceHint => IsCleanup ? Text[TextKeys.HintEndingSession] : DeviceSelection.Scanning ? Text[TextKeys.HintScanning] :
        DeviceSelection.Selected is {State:DeviceState.Online} device
            ? Text.Format(TextKeys.HintOnline, device.AndroidVersionLabel, Text[device.IsNetworkConnection ? TextKeys.LinkWireless : TextKeys.LinkUsb])
            : Text[TextKeys.HintConnect];
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
    public string FooterText => IsPreview ? Text[TextKeys.FooterPreview] :
        IsLoading ? Text.Format(TextKeys.FooterLoading, DeviceLabel) : ErrorMessage.Length > 0 ? ErrorMessage :
        HasDevice ? Text.Format(TextKeys.FooterConnected, DeviceLabel, catalog.Count) : Text[TextKeys.FooterConnect];
    public string Title => Text[Page switch {"favorites" => TextKeys.TitleFavorites, "running" => TextKeys.TitleRunning, "settings" => TextKeys.TitleSettings, _ => TextKeys.TitleApps}];
    public string Description => Page switch {
        "favorites" => Text[TextKeys.DescriptionFavorites],
        "running" => Text[TextKeys.DescriptionRunning],
        "settings" => Text[TextKeys.DescriptionSettings],
        _ => HasDevice ? Text.Format(TextKeys.DescriptionDevice, DeviceLabel) : Text[TextKeys.DescriptionApps]
    };
    public string EmptyTitle => Text[DeviceSelection.Scanning ? TextKeys.EmptyScanningTitle : Page == "running" ? TextKeys.EmptyRunningTitle :
        IsLoading ? TextKeys.EmptyLoadingTitle : ErrorMessage.Length > 0 ? TextKeys.EmptyErrorTitle : !HasDevice && !IsPreview ? TextKeys.EmptyNoDeviceTitle :
        Query.Trim().Length > 0 ? TextKeys.EmptyNoMatchTitle : Page == "favorites" ? TextKeys.EmptyFavoritesTitle : TextKeys.EmptyAppsTitle];
    public string EmptyDescription => DeviceSelection.Scanning ? Text[TextKeys.EmptyScanningDescription] : Page == "running" ? Text[TextKeys.EmptyRunningDescription] :
        IsLoading ? Text[TextKeys.EmptyLoadingDescription] : ErrorMessage.Length > 0 ? ErrorMessage :
        !HasDevice && !IsPreview ? Text[TextKeys.EmptyNoDeviceDescription] : Query.Trim().Length > 0 ? Text[TextKeys.EmptyNoMatchDescription] :
        Page == "favorites" ? Text[TextKeys.EmptyFavoritesDescription] : Text[TextKeys.EmptyAppsDescription];
    public string EmptyGlyph => Page == "running" ? "" : !HasDevice ? "" : Page == "favorites" ? "" : "";
    public string EmptyActionText => Text[Page == "running" ? TextKeys.ActionViewAllApps : !HasDevice || ErrorMessage.Length > 0 ? TextKeys.ActionRefreshDevice :
        Query.Length > 0 ? TextKeys.ActionClearSearch : Page == "favorites" ? TextKeys.ActionViewAllApps : TextKeys.ActionRefreshApps];

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
    internal void BeginLoad(PhoneDevice? device, Func<bool>? canPublish = null) => Dispatcher.Post(() => {
        if(canPublish?.Invoke()==false)return;
        currentDevice=device;
        IsPreview=false;catalog.Clear();Query="";
        DeviceLabel=device?.Model??Text[TextKeys.NoDevice];
        CurrentSerial=device?.Serial??"";
        HasDevice=device?.State==DeviceState.Online;
        IsLoading=HasDevice;
        ErrorMessage=device?.State switch {
            DeviceState.Unauthorized=>Text[TextKeys.LoadUnauthorized],
            DeviceState.Offline=>Text[TextKeys.LoadOffline], _=>""
        };
        Refresh();
    });
    internal void CompleteLoad(IReadOnlyList<PhoneApp> apps, Func<bool>? canPublish = null) => Dispatcher.Post(() => {
        if(canPublish?.Invoke()==false)return;
        catalog.Clear();
        foreach(var app in apps) catalog.Add(new(app.Name,app.PackageName,"\uE8A9",AppColor(app.PackageName),favorites.Contains(app.PackageName),app.IsSystem,CurrentSerial,app.IconPath){Text=Text});
        IsLoading=false;ErrorMessage="";Refresh();
    });
    internal void FailLoad(string error, Func<bool>? canPublish = null) => Dispatcher.Post(() => {
        if(canPublish?.Invoke()==false)return;
        catalog.Clear();IsLoading=false;ErrorMessage=error;Refresh();
    });
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
            catalog[index]=app with {IsRunning=window?.CanFocus==true,WindowState=window==null?"":window.CanFocus?Text[TextKeys.WindowStateFocused]:window.StateLabel};
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
