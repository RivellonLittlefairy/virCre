using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace VirCam.Desktop;

internal static class DesktopCapture {
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle,out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    public static Rectangle WindowBounds(IntPtr handle) {
        if(handle==IntPtr.Zero || IsIconic(handle) || !GetWindowRect(handle,out var r)) return Rectangle.Empty;
        return Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);
    }
    public static Bitmap Region(Rectangle source,Rectangle controller,IntPtr mirror) {
        if(!SystemInformation.VirtualScreen.Contains(source)) throw new InvalidOperationException("采集区域已离开当前桌面，请重新选择");
        if(source.IntersectsWith(controller) || source.IntersectsWith(WindowBounds(mirror)))
            throw new InvalidOperationException("控制窗口或投屏窗口挡住了采集区域。已暂停，请移开窗口再开始。");
        using var raw=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(raw)) graphics.CopyFromScreen(source.Location,Point.Empty,source.Size,CopyPixelOperation.SourceCopy);
        return Fit(raw);
    }
    public static Bitmap FileImage(string path) { using var raw=Image.FromFile(path);return Fit(raw); }
    private static Bitmap Fit(Image image) {
        var target=new Bitmap(640,480,PixelFormat.Format32bppArgb);
        using var graphics=Graphics.FromImage(target);graphics.Clear(Color.Black);
        graphics.InterpolationMode=InterpolationMode.NearestNeighbor;graphics.PixelOffsetMode=PixelOffsetMode.Half;
        var scale=Math.Min(640d/image.Width,480d/image.Height);
        int w=Math.Max(1,(int)Math.Round(image.Width*scale)),h=Math.Max(1,(int)Math.Round(image.Height*scale));
        graphics.DrawImage(image,new Rectangle((640-w)/2,(480-h)/2,w,h));return target;
    }
    public static byte[] Png(Bitmap image) { using var stream=new MemoryStream();image.Save(stream,ImageFormat.Png);return stream.ToArray(); }
}
