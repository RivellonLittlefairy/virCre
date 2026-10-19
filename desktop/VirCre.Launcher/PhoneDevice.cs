namespace VirCre.Launcher;

public enum DeviceState { Online, Unauthorized, Offline }

public sealed record PhoneDevice(string Serial, string Model, DeviceState State, string HardwareId = "") {
    public string DisplayLabel => $"{Model} · {State switch { DeviceState.Online => "已连接", DeviceState.Unauthorized => "未授权", _ => "离线" }}";
}

public sealed record PhoneApp(string Name, string PackageName, bool IsSystem, string? IconPath = null);

public interface IPhoneCatalog {
    Task<IReadOnlyList<PhoneDevice>> GetDevicesAsync(CancellationToken token);
    Task<IReadOnlyList<PhoneApp>> GetAppsAsync(string serial, CancellationToken token);
}
