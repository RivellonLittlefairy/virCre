using System.Runtime.InteropServices;
using VirCre.Launcher;

namespace VirCre.Desktop;

/// <summary>Creates ordinary Explorer shortcuts. Links contain data-only application arguments.</summary>
internal static class ShortcutFiles {
    private static readonly object StagingGate=new();
    public static ShortcutCreation CreateWithFallback(ShortcutRequest request,string? pngPath,string desktop,string staging) {
        lock(StagingGate) {
            Directory.CreateDirectory(staging);
            CacheMaintenance.Prune(staging,29,TimeSpan.FromDays(7));
            PruneLegacyStaging(staging);
            var directory=Path.Combine(staging,Guid.NewGuid().ToString("N"));
            var staged=Create(request,pngPath,directory);
            RuntimeDiagnostics.Write("shortcut-staged",details:staged);
            string copied;
            try {copied=NativeShell.CopyFile(staged,desktop);}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or COMException) {
                RuntimeDiagnostics.Write("shortcut-desktop-copy-failed",error,desktop);
                return new(staged,true);
            }
            RuntimeDiagnostics.Write("shortcut-desktop-created",details:copied);
            // Copy failures keep the fallback. A cleanup failure must not report a successful copy as failed.
            try {File.Delete(staged);Directory.Delete(directory,false);}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException) {
                RuntimeDiagnostics.Write("shortcut-staging-cleanup-failed",error);
            }
            return new(copied,false);
        }
    }
    private static void PruneLegacyStaging(string root) {
        try {
            var directory=new DirectoryInfo(root);
            if((directory.Attributes&FileAttributes.ReparsePoint)!=0)return;
            var keptDirectories=directory.EnumerateDirectories()
                .Count(item=>Guid.TryParseExact(item.Name,"N",out _) && (item.Attributes&FileAttributes.ReparsePoint)==0);
            var keep=Math.Max(0,29-keptDirectories);
            // Old versions wrote flat links here. Only prune our named links, never descend into user folders.
            var files=directory.EnumerateFiles("virCre - *.lnk",SearchOption.TopDirectoryOnly)
                .Where(file=>(file.Attributes&FileAttributes.ReparsePoint)==0).OrderByDescending(file=>file.CreationTimeUtc).ToArray();
            for(var i=0;i<files.Length;i++) {
                if(i<keep && DateTime.UtcNow-files[i].CreationTimeUtc<=TimeSpan.FromDays(7))continue;
                try {files[i].Delete();}
                catch(Exception error) when(error is IOException or UnauthorizedAccessException) {RuntimeDiagnostics.Write("shortcut-legacy-cleanup-failed",error);}
            }
        } catch(Exception error) when(error is IOException or UnauthorizedAccessException) {
            RuntimeDiagnostics.Write("shortcut-legacy-cleanup-failed",error);
        }
    }
    public static string Create(ShortcutRequest request, string? pngPath, string directory) {
        Directory.CreateDirectory(directory);
        var title="virCre - "+request.Name+" - "+request.DeviceName;
        var name=new string(title.Select(ch=>Path.GetInvalidFileNameChars().Contains(ch)?'_':ch).ToArray());
        if(name.Length>100) name=name[..100];
        name=name.TrimEnd(' ','.');
        var destination=Path.Combine(directory,name+".lnk");
        for(var i=2;File.Exists(destination);i++) destination=Path.Combine(directory,name+$" ({i}).lnk");
        var temp=Path.Combine(directory,Guid.NewGuid().ToString("N")+".lnk");
        string? iconPath=null;
        if(pngPath!=null) {
            byte[] bytes;
            try {bytes=File.ReadAllBytes(pngPath);}
            catch(Exception error) when(error is FileNotFoundException or DirectoryNotFoundException) {
                throw new IOException("应用图标缓存已丢失，请刷新应用列表后重新创建快捷方式。",error);
            }
            var icons=Path.Combine(MainWindow.ShortcutDataDirectory,"shortcut-icons");
            Directory.CreateDirectory(icons);
            iconPath=Path.Combine(icons,IconCatalog.Hash(Convert.ToBase64String(bytes))+".ico");
            if(!File.Exists(iconPath)) File.WriteAllBytes(iconPath,IconFile.FromPng(bytes));
        }
        try {
            ShellLinkFile.Save(temp,Environment.ProcessPath!,"--open "+request.Encode(),AppContext.BaseDirectory,$"在 {request.DeviceName} 上打开 {request.Name} · virCre",iconPath);
            File.Move(temp,destination); // Never overwrite an existing user shortcut.
            return destination;
        } finally {
            if(File.Exists(temp)) File.Delete(temp);
        }
    }
}
internal sealed record ShortcutCreation(string Path,bool Staged);
