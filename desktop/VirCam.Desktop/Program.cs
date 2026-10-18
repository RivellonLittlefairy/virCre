namespace VirCam.Desktop;
internal static class Program {
    [STAThread] static void Main(string[] args) {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        var root=args.Length>0?args[0]:FindRoot();
        var powershell=args.Length>1?args[1]:"pwsh";
        Application.Run(new MainForm(root,powershell));
    }
    private static string FindRoot() {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory!=null;directory=directory.Parent)
            if(File.Exists(Path.Combine(directory.FullName,"tools","Start-P0.ps1"))) return directory.FullName;
        return Environment.CurrentDirectory;
    }
}
