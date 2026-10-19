using System.Diagnostics;

namespace VirCre.Launcher;

/// <summary>Uses the release's own client, server and ADB. Never selects another version from PATH.</summary>
public sealed class BundledTools(string? applicationDirectory=null) {
    public string DirectoryPath {get;}=Path.Combine(Path.GetFullPath(applicationDirectory??AppContext.BaseDirectory),"runtime","scrcpy");
    public ProcessStartInfo CreateStartInfo(string file) {
        if(file is not ("adb" or "scrcpy")) return new(file);
        var executable=Path.Combine(DirectoryPath,file+".exe");
        Require(executable);
        foreach(var dependency in new[]{"adb.exe","AdbWinApi.dll","AdbWinUsbApi.dll"}) Require(Path.Combine(DirectoryPath,dependency));
        if(file=="scrcpy") {
            foreach(var dependency in new[]{"scrcpy-server","SDL3.dll","avcodec-63.dll","avformat-63.dll","avutil-61.dll","swresample-7.dll","libusb-1.0.dll"})
                Require(Path.Combine(DirectoryPath,dependency));
        }
        var start=new ProcessStartInfo(executable);
        start.Environment["ADB"]=Path.Combine(DirectoryPath,"adb.exe");
        start.Environment["SCRCPY_SERVER_PATH"]=Path.Combine(DirectoryPath,"scrcpy-server");
        start.Environment["SCRCPY_ICON_DIR"]=DirectoryPath;
        return start;
    }
    private static void Require(string path) {
        if(!File.Exists(path)) throw new FileNotFoundException("内置手机连接组件不完整，请重新解压完整的 virCre 发布目录。缺少："+Path.GetFileName(path),path);
    }
}
