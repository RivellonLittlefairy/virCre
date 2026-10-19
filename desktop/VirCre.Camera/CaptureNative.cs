using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace VirCre.Camera;

internal static class CaptureNative {
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left,Top,Right,Bottom; public readonly int Width=>Right-Left; public readonly int Height=>Bottom-Top; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor,Work; public uint Flags; }
    internal readonly record struct Region(IntPtr Monitor,Rect MonitorBounds,Rect ClientBounds);

    internal static Region GetRegion(IntPtr window) {
        // WinUI is DPI aware, but the pump runs on pool threads: use physical coordinates explicitly.
        var previous=SetThreadDpiAwarenessContext(new IntPtr(-4));
        try {
            RequireInputDesktop();
            if(window==IntPtr.Zero || !IsWindow(window) || !IsWindowVisible(window) || IsIconic(window))
                throw new IOException("投屏窗口不可见或已最小化。还原窗口后点击继续。");
            if(DwmGetWindowAttribute(window,14,out var cloaked,sizeof(int))!=0 || cloaked!=0)
                throw new IOException("投屏窗口不在当前可见的 Windows 桌面，请切回后点击继续。");
            if(!GetWindowDisplayAffinity(window,out var affinity) || affinity!=0x11)
                throw new IOException("投屏窗口未能排除自身采集，已停止发送，避免画面递归。");
            if(!GetClientRect(window,out var client))throw new IOException("无法读取窗口区域。");
            var point=new Point();
            if(!ClientToScreen(window,ref point))throw new IOException("无法读取窗口位置。");
            var bounds=new Rect {Left=point.X,Top=point.Y,Right=point.X+client.Width,Bottom=point.Y+client.Height};
            var monitor=MonitorFromWindow(window,2);
            var info=new MonitorInfo {Size=Marshal.SizeOf<MonitorInfo>()};
            if(monitor==IntPtr.Zero || !GetMonitorInfo(monitor,ref info))throw new IOException("无法读取显示器。");
            if(bounds.Width<=0 || bounds.Height<=0 || bounds.Left<info.Monitor.Left || bounds.Top<info.Monitor.Top || bounds.Right>info.Monitor.Right || bounds.Bottom>info.Monitor.Bottom)
                throw new IOException("请将整个投屏画面移入同一显示器，再点击继续。");
            return new(monitor,info.Monitor,bounds);
        } finally {if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
    }

    private static void RequireInputDesktop() {
        var input=OpenInputDesktop(0,false,1); // DESKTOP_READOBJECTS, no switching or policy changes.
        if(input==IntPtr.Zero)throw new IOException("Windows 桌面已锁定或暂时不可访问，解锁后点击继续。");
        try {
            var current=GetThreadDesktop(GetCurrentThreadId());
            var inputName=new System.Text.StringBuilder(256);
            var currentName=new System.Text.StringBuilder(256);
            if(!GetUserObjectInformation(input,2,inputName,512,out _) || !GetUserObjectInformation(current,2,currentName,512,out _) ||
                !string.Equals(inputName.ToString(),currentName.ToString(),StringComparison.Ordinal))
                throw new IOException("Windows 已切换到锁屏或安全桌面，返回后点击继续。");
        } finally {CloseDesktop(input);}
    }

    internal static IDirect3DDevice CreateDevice() {
        IntPtr device=IntPtr.Zero,context=IntPtr.Zero,dxgi=IntPtr.Zero,inspectable=IntPtr.Zero;
        try {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero,1,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out device,out _,out context));
            var iid=new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device,ref iid,out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi,out inspectable));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        } finally {Release(inspectable);Release(dxgi);Release(context);Release(device);}
    }
    internal static unsafe GraphicsCaptureItem CreateMonitorItem(IntPtr monitor) {
        IntPtr name=IntPtr.Zero,factory=IntPtr.Zero,item=IntPtr.Zero;
        try {
            const string className="Windows.Graphics.Capture.GraphicsCaptureItem";
            Marshal.ThrowExceptionForHR(WindowsCreateString(className,className.Length,out name));
            var iid=new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name,ref iid,out factory));
            var itemIid=new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
            var vtable=*(IntPtr**)factory;
            var create=(delegate* unmanaged[Stdcall]<IntPtr,IntPtr,Guid*,IntPtr*,int>)vtable[4];
            IntPtr result;
            Marshal.ThrowExceptionForHR(create(factory,monitor,&itemIid,&result));
            item=result;
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(item);
        } finally {Release(item);Release(factory);if(name!=IntPtr.Zero)WindowsDeleteString(name);}
    }
    private static void Release(IntPtr value) {if(value!=IntPtr.Zero)Marshal.Release(value);}
    [DllImport("d3d11.dll")]private static extern int D3D11CreateDevice(IntPtr adapter,int driver,IntPtr software,uint flags,IntPtr levels,uint count,uint version,out IntPtr device,out int level,out IntPtr context);
    [DllImport("d3d11.dll")]private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr device,out IntPtr graphicsDevice);
    [DllImport("combase.dll",CharSet=CharSet.Unicode)]private static extern int WindowsCreateString(string source,int length,out IntPtr value);
    [DllImport("combase.dll")]private static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll")]private static extern int RoGetActivationFactory(IntPtr name,ref Guid iid,out IntPtr factory);
    [DllImport("user32.dll")]private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr window,ref Point point);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]private static extern bool GetWindowDisplayAffinity(IntPtr window,out uint affinity);
    [DllImport("user32.dll")]private static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")]private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")]private static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
    [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool GetUserObjectInformation(IntPtr handle,int index,System.Text.StringBuilder value,int length,out int needed);
}
