namespace VirCre.Launcher;

/// <summary>Uses only directories the current process can actually write; never changes filesystem permissions.</summary>
public sealed record DataLocation(string Path,bool UsedFallback,string? Warning) {
    public static DataLocation Resolve(string preferred,string fallback) {
        if(File.Exists(System.IO.Path.Combine(fallback,"settings.json")) && CanWrite(fallback))
            return new(fallback,true,"继续使用程序目录中的 virCre-data 保存图标和设置。");
        if(CanWrite(preferred)) return new(preferred,false,null);
        if(CanWrite(fallback)) return new(fallback,true,"本地应用数据目录不可写，已改用程序目录中的 virCre-data 保存图标和设置。");
        return new(preferred,false,"应用数据目录和程序目录均不可写，无法保存图标、设置与快捷方式。请将完整程序目录放到可写位置后重试。");
    }
    private static bool CanWrite(string directory) {
        var probe=System.IO.Path.Combine(directory,".write-probe-"+Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(directory);
            using var stream=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose);
            stream.WriteByte(0);return true;
        } catch(Exception error) when(error is IOException or UnauthorizedAccessException) {return false;}
    }
}
