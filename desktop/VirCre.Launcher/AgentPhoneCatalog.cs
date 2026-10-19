using System.Security.Cryptography;
using System.Text.Json;

namespace VirCre.Launcher;

/// <summary>One shell helper enumerates launcher entries and exports only names, package IDs, kind and icons.</summary>
public sealed class AgentPhoneCatalog(IPhoneCatalog devices, ICommandRunner runner, string agentPath, string iconDirectory) : IPhoneCatalog, ICatalogWarnings {
    public event Action<string>? Warning;
    public Task<IReadOnlyList<PhoneDevice>> GetDevicesAsync(CancellationToken token) => devices.GetDevicesAsync(token);
    public async Task<IReadOnlyList<PhoneApp>> GetAppsAsync(string serial, CancellationToken token) {
        _ = new ShortcutRequest(serial, "", "device", "com.android.shell", "app").Encode();
        if (!File.Exists(agentPath)) throw new IOException("应用读取组件缺失，请重新解压完整程序。");
        var remote = "/data/local/tmp/vircre-catalog-" + Guid.NewGuid().ToString("N") + ".jar";
        try {
            Ensure(await runner.RunAsync("adb", ["-s", serial, "push", agentPath, remote], TimeSpan.FromSeconds(15), token).ConfigureAwait(false));
            var result = await runner.RunAsync("adb", ["-s", serial, "shell", "CLASSPATH=" + remote, "app_process", "/system/bin", "com.vircre.icons.IconMain", "--catalog"], TimeSpan.FromSeconds(50), token).ConfigureAwait(false);
            Ensure(result);
            return await ParseAsync(result.Output, token).ConfigureAwait(false);
        } finally {
            try {await runner.RunAsync("adb", ["-s", serial, "shell", "rm", "-f", remote], TimeSpan.FromSeconds(3), CancellationToken.None).ConfigureAwait(false);}
            catch (Exception error) when (error is IOException or TimeoutException) { }
        }
    }
    private async Task<IReadOnlyList<PhoneApp>> ParseAsync(string output, CancellationToken token) {
        if (output.Length > 32 * 1024 * 1024) throw new InvalidDataException("应用列表数据过大。");
        var apps = new Dictionary<string, PhoneApp>(StringComparer.Ordinal);
        var complete = false;
        var iconFailures = 0;
        try {
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                token.ThrowIfCancellationRequested();
                if (complete) throw new InvalidDataException("应用列表结束后仍有数据。");
                using var json = JsonDocument.Parse(line);
                var item = json.RootElement;
                if (item.GetProperty("type").GetString() == "complete") {complete = true; continue;}
                if (item.GetProperty("type").GetString() != "app" || apps.Count >= 5000) throw new InvalidDataException("应用条目无效。");
                var package = item.GetProperty("package").GetString()!;
                var name = item.GetProperty("name").GetString()!;
                _ = new ShortcutRequest("device", "", "device", package, name).Encode();
                if (apps.ContainsKey(package)) throw new InvalidDataException("应用列表包名重复。");
                string? icon = null;
                if (item.TryGetProperty("png", out var encoded)) {
                    try {
                        var png = Convert.FromBase64String(encoded.GetString()!);
                        IconFile.ValidatePng(png);
                        icon = await SaveIconAsync(png, token).ConfigureAwait(false);
                    } catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or ArgumentException) {iconFailures++;}
                } else iconFailures++;
                apps.Add(package, new(name, package, item.GetProperty("system").GetBoolean(), icon));
            }
        } catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException) {
            throw new InvalidDataException("手机应用列表格式异常，请刷新重试。", error);
        }
        if (!complete) throw new InvalidDataException("应用列表传输未完成，保留原缓存。");
        if (iconFailures > 0) Warning?.Invoke($"{iconFailures} 个图标暂不可用，其余应用正常显示。");
        return apps.Values.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(app => app.PackageName, StringComparer.Ordinal).ToArray();
    }
    private async Task<string> SaveIconAsync(byte[] png, CancellationToken token) {
        Directory.CreateDirectory(iconDirectory);
        var path = Path.Combine(iconDirectory, Convert.ToHexString(SHA256.HashData(png)) + ".png");
        if (File.Exists(path)) return path;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            await File.WriteAllBytesAsync(temporary, png, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
            return path;
        } finally {
            try {if (File.Exists(temporary)) File.Delete(temporary);}
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
    private static void Ensure(CommandResult result) {
        if (result.ExitCode != 0) throw new IOException("手机应用读取失败，请检查调试连接后刷新。");
    }
}
