using System.Globalization;

namespace VirCre.Presentation;

/// <summary>
/// User-visible wording addressed by stable keys, so a host can localize or restyle the model
/// without touching its rules. Unknown keys fall back to the key itself, which makes gaps visible.
/// </summary>
public interface IShellText {
    string this[string key] {get;}
    string Format(string key, params object?[] args);
}

/// <summary>Stable identifiers for every string the presentation layer produces.</summary>
public static class TextKeys {
    public const string NoDevice="device.none";
    public const string StatusLoading="status.loading";
    public const string StatusRetry="status.retry";
    public const string StatusConnected="status.connected";
    public const string StatusDisconnected="status.disconnected";
    public const string ConnectionWaitingExit="connection.waitingExit";
    public const string HintEndingSession="device.hint.endingSession";
    public const string HintScanning="device.hint.scanning";
    public const string HintOnline="device.hint.online";
    public const string HintConnect="device.hint.connect";
    public const string LinkWireless="device.link.wireless";
    public const string LinkUsb="device.link.usb";

    public const string AppCount="apps.count";
    public const string AppCountPreview="apps.countPreview";
    public const string FooterPreview="footer.preview";
    public const string FooterLoading="footer.loading";
    public const string FooterConnected="footer.connected";
    public const string FooterConnect="footer.connect";

    public const string TitleApps="page.apps.title";
    public const string TitleFavorites="page.favorites.title";
    public const string TitleRunning="page.running.title";
    public const string TitleSettings="page.settings.title";
    public const string DescriptionApps="page.apps.description";
    public const string DescriptionDevice="page.apps.descriptionDevice";
    public const string DescriptionFavorites="page.favorites.description";
    public const string DescriptionRunning="page.running.description";
    public const string DescriptionSettings="page.settings.description";

    public const string EmptyScanningTitle="empty.scanning.title";
    public const string EmptyScanningDescription="empty.scanning.description";
    public const string EmptyRunningTitle="empty.running.title";
    public const string EmptyRunningDescription="empty.running.description";
    public const string EmptyLoadingTitle="empty.loading.title";
    public const string EmptyLoadingDescription="empty.loading.description";
    public const string EmptyErrorTitle="empty.error.title";
    public const string EmptyNoDeviceTitle="empty.noDevice.title";
    public const string EmptyNoDeviceDescription="empty.noDevice.description";
    public const string EmptyNoMatchTitle="empty.noMatch.title";
    public const string EmptyNoMatchDescription="empty.noMatch.description";
    public const string EmptyFavoritesTitle="empty.favorites.title";
    public const string EmptyFavoritesDescription="empty.favorites.description";
    public const string EmptyAppsTitle="empty.apps.title";
    public const string EmptyAppsDescription="empty.apps.description";
    public const string ActionViewAllApps="empty.action.viewAllApps";
    public const string ActionRefreshDevice="empty.action.refreshDevice";
    public const string ActionClearSearch="empty.action.clearSearch";
    public const string ActionRefreshApps="empty.action.refreshApps";

    public const string LoadUnauthorized="load.error.unauthorized";
    public const string LoadOffline="load.error.offline";
    public const string LoadCancelled="load.error.cancelled";
    public const string ScanReauthorize="scan.error.reauthorize";
    public const string ScanUnconfirmed="scan.error.unconfirmed";
    public const string ScanKeepsPrevious="scan.error.keepsPrevious";
    public const string ScanFailed="scan.error.failed";

    public const string CameraRequirements="camera.requirements";
    public const string CameraUnknownVersion="camera.unknownVersion";
    public const string CameraUnsupported="camera.unsupported";
    public const string CameraDisabled="camera.disabled";
    public const string CameraSupported="camera.supported";

    public const string CardFavorite="card.favorite";
    public const string CardUnfavorite="card.unfavorite";
    public const string CardKindSystem="card.kind.system";
    public const string CardKindInstalled="card.kind.installed";
    public const string CardOpen="card.open";
    public const string CardSwitchBack="card.switchBack";
    public const string CardOpenHint="card.openHint";
    public const string WindowStateFocused="window.state.focused";

    public const string ShutdownTimeout="shutdown.timeout";
}

/// <summary>Chinese defaults for the desktop build; a dictionary only overrides the keys it names.</summary>
public sealed class ShellText : IShellText {
    public static ShellText Default {get;}=new();

    private static readonly Dictionary<string,string> Defaults = new(StringComparer.Ordinal) {
        [TextKeys.NoDevice]="未选择设备",
        [TextKeys.StatusLoading]="读取中",
        [TextKeys.StatusRetry]="需要重试",
        [TextKeys.StatusConnected]="已连接",
        [TextKeys.StatusDisconnected]="未连接",
        [TextKeys.ConnectionWaitingExit]="等待退出",
        [TextKeys.HintEndingSession]="正在结束会话",
        [TextKeys.HintScanning]="正在检查设备…",
        [TextKeys.HintOnline]="{0} · {1}",
        [TextKeys.HintConnect]="连接后选择设备",
        [TextKeys.LinkWireless]="无线连接",
        [TextKeys.LinkUsb]="USB 连接",

        [TextKeys.AppCount]="{0} 个应用",
        [TextKeys.AppCountPreview]="{0} 个示例应用",
        [TextKeys.FooterPreview]="示例应用 · 收藏仅本次有效，尚未连接手机",
        [TextKeys.FooterLoading]="正在从 {0} 读取应用列表…",
        [TextKeys.FooterConnected]="来自 {0} · {1} 个应用 · 安装、卸载或更新应用后，请点击刷新",
        [TextKeys.FooterConnect]="连接手机后，读取可启动的应用列表。",

        [TextKeys.TitleApps]="全部应用",
        [TextKeys.TitleFavorites]="收藏",
        [TextKeys.TitleRunning]="运行中",
        [TextKeys.TitleSettings]="设置",
        [TextKeys.DescriptionApps]="手机上的应用，在桌面从容使用。",
        [TextKeys.DescriptionDevice]="来自 {0} 的手机应用。",
        [TextKeys.DescriptionFavorites]="把常用的应用放在手边。",
        [TextKeys.DescriptionRunning]="在这里管理独立打开的手机应用。",
        [TextKeys.DescriptionSettings]="让桌面工具更合你的习惯。",

        [TextKeys.EmptyScanningTitle]="正在查找手机",
        [TextKeys.EmptyScanningDescription]="正在检查已授权的 ADB 设备。",
        [TextKeys.EmptyRunningTitle]="还没有打开的应用",
        [TextKeys.EmptyRunningDescription]="点击应用卡片打开后，即可在这里管理独立窗口。",
        [TextKeys.EmptyLoadingTitle]="正在读取手机应用",
        [TextKeys.EmptyLoadingDescription]="首次读取可能需要一些时间，请保持手机连接。",
        [TextKeys.EmptyErrorTitle]="暂时无法读取应用",
        [TextKeys.EmptyNoDeviceTitle]="连接你的手机",
        [TextKeys.EmptyNoDeviceDescription]="连接并授权后，手机里的应用就会出现在这里。",
        [TextKeys.EmptyNoMatchTitle]="没有找到匹配的应用",
        [TextKeys.EmptyNoMatchDescription]="试试其他名称，或清空搜索内容。",
        [TextKeys.EmptyFavoritesTitle]="把常用应用放在这里",
        [TextKeys.EmptyFavoritesDescription]="点击应用卡片右侧的星标，收藏将在重启后保留。",
        [TextKeys.EmptyAppsTitle]="没有可启动的应用",
        [TextKeys.EmptyAppsDescription]="点击刷新，重新读取手机上的应用。",
        [TextKeys.ActionViewAllApps]="查看全部应用",
        [TextKeys.ActionRefreshDevice]="刷新设备",
        [TextKeys.ActionClearSearch]="清空搜索",
        [TextKeys.ActionRefreshApps]="刷新设备与应用",

        [TextKeys.LoadUnauthorized]="请在手机上允许此电脑的 USB/无线调试授权，然后刷新设备。",
        [TextKeys.LoadOffline]="设备已离线，请检查连接后刷新设备。",
        [TextKeys.LoadCancelled]="应用读取已取消，请刷新重试。",
        [TextKeys.ScanReauthorize]="请在手机上允许调试授权后刷新。",
        [TextKeys.ScanUnconfirmed]="手机连接未确认，请检查连接后刷新。",
        [TextKeys.ScanKeepsPrevious]="仍显示上次列表，暂不能启动应用。",
        [TextKeys.ScanFailed]="设备读取失败，仍显示上次列表，暂不能启动应用。{0}",

        [TextKeys.CameraRequirements]="普通应用窗口需要 Android 10 或以上；虚拟摄像头需要 Android 16 或以上。",
        [TextKeys.CameraUnknownVersion]="未能确认当前手机的 Android 版本，请刷新设备后重试。",
        [TextKeys.CameraUnsupported]="当前手机为 {0}，不支持独立应用窗口；需要 Android 10 或以上。",
        [TextKeys.CameraDisabled]="当前手机为 {0}，已自动禁用虚拟摄像头；应用列表、普通窗口和快捷方式仍可使用。",
        [TextKeys.CameraSupported]="当前手机为 {0}，支持虚拟摄像头。此开关保留为支持设备的默认设置。",

        [TextKeys.CardFavorite]="收藏{0}",
        [TextKeys.CardUnfavorite]="取消收藏{0}",
        [TextKeys.CardKindSystem]="系统应用",
        [TextKeys.CardKindInstalled]="已安装",
        [TextKeys.CardOpen]="打开{0}",
        [TextKeys.CardSwitchBack]="切回{0}",
        [TextKeys.CardOpenHint]="点击打开",
        [TextKeys.WindowStateFocused]="运行中 · 切回",

        [TextKeys.ShutdownTimeout]="收尾任务 {0} 等待超过 {1:0.#} 秒，仍在后台结束。"
    };

    private readonly IReadOnlyDictionary<string,string>? overrides;
    public ShellText(IReadOnlyDictionary<string,string>? overrides=null)=>this.overrides=overrides;
    public string this[string key] => overrides?.TryGetValue(key,out var value)==true ? value :
        Defaults.TryGetValue(key,out var fallback) ? fallback : key;
    public string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, this[key], args);
}
