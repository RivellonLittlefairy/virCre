using VirCre.Launcher;
using System.Text;

static class DeviceMonitorChecks {
    public static async Task RunAsync() {
        var snapshots=new AdbDeviceSnapshot();
        Require(await Read(""),"An initial empty snapshot must establish the disconnected state");
        Require(!await Read(""),"An unchanged empty snapshot must not trigger a refresh");
        Require(await Read("phone.a\tunauthorized\n"),"A phone awaiting authorization must trigger a refresh");
        Require(await Read("phone.a\tdevice model:Phone\nphone.b\toffline\n"),"Authorization and transport changes must trigger a refresh");
        Require(!await Read("phone.b\toffline\nphone.a\tdevice model:Phone\n"),"Reordering the same transports must not rescan devices");
        Require(await Read(""),"Disconnecting all phones must trigger a refresh");
        var truncated=false;
        try {using var stream=new MemoryStream(Encoding.ASCII.GetBytes("0004ab"));await snapshots.ReadAsync(stream,default);}
        catch(EndOfStreamException) {truncated=true;}
        Require(truncated,"A partial payload must be rejected without publishing an incomplete snapshot");
        using(var joined=new MemoryStream(Encoding.ASCII.GetBytes("00000000"))) {
            var reader=new AdbDeviceSnapshot();
            Require(await reader.ReadAsync(joined,default) && !await reader.ReadAsync(joined,default),"Consecutive empty frames have no newline delimiter");
        }
        Console.WriteLine("PASS: ADB device snapshots notify only complete connection changes");
        async Task<bool> Read(string payload) {
            var bytes=Encoding.UTF8.GetBytes(payload);
            using var stream=new MemoryStream(Encoding.ASCII.GetBytes(bytes.Length.ToString("x4")).Concat(bytes).ToArray());
            return await snapshots.ReadAsync(stream,default);
        }
    }
    private static void Require(bool condition,string message) {if(!condition)throw new Exception(message);}
}
