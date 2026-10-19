using System.Runtime.InteropServices;

/// <summary>Checks what Explorer's icon handler renders, rather than only checking an ICO header.</summary>
internal static class ShellIconPixels {
    public static byte[] Read(string path) {
        var completion=new TaskCompletionSource<byte[]>();
        var thread=new Thread(()=>{
            var hr=CoInitializeEx(IntPtr.Zero,2);
            try {Marshal.ThrowExceptionForHR(hr);completion.SetResult(ReadCore(path));}
            catch(Exception error) {completion.SetException(error);}
            finally {if(hr>=0)CoUninitialize();}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        return completion.Task.GetAwaiter().GetResult();
    }
    private static byte[] ReadCore(string path) {
        if(SHGetFileInfo(path,0,out var file,(uint)Marshal.SizeOf<FileInfo>(),0x100)==IntPtr.Zero || file.Icon==IntPtr.Zero)
            throw new IOException("Windows did not return a shell icon: "+path);
        var icon=new IconInfo();
        try {
            if(!GetIconInfo(file.Icon,out icon) || icon.Color==IntPtr.Zero || GetObject(icon.Color,Marshal.SizeOf<Bitmap>(),out var bitmap)==0)
                throw new IOException("Cannot read rendered shell icon: "+path);
            var bytes=new byte[checked(bitmap.WidthBytes*bitmap.Height)];
            if(GetBitmapBits(icon.Color,bytes.Length,bytes)!=bytes.Length) throw new IOException("Incomplete rendered shell icon.");
            // Explorer adds a shortcut-arrow overlay. Compare the unaffected upper-right corner.
            if(bitmap.BitsPixel!=32) throw new IOException("Expected a 32-bit shell icon.");
            var visible=new List<byte>();
            for(var row=0;row<Math.Max(1,bitmap.Height/4);row++)
                visible.AddRange(bytes.AsSpan(row*bitmap.WidthBytes+bitmap.Width*3/4*4,(bitmap.Width-bitmap.Width*3/4)*4).ToArray());
            return visible.ToArray();
        } finally {if(icon.Color!=IntPtr.Zero)DeleteObject(icon.Color);if(icon.Mask!=IntPtr.Zero)DeleteObject(icon.Mask);DestroyIcon(file.Icon);}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct FileInfo {
        public IntPtr Icon;public int Index;public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string Type;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;public uint X,Y;public IntPtr Mask,Color; }
    [StructLayout(LayoutKind.Sequential)] private struct Bitmap {public int Type,Width,Height,WidthBytes;public ushort Planes,BitsPixel;public IntPtr Bits;}
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved,uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path,uint attributes,out FileInfo info,uint size,uint flags);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon,out IconInfo info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("gdi32.dll",EntryPoint="GetObjectW")] private static extern int GetObject(IntPtr bitmap,int size,out Bitmap data);
    [DllImport("gdi32.dll")] private static extern int GetBitmapBits(IntPtr bitmap,int count,[Out] byte[] data);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr bitmap);
}
