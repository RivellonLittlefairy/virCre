using System.Runtime.InteropServices;
using VirCre.Desktop;
using VirCre.Launcher;

var directory=Path.Combine(AppContext.BaseDirectory,"vircre-shortcuts-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try {
    var payload=new ShortcutRequest("phone.one","hardware","手机","com.app.test","设置 / 测试");
    var png=Path.Combine(directory,Guid.NewGuid().ToString("N")+".png");
    File.WriteAllBytes(png,Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg=="));
    try {
        var path=ShortcutFiles.Create(payload,png,directory);
        var second=ShortcutFiles.Create(payload,png,directory);
        Check(path!=second && File.Exists(path),"shortcut creation never overwrites existing link");
        var native=ShellLinkFile.Read(path);
        Check(native.Target==Environment.ProcessPath && native.Arguments=="--open "+payload.Encode(),"Windows IShellLink reads exact target and structured arguments");
        var data=NativeFileDrag.CreateDataObject(path);
        var format=new System.Runtime.InteropServices.ComTypes.FORMATETC {cfFormat=15,dwAspect=System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,lindex=-1,tymed=System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL};
        Check(data.QueryGetData(ref format)==0,"native drag advertises the shell file-drop format");
        data.GetData(ref format,out var medium);
        try {
            var name=new System.Text.StringBuilder(1024);
            Check(DragQueryFile(medium.unionmember,0,name,name.Capacity)>0 && name.ToString()==path,"Windows Shell reads a Unicode lnk path from native drag data");
        } finally {ReleaseStgMedium(ref medium);}
        Check(NativeFileDrag.ContinueDrag(false,1)==0 && NativeFileDrag.ContinueDrag(false,0)==0x40100 && NativeFileDrag.ContinueDrag(true,1)==0x40101,"native drag continues while pressed, drops on release, cancels on Escape");
        var dropSource=NativeFileDrag.CreateDropSource(()=>false);
        var unknown=Marshal.GetIUnknownForObject(dropSource);
        var iid=new Guid("00000121-0000-0000-C000-000000000046");
        try {var hr=Marshal.QueryInterface(unknown,ref iid,out var pointer);try {Check(hr==0,"native OLE can query the drop-source COM interface");} finally {if(pointer!=IntPtr.Zero)Marshal.Release(pointer);}}
        finally {Marshal.Release(unknown);}
        var copied=NativeShell.CopyFile(path,Path.Combine(directory,"copied"));
        Check(File.Exists(copied) && ShellLinkFile.Read(copied)==native,"Windows IFileOperation copies a valid shell link");
        var copiedAgain=NativeShell.CopyFile(path,Path.Combine(directory,"copied"));
        Check(copiedAgain!=copied && File.Exists(copied),"shell copying never overwrites an existing shortcut");
        if(args.Contains("--desktop-probe")) {
            var desktopCopy=NativeShell.CopyFile(path,Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            try {Check(File.Exists(desktopCopy),"actual apphost can place a shortcut on Desktop through IFileOperation");}
            finally {File.Delete(desktopCopy);}
        }
        var deniedDestination=Path.Combine(directory,"not-a-directory");File.WriteAllText(deniedDestination,"blocked destination");
        var staged=ShortcutFiles.CreateWithFallback(payload,png,deniedDestination,Path.Combine(directory,"staging"));
        Check(staged.Staged && File.Exists(staged.Path),"unwritable desktop still produces a usable shortcut in the app data directory");
        object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true)!)!;
        object link=((dynamic)shell).CreateShortcut(path);
        try {
            dynamic shortcut=link;
            Check((string)shortcut.TargetPath==Environment.ProcessPath,"shortcut targets the current executable");
            Check(ActivationArguments.ParseCommandLine((string)shortcut.Arguments)==payload.Encode(),"shortcut arguments are valid without an executable prefix");
            Check(ActivationArguments.ParseCommandLine("\"C:\\Program Files\\virCre\\app.exe\" "+(string)shortcut.Arguments)==payload.Encode(),"redirected quoted executable preserves the payload");
            Check(File.Exists(((string)shortcut.IconLocation).Split(',')[0]),"shortcut icon persists outside drag staging");
        } finally {Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
        Check(ActivationArguments.Parse(["--open"])=="invalid" && ActivationArguments.Parse(["--open","a","b"])=="invalid","extra or missing launch data rejected");
        Console.WriteLine("PASS: Windows shortcut checks");
    } finally {File.Delete(png);}
} finally {if(Directory.Exists(directory)) Directory.Delete(directory,true);if(Directory.Exists(MainWindow.DataDirectory)) Directory.Delete(MainWindow.DataDirectory,true);}
static void Check(bool value,string message) {if(!value) throw new Exception(message);Console.WriteLine("PASS: "+message);}
[DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern uint DragQueryFile(IntPtr drop,uint index,System.Text.StringBuilder file,int count);
[DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);
namespace VirCre.Desktop {internal static class MainWindow {internal static readonly string DataDirectory=Path.Combine(AppContext.BaseDirectory,"vircre-test-data-"+Guid.NewGuid().ToString("N"));}}
