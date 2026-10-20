using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using VirCam.Core;

namespace VirCre.Camera;

/// <summary>Single pump owner. Composes the intersecting monitors with scrcpy excluded from capture.</summary>
internal sealed class WindowBackgroundCapture : IDisposable {
    private readonly IDirect3DDevice device;
    private readonly Dictionary<IntPtr,MonitorCapture> monitors=[];
    private readonly int outputWidth,outputHeight;
    private byte[]? cropped;

    internal WindowBackgroundCapture(int width,int height) {
        if(!FrameProtocol.IsSupportedImage(width,height,FrameProtocol.FormatJpeg))throw new IOException("投屏相机尺寸超出支持范围。");
        if(!GraphicsCaptureSession.IsSupported())throw new PlatformNotSupportedException("此 Windows 不支持桌面画面采集。");
        outputWidth=width;outputHeight=height;
        device=CaptureNative.CreateDevice();
    }

    internal async Task<byte[]?> CaptureAsync(IntPtr window,CancellationToken token) {
        token.ThrowIfCancellationRequested();
        var region=CaptureNative.GetRegion(window);
        var wanted=region.Monitors.Select(value=>value.Handle).ToHashSet();
        foreach(var handle in monitors.Keys.Where(handle=>!wanted.Contains(handle)).ToArray()) {
            Release(monitors[handle]);monitors.Remove(handle);
        }
        foreach(var monitor in region.Monitors) {
            if(monitors.TryGetValue(monitor.Handle,out var current) && !current.Matches(monitor)) {
                Release(current);monitors.Remove(monitor.Handle);current=null;
            }
            if(current==null) {current=new(device,monitor);monitors.Add(monitor.Handle,current);}
            await current.UpdateAsync(token).ConfigureAwait(false);
        }
        // Recheck after the asynchronous GPU copy: never crop a stale window position.
        // A monitor newly crossed during the copy is prepared on the next pump iteration.
        region=CaptureNative.GetRegion(window);
        if(region.Monitors.Count==0)return null;
        foreach(var monitor in region.Monitors)
            if(!monitors.TryGetValue(monitor.Handle,out var source) || !source.Matches(monitor) || source.Pixels==null)return null;
        var client=ContentBounds(region.ClientBounds);
        // Bound the native-resolution composition buffer, including windows extended off-screen.
        if((long)client.Width*client.Height>32L*1024*1024)throw new IOException("窗口采集区域过大，缩小窗口后自动恢复。");
        int cropLength=checked(client.Width*client.Height*4);
        if(cropped==null || cropped.Length!=cropLength)cropped=new byte[cropLength];
        else Array.Clear(cropped);
        // Compose in physical desktop coordinates. Parts outside all monitors remain
        // black in their original positions, rather than stretching the visible part.
        foreach(var monitor in region.Monitors) {
            var overlap=CaptureNative.Intersect(client,monitor.Bounds);
            if(overlap.Width<=0 || overlap.Height<=0)continue;
            var pixels=monitors[monitor.Handle].Pixels!;
            int sourceX=overlap.Left-monitor.Bounds.Left,sourceY=overlap.Top-monitor.Bounds.Top;
            int targetX=overlap.Left-client.Left,targetY=overlap.Top-client.Top;
            int rowBytes=checked(overlap.Width*4);
            for(int y=0;y<overlap.Height;y++) {
                token.ThrowIfCancellationRequested();
                System.Buffer.BlockCopy(pixels,checked(((sourceY+y)*monitor.Bounds.Width+sourceX)*4),
                    cropped,checked(((targetY+y)*client.Width+targetX)*4),rowBytes);
            }
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

    public void Dispose() {
        try {foreach(var monitor in monitors.Values)Release(monitor);}
        finally {monitors.Clear();cropped=null;Release(device);}
    }
    private static void Release(IDisposable resource) {
        try {resource.Dispose();}
        catch(System.Runtime.InteropServices.COMException) { /* A removed display/device may already be closed. */ }
        catch(ObjectDisposedException) { /* Continue releasing the other monitor resources. */ }
    }

    private sealed class MonitorCapture : IDisposable {
        private readonly GraphicsCaptureItem item;
        private readonly Direct3D11CaptureFramePool pool;
        private readonly GraphicsCaptureSession session;
        private readonly CaptureNative.MonitorRegion monitor;
        private readonly long started=System.Diagnostics.Stopwatch.GetTimestamp();
        private Windows.Storage.Streams.Buffer? staging;
        private volatile bool closed;
        internal byte[]? Pixels {get;private set;}

        internal MonitorCapture(IDirect3DDevice device,CaptureNative.MonitorRegion monitor) {
            this.monitor=monitor;
            item=CaptureNative.CreateMonitorItem(monitor.Handle);
            item.Closed+=ItemClosed;
            try {
                pool=Direct3D11CaptureFramePool.CreateFreeThreaded(device,DirectXPixelFormat.B8G8R8A8UIntNormalized,2,item.Size);
                try {
                    session=pool.CreateCaptureSession(item);
                    try {session.IsCursorCaptureEnabled=false;session.StartCapture();}
                    catch {session.Dispose();throw;}
                } catch {pool.Dispose();throw;}
            } catch {item.Closed-=ItemClosed;throw;}
        }
        private void ItemClosed(GraphicsCaptureItem sender,object args)=>closed=true;
        internal bool Matches(CaptureNative.MonitorRegion other)=>!closed && monitor.Equals(other);
        internal async Task UpdateAsync(CancellationToken token) {
            Direct3D11CaptureFrame? frame=null;
            try {
                while(pool.TryGetNextFrame() is { } next) {frame?.Dispose();frame=next;}
                if(frame!=null) {
                    if(frame.ContentSize.Width!=monitor.Bounds.Width || frame.ContentSize.Height!=monitor.Bounds.Height)
                        throw new IOException("显示器分辨率已变化，正在重新采集。");
                    using var bitmap=await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface,BitmapAlphaMode.Ignore).AsTask(token).ConfigureAwait(false);
                    if(bitmap.PixelWidth!=monitor.Bounds.Width || bitmap.PixelHeight!=monitor.Bounds.Height)
                        throw new IOException("显示器画面尺寸正在变化。");
                    var length=checked((uint)(bitmap.PixelWidth*bitmap.PixelHeight*4));
                    staging??=new Windows.Storage.Streams.Buffer(length);
                    bitmap.CopyToBuffer(staging);
                    Pixels??=new byte[length];
                    using var reader=DataReader.FromBuffer(staging);
                    reader.ReadBytes(Pixels);
                }
            } finally {frame?.Dispose();}
            if(Pixels==null && System.Diagnostics.Stopwatch.GetElapsedTime(started)>TimeSpan.FromSeconds(5))
                throw new IOException("暂未收到显示器画面，正在重试。");
        }
        public void Dispose() {
            closed=true;Pixels=null;staging=null;
            try {item.Closed-=ItemClosed;}
            finally {Release(session);Release(pool);}
        }
    }
}
