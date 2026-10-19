using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VirCre.Launcher;

/// <summary>Data-only startup arguments. Never executable shell text.</summary>
public sealed partial record ShortcutRequest(string Serial, string HardwareId, string DeviceName, string PackageName, string Name) {
    public string Encode() {Validate();return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this)).TrimEnd('=').Replace('+','-').Replace('/','_');}
    public static ShortcutRequest Decode(string value) {
        if (value.Length is 0 or > 8192 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new ArgumentException("快捷方式参数无效。");
        try {
            var base64 = value.Replace('-','+').Replace('_','/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            var result = JsonSerializer.Deserialize<ShortcutRequest>(Convert.FromBase64String(base64)) ?? throw new ArgumentException("快捷方式参数为空。");
            result.Validate();return result;
        } catch (Exception e) when (e is JsonException or FormatException) {throw new ArgumentException("快捷方式参数损坏。", e);}
    }
    private void Validate() {
        if (string.IsNullOrEmpty(Serial) || Serial.Length > 512 || !SerialPattern().IsMatch(Serial) ||
            string.IsNullOrEmpty(PackageName) || PackageName.Length > 255 || !PackagePattern().IsMatch(PackageName) ||
            HardwareId == null || HardwareId.Length > 256 || string.IsNullOrWhiteSpace(Name) || Name.Length > 512 ||
            string.IsNullOrWhiteSpace(DeviceName) || DeviceName.Length > 512) throw new ArgumentException("快捷方式设备或应用信息无效。");
    }
    public async Task<PhoneDevice> ResolveAsync(IReadOnlyList<PhoneDevice> devices, Func<string,CancellationToken,Task<string>> identity, CancellationToken token) {
        Validate();
        if(HardwareId.Length==0) throw new IOException("快捷方式缺少手机稳定标识，请连接手机后重新创建；不能只凭网络地址自动打开。");
        // Hardware ID takes priority even when an old address is reused by another phone.
        foreach (var device in devices.Where(d => d.State == DeviceState.Online).OrderByDescending(d => d.Serial == Serial)) {
            token.ThrowIfCancellationRequested();
            try {if ((device.HardwareId.Length>0?device.HardwareId:await identity(device.Serial, token).ConfigureAwait(false)) == HardwareId) return device;}
            catch (Exception e) when (e is IOException or TimeoutException) { }
        }
        throw new IOException($"未找到快捷方式对应的 {DeviceName}。请连接并授权该手机后重试。");
    }
    public static async Task<string> GetHardwareIdAsync(ICommandRunner runner, string serial, CancellationToken token) {
        if (!SerialPattern().IsMatch(serial)) throw new ArgumentException("设备地址无效。");
        var result = await runner.RunAsync("adb", ["-s", serial, "shell", "getprop", "ro.serialno"], TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new IOException("无法读取手机标识，请检查连接后重试。");
        var id = result.Output.Trim();
        return id.Length is > 0 and <= 256 && id != "unknown" && !id.Any(char.IsControl) ? id : "";
    }
    [GeneratedRegex(@"^[a-zA-Z0-9._:-]+$")] private static partial Regex SerialPattern();
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*(?:\.[a-zA-Z_][a-zA-Z0-9_]*)+$")] private static partial Regex PackagePattern();
}
