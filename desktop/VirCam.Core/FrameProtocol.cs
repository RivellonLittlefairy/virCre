using System.Buffers.Binary;
using System.Security.Cryptography;

namespace VirCam.Core;

public enum MessageType : ushort { Hello=1, Frame=2, Pause=3, Stop=4, Ack=5 }
public sealed record Packet(MessageType Type,string Session,int DeviceId,long Sequence,long CapturedUtcMs,
    int Width,int Height,int Rotation,int Format,byte[] Payload);

public static class FrameProtocol {
    public const int HeaderSize=64, MaxPayload=4*1024*1024;
    public const int MaxImageDimension=4096, MaxImagePixels=4096*2160, FormatPng=1, FormatJpeg=2;
    public static bool IsSupportedImage(int width,int height,int format)=>
        width>0 && height>0 && width<=MaxImageDimension && height<=MaxImageDimension &&
        (long)width*height<=MaxImagePixels && format is FormatPng or FormatJpeg;
    public static Packet Control(MessageType type,string session,int device,long sequence) => new(type,session,device,sequence,0,0,0,0,0,[]);
    public static byte[] Encode(Packet p) {
        Validate(p.Type,p.Payload.Length,p.DeviceId,p.Sequence,p.CapturedUtcMs,p.Width,p.Height,p.Rotation,p.Format);
        var bytes=new byte[HeaderSize]; var b=bytes.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(b,0x56435231);
        BinaryPrimitives.WriteUInt16BigEndian(b[4..],1);
        BinaryPrimitives.WriteUInt16BigEndian(b[6..],(ushort)p.Type);
        BinaryPrimitives.WriteInt32BigEndian(b[8..],p.Payload.Length);
        SessionBytes(p.Session).CopyTo(b[12..]);
        BinaryPrimitives.WriteInt32BigEndian(b[28..],p.DeviceId);
        BinaryPrimitives.WriteInt64BigEndian(b[32..],p.Sequence);
        BinaryPrimitives.WriteInt64BigEndian(b[40..],p.CapturedUtcMs);
        BinaryPrimitives.WriteInt32BigEndian(b[48..],p.Width);
        BinaryPrimitives.WriteInt32BigEndian(b[52..],p.Height);
        BinaryPrimitives.WriteInt32BigEndian(b[56..],p.Rotation);
        BinaryPrimitives.WriteInt32BigEndian(b[60..],p.Format);
        return bytes;
    }
    public static Packet Decode(byte[] bytes,string session,int device) {
        if(bytes.Length!=HeaderSize) throw new InvalidDataException("Truncated header");
        var b=bytes.AsSpan();
        if(BinaryPrimitives.ReadUInt32BigEndian(b)!=0x56435231 || BinaryPrimitives.ReadUInt16BigEndian(b[4..])!=1)
            throw new InvalidDataException("Unsupported frame protocol");
        var type=(MessageType)BinaryPrimitives.ReadUInt16BigEndian(b[6..]);
        var length=BinaryPrimitives.ReadInt32BigEndian(b[8..]);
        var actualDevice=BinaryPrimitives.ReadInt32BigEndian(b[28..]);
        if(actualDevice!=device || !CryptographicOperations.FixedTimeEquals(b.Slice(12,16),SessionBytes(session)))
            throw new InvalidDataException("Session/device mismatch");
        var seq=BinaryPrimitives.ReadInt64BigEndian(b[32..]); var captured=BinaryPrimitives.ReadInt64BigEndian(b[40..]);
        var w=BinaryPrimitives.ReadInt32BigEndian(b[48..]);var h=BinaryPrimitives.ReadInt32BigEndian(b[52..]);
        var rotation=BinaryPrimitives.ReadInt32BigEndian(b[56..]);var format=BinaryPrimitives.ReadInt32BigEndian(b[60..]);
        Validate(type,length,actualDevice,seq,captured,w,h,rotation,format);
        return new(type,session,device,seq,captured,w,h,rotation,format,new byte[length]);
    }
    private static byte[] SessionBytes(string session) {
        if(session.Length!=32 || session.Any(c=>!"0123456789abcdef".Contains(c))) throw new InvalidDataException("Invalid session value");
        return Convert.FromHexString(session);
    }
    private static void Validate(MessageType type,int length,int device,long sequence,long captured,int w,int h,int rotation,int format) {
        if(type<MessageType.Hello || type>MessageType.Ack || length<0 || length>MaxPayload || device<=0 || sequence<0 || captured<0)
            throw new InvalidDataException("Invalid frame fields");
        if(type==MessageType.Frame) {
            if(length==0 || sequence==0 || !IsSupportedImage(w,h,format) || rotation!=0)
                throw new InvalidDataException("Expected supported PNG/JPEG image dimensions, rotation 0");
        } else if(length!=0 || w!=0 || h!=0 || rotation!=0 || format!=0 || (type==MessageType.Hello && sequence!=0))
            throw new InvalidDataException("Invalid control packet");
    }
    public static async Task WriteAsync(Stream stream,Packet packet,CancellationToken token) {
        await stream.WriteAsync(Encode(packet),token);
        if(packet.Payload.Length>0) await stream.WriteAsync(packet.Payload,token);
        await stream.FlushAsync(token);
    }
    public static async Task<Packet> ReadAsync(Stream stream,string session,int device,CancellationToken token) {
        var header=new byte[HeaderSize]; await stream.ReadExactlyAsync(header,token);
        var packet=Decode(header,session,device);
        if(packet.Payload.Length>0) await stream.ReadExactlyAsync(packet.Payload,token);
        return packet;
    }
}
