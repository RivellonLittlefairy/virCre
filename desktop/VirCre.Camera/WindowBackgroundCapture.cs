using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using VirCam.Core;

namespace VirCre.Camera;

/// <summary>Single pump owner. Captures a monitor with the scrcpy window excluded by its own process.</summary>
internal sealed class WindowBackgroundCapture : IDisposable {
    private readonly IDirect3DDevice device;
    private readonly GraphicsCaptureItem item;
    private readonly Direct3D11CaptureFramePool pool;
    private readonly GraphicsCaptureSession session;
    private readonly CaptureNative.Region initial;
    private byte[]? desktop;
    private Windows.Storage.Streams.Buffer? staging;
    private readonly int outputWidth,outputHeight;
    private byte[]? cropped;
    private volatile bool closed;
    private readonly DateTime started=DateTime.UtcNow;

    internal WindowBackgroundCapture(CaptureNative.Region region,int width,int height) {
        if(!FrameProtocol.IsSupportedImage(width,height,FrameProtocol.FormatJpeg))throw new IOException("投屏相机尺寸超出支持范围。");
        if(!GraphicsCaptureSession.IsSupported())throw new IOException("此 Windows 不支持桌面画面采集。");
        outputWidth=width;outputHeight=height;
        initial=region;
        device=CaptureNative.CreateDevice();
        try {
            item=CaptureNative.CreateMonitorItem(region.Monitor);
            item.Closed+=ItemClosed;
            pool=Direct3D11CaptureFramePool.CreateFreeThreaded(device,DirectXPixelFormat.B8G8R8A8UIntNormalized,2,item.Size);
            try {
                session=pool.CreateCaptureSession(item);
                try {session.IsCursorCaptureEnabled=false;session.StartCapture();}
                catch {session.Dispose();throw;}
            } catch {pool.Dispose();item.Closed-=ItemClosed;throw;}
        } catch {device.Dispose();throw;}
    }
    private void ItemClosed(GraphicsCaptureItem sender,object args)=>closed=true;

    internal async Task<byte[]?> CaptureAsync(IntPtr window,CancellationToken token) {
        token.ThrowIfCancellationRequested();
        var region=CaptureNative.GetRegion(window);
        if(closed || region.Monitor!=initial.Monitor || region.MonitorBounds.Width!=initial.MonitorBounds.Width || region.MonitorBounds.Height!=initial.MonitorBounds.Height)
            throw new IOException("显示器或采集状态已变化，请点击继续重新采集。");
        Direct3D11CaptureFrame? frame=null;
        try {
            while(pool.TryGetNextFrame() is { } next) {frame?.Dispose();frame=next;}
            if(frame!=null) {
                if(frame.ContentSize.Width!=initial.MonitorBounds.Width || frame.ContentSize.Height!=initial.MonitorBounds.Height)
                    throw new IOException("显示器分辨率已变化，请点击继续重新采集。");
                using var bitmap=await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface,BitmapAlphaMode.Ignore).AsTask(token).ConfigureAwait(false);
                var length=checked((uint)(bitmap.PixelWidth*bitmap.PixelHeight*4));
                staging??=new Windows.Storage.Streams.Buffer(length);
                bitmap.CopyToBuffer(staging);
                desktop??=new byte[length];
                using var reader=DataReader.FromBuffer(staging);
                reader.ReadBytes(desktop);
            }
        } finally {frame?.Dispose();}
        if(desktop==null) {
            if(DateTime.UtcNow-started>TimeSpan.FromSeconds(5))throw new IOException("未收到桌面画面，请检查 Windows 屏幕采集权限后点击继续。");
            return null;
        }
        // Recheck after the asynchronous GPU copy: never crop a stale window position.
        region=CaptureNative.GetRegion(window);
        if(region.Monitor!=initial.Monitor)throw new IOException("投屏窗口已切换显示器，请点击继续。");
        var client=ContentBounds(region.ClientBounds);
        int sourceX=client.Left-region.MonitorBounds.Left,sourceY=client.Top-region.MonitorBounds.Top;
        int rowBytes=checked(client.Width*4),cropLength=checked(rowBytes*client.Height);
        if(cropped==null || cropped.Length!=cropLength)cropped=new byte[cropLength];
        int stride=initial.MonitorBounds.Width;
        for(int y=0;y<client.Height;y++) {
            token.ThrowIfCancellationRequested();
            System.Buffer.BlockCopy(desktop,((sourceY+y)*stride+sourceX)*4,cropped,y*rowBytes,rowBytes);
        }
        using var stream=new InMemoryRandomAccessStream();
        var properties=new BitmapPropertySet { ["ImageQuality"]=new BitmapTypedValue(0.88f,Windows.Foundation.PropertyType.Single) };
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId,stream,properties).AsTask(token).ConfigureAwait(false);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,(uint)client.Width,(uint)client.Height,96,96,cropped);
        // Native image scaling avoids a managed per-pixel loop over a full-resolution camera frame.
        encoder.BitmapTransform.ScaledWidth=(uint)outputWidth;
        encoder.BitmapTransform.ScaledHeight=(uint)outputHeight;
        encoder.BitmapTransform.InterpolationMode=BitmapInterpolationMode.Linear;
        await encoder.FlushAsync().AsTask(token).ConfigureAwait(false);
        if(stream.Size>FrameProtocol.MaxPayload)throw new IOException("桌面帧超过传输上限。");
        stream.Seek(0);
        var image=new byte[(int)stream.Size];
        using var data=new DataReader(stream.GetInputStreamAt(0));
        await data.LoadAsync((uint)image.Length).AsTask(token).ConfigureAwait(false);
        data.ReadBytes(image);
        return image;
    }
    private CaptureNative.Rect ContentBounds(CaptureNative.Rect client) {
        // scrcpy's default render-fit is letterbox. Crop only the rendered phone viewport,
        // so making a window wider does not add a second set of bars to the camera input.
        double scale=Math.Min((double)client.Width/outputWidth,(double)client.Height/outputHeight);
        int width=Math.Clamp((int)Math.Round(outputWidth*scale),1,client.Width);
        int height=Math.Clamp((int)Math.Round(outputHeight*scale),1,client.Height);
        int left=client.Left+(client.Width-width)/2,top=client.Top+(client.Height-height)/2;
        return new CaptureNative.Rect {Left=left,Top=top,Right=left+width,Bottom=top+height};
    }
    public void Dispose() {closed=true;desktop=null;staging=null;cropped=null;item.Closed-=ItemClosed;session.Dispose();pool.Dispose();device.Dispose();}
}
