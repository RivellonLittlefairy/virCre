using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace VirCam.Core;

public sealed record ConnectionInfo(string Session,int Port,int DeviceId,int DisplayId,string Serial,int Width=640,int Height=480,int Format=FrameProtocol.FormatPng,bool SupportsRepeat=false,bool SupportsHoldLastFrame=false) {
    public static ConnectionInfo Load(string path) {
        var info=JsonSerializer.Deserialize<ConnectionInfo>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})
            ?? throw new InvalidDataException("Missing connection metadata");
        if(info.Port<1 || info.Port>65535 || info.DeviceId<=0 || info.DisplayId<=0 || !FrameProtocol.IsSupportedImage(info.Width,info.Height,info.Format)) throw new InvalidDataException("Invalid connection metadata");
        return info;
    }
}

public sealed class FrameClient(ConnectionInfo info) : IDisposable {
    private readonly TcpClient socket=new();
    private long sequence;
    private byte[]? latestImage;
    public bool SupportsRepeat=>info.SupportsRepeat;
    private readonly SemaphoreSlim gate=new(1,1);
    public async Task ConnectAsync(CancellationToken token) {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(3000);
        await socket.ConnectAsync(IPAddress.Loopback,info.Port,timeout.Token);
        socket.NoDelay=true;
        await ExchangeAsync(FrameProtocol.Control(MessageType.Hello,info.Session,info.DeviceId,0),timeout.Token);
    }
    public async Task<long> SendFrameAsync(byte[] image,long capturedUtcMs,CancellationToken token) {
        await gate.WaitAsync(token);
        try {
            var seq=++sequence;
            await ExchangeAsync(new Packet(MessageType.Frame,info.Session,info.DeviceId,seq,capturedUtcMs,info.Width,info.Height,0,info.Format,image),token);
            latestImage=image;
            return seq;
        } finally { gate.Release(); }
    }
    public async Task<long> RepeatFrameAsync(long capturedUtcMs,CancellationToken token) {
        await gate.WaitAsync(token);
        try {
            if(latestImage==null)throw new InvalidOperationException("A complete frame is required before Repeat.");
            long seq=++sequence;
            var packet=info.SupportsRepeat ? FrameProtocol.Control(MessageType.Repeat,info.Session,info.DeviceId,seq) :
                new Packet(MessageType.Frame,info.Session,info.DeviceId,seq,capturedUtcMs,info.Width,info.Height,0,info.Format,latestImage);
            await ExchangeAsync(packet,token);return seq;
        } finally {gate.Release();}
    }
    public async Task HoldAsync(CancellationToken token) {
        if(!info.SupportsHoldLastFrame) {await PauseAsync(token);return;}
        await gate.WaitAsync(token);
        try {await ExchangeAsync(FrameProtocol.Control(MessageType.Hold,info.Session,info.DeviceId,++sequence),token);}
        finally {gate.Release();}
    }
    public async Task PauseAsync(CancellationToken token) {
        await gate.WaitAsync(token);
        try { latestImage=null;await ExchangeAsync(FrameProtocol.Control(MessageType.Pause,info.Session,info.DeviceId,++sequence),token); }
        finally { gate.Release(); }
    }
    private async Task ExchangeAsync(Packet packet,CancellationToken token) {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(3000);
        var stream=socket.GetStream();
        await FrameProtocol.WriteAsync(stream,packet,timeout.Token);
        var ack=await FrameProtocol.ReadAsync(stream,info.Session,info.DeviceId,timeout.Token);
        if(ack.Type!=MessageType.Ack || ack.Sequence!=packet.Sequence) throw new InvalidDataException("Unexpected frame ACK");
    }
    public void Dispose() { socket.Dispose(); gate.Dispose(); }
}
