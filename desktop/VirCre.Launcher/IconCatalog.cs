using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VirCre.Launcher;

/// <summary>Adds independently rendered device icons; failure never hides an otherwise valid app directory.</summary>
public sealed class IconCatalog(IPhoneCatalog catalog, ICommandRunner runner, string agentPath, string cacheRoot) : IPhoneCatalog {
    public event Action<string>? Warning;
    public Task<IReadOnlyList<PhoneDevice>> GetDevicesAsync(CancellationToken token) => catalog.GetDevicesAsync(token);
    public async Task<IReadOnlyList<PhoneApp>> GetAppsAsync(string serial, CancellationToken token) {
        var apps = await catalog.GetAppsAsync(serial,token).ConfigureAwait(false);
        if (apps.Count == 0) return apps;
        var remote = "/data/local/tmp/vircre-icons-" + Guid.NewGuid().ToString("N") + ".jar";
        var deviceCache=Path.Combine(cacheRoot,Hash(serial));
        CacheMaintenance.Prune(deviceCache,1,TimeSpan.FromDays(7));
        var directory = Path.Combine(deviceCache, Guid.NewGuid().ToString("N"));
        var icons = new Dictionary<string,string>(StringComparer.Ordinal);
        var pushed = false;
        try {
            if (!File.Exists(agentPath)) throw new IOException("图标读取组件缺失，请重新构建或安装完整程序。");
            pushed = true; // Even an interrupted push may leave a partial file.
            Ensure(await runner.RunAsync("adb", ["-s",serial,"push",agentPath,remote], TimeSpan.FromSeconds(15),token).ConfigureAwait(false));
            Directory.CreateDirectory(directory);
            foreach (var batch in apps.Chunk(60)) {
                var packages = batch.Select(a => a.PackageName).ToArray();
                // Package names originate in the strict scrcpy parser; validate again at the shell boundary.
                foreach (var package in packages) _ = new ShortcutRequest(serial,"","device",package,"app").Encode();
                var args = new List<string> {"-s",serial,"shell","CLASSPATH="+remote,"app_process","/system/bin","com.vircre.icons.IconMain"};
                args.AddRange(packages);
                var result = await runner.RunAsync("adb",args,TimeSpan.FromSeconds(35),token).ConfigureAwait(false);
                Ensure(result);
                foreach (var (package,png) in ParseIcons(result.Output,packages)) {
                    token.ThrowIfCancellationRequested();
                    var file = Path.Combine(directory,Hash(package)+".png");
                    await File.WriteAllBytesAsync(file,png,token).ConfigureAwait(false);
                    icons[package]=file;
                }
            }
            if (icons.Count < apps.Count) Warning?.Invoke($"已读取 {icons.Count}/{apps.Count} 个应用图标，其余使用占位图。");
        } catch (OperationCanceledException) {throw;}
        catch (Exception e) when (e is IOException or InvalidDataException or TimeoutException or UnauthorizedAccessException or ArgumentException) {
            Warning?.Invoke("部分图标读取失败，可刷新重试。"+e.Message);
        } finally {
            if (pushed) {
                try {await runner.RunAsync("adb",["-s",serial,"shell","rm","-f",remote],TimeSpan.FromSeconds(3),CancellationToken.None).ConfigureAwait(false);}
                catch (Exception e) when (e is IOException or TimeoutException) { }
            }
        }
        token.ThrowIfCancellationRequested();
        return apps.Select(app => app with {IconPath=icons.GetValueOrDefault(app.PackageName)}).ToArray();
    }
    private static void Ensure(CommandResult result) {if (result.ExitCode != 0) throw new IOException("手机端图标读取失败。");}
    public static Dictionary<string,byte[]> ParseIcons(string output, IReadOnlyCollection<string> requested) {
        if (output.Length > 20 * 1024 * 1024) throw new InvalidDataException("图标数据过大。");
        var icons = new Dictionary<string,byte[]>(StringComparer.Ordinal);
        var complete = false;
        try {
            foreach (var line in output.Split('\n',StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                if (complete) throw new InvalidDataException("图标结束标记后仍有数据。");
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("type",out var type) && type.GetString()=="complete") {complete=true;continue;}
                var package=root.GetProperty("package").GetString()!;
                if (!requested.Contains(package) || icons.ContainsKey(package)) throw new InvalidDataException("图标包名不匹配。");
                var png=Convert.FromBase64String(root.GetProperty("png").GetString()!);
                IconFile.ValidatePng(png);icons.Add(package,png);
            }
        } catch (Exception e) when (e is JsonException or FormatException or KeyNotFoundException or InvalidOperationException or ArgumentNullException) {throw new InvalidDataException("图标协议损坏。",e);}
        if (!complete) throw new InvalidDataException("图标传输未完成。");
        return icons;
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
}
