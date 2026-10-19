using System.Runtime.InteropServices;
using VirCre.Launcher;

namespace VirCre.Desktop;

/// <summary>Creates ordinary Explorer shortcuts. Links contain data-only application arguments.</summary>
internal static class ShortcutFiles {
    public static ShortcutCreation CreateWithFallback(ShortcutRequest request,string? pngPath,string desktop,string staging) {
        var staged=Create(request,pngPath,staging);
        RuntimeDiagnostics.Write("shortcut-staged",details:staged);
        try {
            var copied=NativeShell.CopyFile(staged,desktop);
            RuntimeDiagnostics.Write("shortcut-desktop-created",details:copied);
            return new(copied,false);
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or COMException) {
            RuntimeDiagnostics.Write("shortcut-desktop-copy-failed",error,desktop);
            return new(staged,true);
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
        if(pngPath!=null && File.Exists(pngPath)) {
            var bytes=File.ReadAllBytes(pngPath);
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
