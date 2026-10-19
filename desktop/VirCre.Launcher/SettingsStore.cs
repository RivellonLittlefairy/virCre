using System.Text.Json;

namespace VirCre.Launcher;

public sealed record UserSettings(string[] Favorites, string Theme = "System", int Version = 1);
public sealed record SettingsLoad(UserSettings Settings, string? Warning = null);

/// <summary>Per-user, atomically replaced settings. A failed read never overwrites the damaged file.</summary>
public sealed class SettingsStore(string path) {
    private readonly object gate = new();
    private static readonly JsonSerializerOptions Json = new() {PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true};
    public SettingsLoad Load() {
        lock (gate) {
            try {
                if (!File.Exists(path)) return new(new([]));
                if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("设置文件过大");
                var data = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), Json);
                if (data == null || data.Version != 1 || data.Favorites == null) throw new InvalidDataException("设置格式或版本不支持");
                return new(Normalize(data));
            } catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) {
                return new(new([]), "设置读取失败，已使用默认值；原文件保留。" + e.Message);
            }
        }
    }
    public void Save(UserSettings data) {
        lock (gate) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) {
                    JsonSerializer.Serialize(file, Normalize(data), Json);
                    file.Flush(true);
                }
                File.Move(temp, path, true);
            } finally {if (File.Exists(temp)) File.Delete(temp);}
        }
    }
    private static UserSettings Normalize(UserSettings data) => new(
        data.Favorites.Where(p => !string.IsNullOrWhiteSpace(p) && p.Length <= 255).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
        data.Theme is "Dark" or "Light" ? data.Theme : "System");
}
