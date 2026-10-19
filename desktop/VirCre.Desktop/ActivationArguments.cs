using System.Runtime.InteropServices;

namespace VirCre.Desktop;

internal static class ActivationArguments {
    // Redirected launch arguments may contain the executable followed by the arguments.
    // Delegate quote processing to Windows rather than splitting paths on spaces.
    public static string? Parse(string[] arguments) {
        var index=Array.IndexOf(arguments,"--open");
        if(index<0) return arguments.Length==0?null:"invalid";
        return index==arguments.Length-2 && index<=1?arguments[index+1]:"invalid";
    }
    public static string? ParseCommandLine(string commandLine) {
        if(string.IsNullOrWhiteSpace(commandLine)) return null;
        var pointer=CommandLineToArgvW(commandLine,out var count);
        if(pointer==IntPtr.Zero) return "invalid";
        try {
            var args=Enumerable.Range(0,count).Select(i=>Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer,i*IntPtr.Size))!).ToArray();
            if(args.Length==1 && string.Equals(args[0],Environment.ProcessPath,StringComparison.OrdinalIgnoreCase)) return null;
            return Parse(args);
        } finally {LocalFree(pointer);}
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CommandLineToArgvW(string commandLine,out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
}
