using System.Text.Json;
using System.Text.RegularExpressions;

namespace VirCre.Launcher;

/// <summary>A complete per-phone snapshot. Icons are immutable, content-addressed PNGs beneath this cache.</summary>
public sealed partial class AppCatalogCache(string root) {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public string IconDirectory {get;} = Path.Combine(Path.GetFullPath(root), "icons");
    private string SnapshotPath(string identity) => Path.Combine(root, IconCatalog.Hash("hardware:" + identity) + ".json");
    private sealed record Entry(string Name, string PackageName, bool IsSystem, string? IconFile);
    private sealed record Snapshot(int Version, string Identity, Entry[] Apps);

    public async Task<IReadOnlyList<PhoneApp>?> LoadAsync(string identity, CancellationToken token) {
        var path = SnapshotPath(identity);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("应用缓存文件过大。");
        await using var file = File.OpenRead(path);
        var snapshot = await JsonSerializer.DeserializeAsync<Snapshot>(file, Json, token).ConfigureAwait(false);
        if (snapshot is not {Version: 1, Apps: not null} || snapshot.Identity != identity || snapshot.Apps.Length > 5000)
            throw new InvalidDataException("应用缓存格式或设备标识无效。");
        var apps = new List<PhoneApp>(snapshot.Apps.Length);
        var packages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in snapshot.Apps) {
            token.ThrowIfCancellationRequested();
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name) || entry.Name.Length > 512 || entry.PackageName == null ||
                entry.PackageName.Length > 255 || !PackagePattern().IsMatch(entry.PackageName) || !packages.Add(entry.PackageName))
                throw new InvalidDataException("应用缓存包含无效条目。");
            string? icon = null;
            if (entry.IconFile != null) {
                if (!IconPattern().IsMatch(entry.IconFile)) throw new InvalidDataException("应用图标缓存路径无效。");
                var candidate = Path.Combine(IconDirectory, entry.IconFile);
                if (File.Exists(candidate)) icon = candidate;
            }
            apps.Add(new(entry.Name, entry.PackageName, entry.IsSystem, icon));
        }
        return apps;
    }

    public async Task SaveAsync(string identity, IReadOnlyList<PhoneApp> apps, CancellationToken token) {
        Directory.CreateDirectory(root);
        var entries = apps.Select(app => new Entry(app.Name, app.PackageName, app.IsSystem,
            app.IconPath == null ? null : Path.GetFileName(app.IconPath))).ToArray();
        var path = SnapshotPath(identity);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous)) {
                await JsonSerializer.SerializeAsync(file, new Snapshot(1, identity, entries), Json, token).ConfigureAwait(false);
                await file.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        } finally {
            try {if (File.Exists(temporary)) File.Delete(temporary);}
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*(?:\.[a-zA-Z_][a-zA-Z0-9_]*)+$")] private static partial Regex PackagePattern();
    [GeneratedRegex(@"^[A-F0-9]{64}\.png$")] private static partial Regex IconPattern();
}
