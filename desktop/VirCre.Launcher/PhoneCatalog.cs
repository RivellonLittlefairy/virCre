using System.Text.RegularExpressions;

namespace VirCre.Launcher;

public sealed partial class PhoneCatalog(ICommandRunner runner) : IPhoneCatalog {
    public async Task<IReadOnlyList<PhoneDevice>> GetDevicesAsync(CancellationToken token) {
        var result = await runner.RunAsync("adb", ["devices", "-l"], TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
        EnsureSuccess(result, "设备列表");
        var devices=ParseDevices(result.Output).ToArray();
        for(var i=0;i<devices.Length;i++) {
            if(devices[i].State!=DeviceState.Online) continue;
            try {devices[i]=devices[i] with {HardwareId=await ShortcutRequest.GetHardwareIdAsync(runner,devices[i].Serial,token).ConfigureAwait(false)};}
            catch(Exception error) when(error is IOException or TimeoutException) { /* Directory discovery still works when an OEM hides its serial. */ }
        }
        return devices;
    }
    public async Task<IReadOnlyList<PhoneApp>> GetAppsAsync(string serial, CancellationToken token) {
        if (!SerialPattern().IsMatch(serial)) throw new ArgumentException("设备序列号格式无效。", nameof(serial));
        var result = await runner.RunAsync("scrcpy", ["-s", serial, "--list-apps"], TimeSpan.FromSeconds(45), token).ConfigureAwait(false);
        EnsureSuccess(result, "应用列表");
        return ParseApps(result.Output + "\n" + result.Error);
    }
    private static void EnsureSuccess(CommandResult result, string operation) {
        if (result.ExitCode != 0) throw new IOException($"{operation}读取失败（退出码 {result.ExitCode}），请检查设备授权与连接后刷新。");
    }
    public static IReadOnlyList<PhoneDevice> ParseDevices(string text) {
        if (!text.Contains("List of devices attached", StringComparison.Ordinal)) throw new InvalidDataException("设备列表格式无效，请检查 adb 版本。");
        var devices = new List<PhoneDevice>();
        foreach (var line in text.Split('\n')) {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !SerialPattern().IsMatch(parts[0])) continue;
            var state = parts[1] switch { "device" => DeviceState.Online, "unauthorized" => DeviceState.Unauthorized, "offline" => DeviceState.Offline, _ => (DeviceState?)null };
            if (state == null) continue;
            var model = parts.FirstOrDefault(part => part.StartsWith("model:", StringComparison.Ordinal))?[6..].Replace('_', ' ') ?? "Android";
            devices.Add(new(parts[0], model, state.Value));
        }
        return devices.DistinctBy(device => device.Serial).ToArray();
    }
    public static IReadOnlyList<PhoneApp> ParseApps(string text) {
        if (!text.Contains("List of apps:", StringComparison.Ordinal)) throw new InvalidDataException("没有收到完整应用列表，请检查 scrcpy 版本与手机连接。");
        var apps = new Dictionary<string, PhoneApp>(StringComparer.Ordinal);
        var inList = false;
        foreach (var line in text.Split('\n')) {
            if (line.Contains("List of apps:", StringComparison.Ordinal)) { inList = true; continue; }
            if (!inList) continue;
            var trimmed = line.Trim();
            if (!(trimmed.StartsWith("* ", StringComparison.Ordinal) || trimmed.StartsWith("- ", StringComparison.Ordinal))) continue;
            var match = AppPattern().Match(trimmed);
            if (!match.Success) throw new InvalidDataException("应用列表包含无法识别的条目，请刷新重试。");
            var package = match.Groups["package"].Value;
            apps.TryAdd(package, new(match.Groups["name"].Value.Trim(), package, match.Groups["kind"].Value == "*"));
        }
        return apps.Values.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(app => app.PackageName, StringComparer.Ordinal).ToArray();
    }
    [GeneratedRegex(@"^[a-zA-Z0-9._:-]+$")]
    private static partial Regex SerialPattern();
    [GeneratedRegex(@"^(?<kind>[*-])\s+(?<name>.+?)\s+(?<package>[a-zA-Z_][a-zA-Z0-9_]*(?:\.[a-zA-Z_][a-zA-Z0-9_]*)+)\s*$")]
    private static partial Regex AppPattern();
}
