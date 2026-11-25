using System.Globalization;
using System.Text;

namespace VirCre.Launcher;

/// <summary>Reads track-devices frames: four hexadecimal length bytes followed by the complete UTF-8 snapshot.</summary>
public sealed class AdbDeviceSnapshot {
    private string? previous;
    public async Task<bool> ReadAsync(Stream stream,CancellationToken token) {
        var header=new byte[4];
        await stream.ReadExactlyAsync(header,token).ConfigureAwait(false);
        if(!ushort.TryParse(Encoding.ASCII.GetString(header),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out var length))
            throw new IOException("ADB 设备监听返回了无效的数据长度。");
        var payload=new byte[length];
        await stream.ReadExactlyAsync(payload,token).ConfigureAwait(false);
        var lines=Encoding.UTF8.GetString(payload).Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var next=string.Join('\n',lines.Order(StringComparer.Ordinal));
        if(next==previous)return false;
        previous=next;
        return true;
    }
}
