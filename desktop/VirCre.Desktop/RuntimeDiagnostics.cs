using System.Text.Json;

namespace VirCre.Desktop;

/// <summary>Bounded operation metadata. No phone frames, clipboard text, or keyboard input.</summary>
internal static class RuntimeDiagnostics {
    private static readonly object Gate=new();
    public static void Write(string operation,Exception? error=null,string? details=null) {
        lock(Gate) {
            try {
                var directory=Path.Combine(MainWindow.DataDirectory,"logs");Directory.CreateDirectory(directory);
                var path=Path.Combine(directory,"desktop.jsonl");
                if(File.Exists(path) && new FileInfo(path).Length>256*1024) File.Move(path,path+".previous",true);
                File.AppendAllText(path,JsonSerializer.Serialize(new {time=DateTimeOffset.Now,operation,details,
                    error=error?.GetType().FullName,hresult=error==null?null:"0x"+error.HResult.ToString("X8"),message=error?.Message,stack=error?.StackTrace})+Environment.NewLine);
            } catch(Exception failure) when(failure is IOException or UnauthorizedAccessException) { }
        }
    }
}
