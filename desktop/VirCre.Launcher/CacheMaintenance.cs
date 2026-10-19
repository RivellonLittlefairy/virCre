namespace VirCre.Launcher;

/// <summary>Best-effort pruning of our immediate GUID directories, without traversing links or user folders.</summary>
public static class CacheMaintenance {
    public static void Prune(string root,int keep,TimeSpan maxAge) {
        try {
            if(!Directory.Exists(root)) return;
            var candidates=new DirectoryInfo(root).EnumerateDirectories()
                .Where(d=>Guid.TryParseExact(d.Name,"N",out _) && (d.Attributes&FileAttributes.ReparsePoint)==0)
                .OrderByDescending(d=>d.CreationTimeUtc).ToArray();
            for(var i=0;i<candidates.Length;i++) {
                var dir=candidates[i];
                if(i<keep && DateTime.UtcNow-dir.CreationTimeUtc<=maxAge) continue;
                try {
                    // No recursive deletion. Unexpected descendants and non-cache files remain untouched.
                    foreach(var file in dir.EnumerateFiles().Where(f=>(f.Attributes&FileAttributes.ReparsePoint)==0 && f.Extension is ".png" or ".lnk")) file.Delete();
                    if(!dir.EnumerateFileSystemInfos().Any()) dir.Delete();
                } catch(Exception e) when(e is IOException or UnauthorizedAccessException) { }
            }
        } catch(Exception e) when(e is IOException or UnauthorizedAccessException) { }
    }
}
