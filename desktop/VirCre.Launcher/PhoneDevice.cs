namespace VirCre.Launcher;

public enum DeviceState { Online, Unauthorized, Offline }

public sealed record PhoneDevice(string Serial, string Model, DeviceState State, string HardwareId = "", int? ApiLevel = null) {
    public IReadOnlyList<string> ConnectionSerials {get;init;} = [];
    public bool HasConnection(string serial) => Serial==serial || ConnectionSerials.Contains(serial,StringComparer.Ordinal);
    public bool IsNetworkConnection => Serial.Contains(':') || Serial.EndsWith("._tcp",StringComparison.OrdinalIgnoreCase);
    public bool SupportsVirtualCamera => ApiLevel >= AndroidCompatibility.MinimumCameraApi;
    public string AndroidVersionLabel => ApiLevel switch {
        29 => "Android 10", 30 => "Android 11", 31 => "Android 12", 32 => "Android 12L",
        33 => "Android 13", 34 => "Android 14", 35 => "Android 15", 36 => "Android 16",
        { } api => $"Android（API {api}）", _ => "Android 版本未确认"
    };
    public string DisplayLabel => $"{Model} · {State switch { DeviceState.Online => "已连接", DeviceState.Unauthorized => "未授权", _ => "离线" }}";
}

public sealed record PhoneApp(string Name, string PackageName, bool IsSystem, string? IconPath = null);

public interface IPhoneCatalog {
    Task<IReadOnlyList<PhoneDevice>> GetDevicesAsync(CancellationToken token);
    Task<IReadOnlyList<PhoneApp>> GetAppsAsync(string serial, CancellationToken token);
    Task<IReadOnlyList<PhoneApp>> GetAppsAsync(string serial, CancellationToken token, bool forceRefresh) => GetAppsAsync(serial, token);
}

public interface ICatalogWarnings {
    event Action<string>? Warning;
}
