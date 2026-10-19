using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace VirCre.Desktop;

/// <summary>Windows Shell owns the link format and persistence; no script host or custom .lnk serialization.</summary>
internal static class ShellLinkFile {
    public static void Save(string path,string target,string arguments,string workingDirectory,string description,string? icon) {
        var instance=new ShellLink();
        try {
            var link=(IShellLinkW)instance;
            link.SetPath(target);link.SetArguments(arguments);link.SetWorkingDirectory(workingDirectory);
            link.SetDescription(description);link.SetShowCmd(1);
            if(icon!=null) link.SetIconLocation(icon,0);
            ((IPersistFile)instance).Save(path,true);
        } finally {Marshal.FinalReleaseComObject(instance);}
    }
    public static (string Target,string Arguments) Read(string path) {
        var instance=new ShellLink();
        try {
            ((IPersistFile)instance).Load(path,0);
            var link=(IShellLinkW)instance;
            var target=new StringBuilder(32768);var arguments=new StringBuilder(32768);
            link.GetPath(target,target.Capacity,IntPtr.Zero,4);link.GetArguments(arguments,arguments.Capacity);
            return(target.ToString(),arguments.ToString());
        } finally {Marshal.FinalReleaseComObject(instance);}
    }
    [ComImport,Guid("00021401-0000-0000-C000-000000000046"),ClassInterface(ClassInterfaceType.None)] private class ShellLink { }
    [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW {
        void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,int count,IntPtr findData,uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder text,int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory,int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments,int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int show);
        void SetShowCmd(int show);
        void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder icon,int count,out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon,int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path,uint reserved);
        void Resolve(IntPtr window,uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
