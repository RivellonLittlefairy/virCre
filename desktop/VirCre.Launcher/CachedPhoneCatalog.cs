using System.Text.Json;

namespace VirCre.Launcher;

/// <summary>Startup/device selection never scans applications when this phone has a valid snapshot.</summary>
public sealed class CachedPhoneCatalog : IPhoneCatalog, ICatalogWarnings {
    private readonly IPhoneCatalog source;
    private readonly AppCatalogCache cache;
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyDictionary<string, string> identities = new Dictionary<string, string>();
    public CachedPhoneCatalog(IPhoneCatalog source, AppCatalogCache cache) {
        this.source = source; this.cache = cache;
        if (source is ICatalogWarnings warnings) warnings.Warning += message => Warning?.Invoke(message);
    }
    public event Action<string>? Warning;
    public async Task<IReadOnlyList<PhoneDevice>> GetDevicesAsync(CancellationToken token) {
        var devices = await source.GetDevicesAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        Volatile.Write(ref identities, devices.ToDictionary(device => device.Serial, device => device.HardwareId, StringComparer.Ordinal));
        return devices;
    }
    public Task<IReadOnlyList<PhoneApp>> GetAppsAsync(string serial, CancellationToken token) => GetAppsAsync(serial, token, false);
    public async Task<IReadOnlyList<PhoneApp>> GetAppsAsync(string serial, CancellationToken token, bool forceRefresh) {
        var identity = Volatile.Read(ref identities).GetValueOrDefault(serial);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try {
            IReadOnlyList<PhoneApp>? saved = null;
            if (!string.IsNullOrEmpty(identity)) {
                try {saved = await cache.LoadAsync(identity, token).ConfigureAwait(false);}
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) {
                    Warning?.Invoke("应用缓存无法读取，将重新读取手机。" + error.Message);
                }
                if (!forceRefresh && saved != null) return saved;
            }
            IReadOnlyList<PhoneApp> apps;
            try {apps = await source.GetAppsAsync(serial, token).ConfigureAwait(false);}
            catch (Exception error) when (saved != null && (CatalogErrors.IsExpected(error) || error is UnauthorizedAccessException)) {
                token.ThrowIfCancellationRequested();
                Warning?.Invoke("刷新失败，继续显示上次缓存；应用变动尚未更新。" + error.Message);
                return saved;
            }
            token.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(identity)) {
                try {await cache.SaveAsync(identity, apps, token).ConfigureAwait(false);}
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                    Warning?.Invoke("本次应用列表已读取，但缓存保存失败。" + error.Message);
                }
            } else Warning?.Invoke("手机未提供稳定标识，暂不保存跨重启应用缓存，避免读到其他手机的列表。");
            return apps;
        } finally {gate.Release();}
    }
}
