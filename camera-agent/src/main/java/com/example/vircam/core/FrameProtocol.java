package com.example.vircam.core;

import java.io.*;
import java.nio.ByteBuffer;
import java.security.MessageDigest;

/** Version 1: 64-byte big-endian header. Image pixels never enter diagnostic logs. */
public final class FrameProtocol {
    // Repeat is an additive v1 extension, sent only after inputRepeat capability advertisement.
    public static final int HELLO=1, FRAME=2, PAUSE=3, STOP=4, ACK=5, REPEAT=6, HOLD=7;
    public static final int MAX_PAYLOAD=4*1024*1024, HEADER_SIZE=64;
    public static final int MAX_IMAGE_DIMENSION=4096, MAX_IMAGE_PIXELS=4096*2160, FORMAT_PNG=1, FORMAT_JPEG=2;
    public static boolean isSupportedImage(int width,int height,int format) {
        return width>0 && height>0 && width<=MAX_IMAGE_DIMENSION && height<=MAX_IMAGE_DIMENSION
                && (long)width*height<=MAX_IMAGE_PIXELS && (format==FORMAT_PNG || format==FORMAT_JPEG);
    }
    private FrameProtocol() {}

    public static final class Packet {
        public final int type, deviceId, width, height, rotation, format;
        public final long sequence, capturedUtcMs;
        public final String session;
        public final byte[] payload;
        public Packet(int type,String session,int deviceId,long sequence,long capturedUtcMs,
                int width,int height,int rotation,int format,byte[] payload) {
            this.type=type; this.session=session; this.deviceId=deviceId; this.sequence=sequence;
            this.capturedUtcMs=capturedUtcMs; this.width=width; this.height=height;
            this.rotation=rotation; this.format=format; this.payload=payload;
        }
    }

    public static Packet control(int type,String session,int deviceId,long sequence) {
        return new Packet(type,session,deviceId,sequence,0,0,0,0,0,new byte[0]);
    }

    public static Packet read(InputStream input,String expectedSession,int expectedDevice) throws IOException {
        byte[] header=new byte[HEADER_SIZE];
        new DataInputStream(input).readFully(header);
        ByteBuffer b=ByteBuffer.wrap(header);
        if(b.getInt()!=0x56435231 || b.getShort()!=1) throw new IOException("Unsupported frame protocol");
        int type=b.getShort() & 0xffff, length=b.getInt();
        if(length<0 || length>MAX_PAYLOAD) throw new IOException("Payload length out of bounds");
        byte[] session=new byte[16]; b.get(session);
        int deviceId=b.getInt();
        if(deviceId!=expectedDevice || !MessageDigest.isEqual(session,sessionBytes(expectedSession))) {
            throw new IOException("Session/device mismatch");
        }
        long sequence=b.getLong(), captured=b.getLong();
        int width=b.getInt(),height=b.getInt(),rotation=b.getInt(),format=b.getInt();
        validate(type,length,deviceId,sequence,captured,width,height,rotation,format);
        byte[] payload=new byte[length];
        new DataInputStream(input).readFully(payload);
        return new Packet(type,expectedSession,deviceId,sequence,captured,width,height,rotation,format,payload);
    }

    public static void write(OutputStream output,Packet p) throws IOException {
        validate(p.type,p.payload.length,p.deviceId,p.sequence,p.capturedUtcMs,p.width,p.height,p.rotation,p.format);
        ByteBuffer b=ByteBuffer.allocate(HEADER_SIZE);
        b.putInt(0x56435231).putShort((short)1).putShort((short)p.type).putInt(p.payload.length)
            .put(sessionBytes(p.session)).putInt(p.deviceId).putLong(p.sequence).putLong(p.capturedUtcMs)
            .putInt(p.width).putInt(p.height).putInt(p.rotation).putInt(p.format);
        output.write(b.array()); output.write(p.payload); output.flush();
    }

    private static void validate(int type,int length,int device,long seq,long captured,int w,int h,int rotation,int format) throws IOException {
        if(type<HELLO || type>HOLD || device<=0 || seq<0 || captured<0 || length<0 || length>MAX_PAYLOAD || ((type==REPEAT || type==HOLD) && seq==0))
            throw new IOException("Invalid frame fields");
        if(type==FRAME) {
            if(length==0 || seq==0 || !isSupportedImage(w,h,format) || rotation!=0)
                throw new IOException("Expected supported PNG/JPEG image dimensions, rotation 0");
        } else if((type==HOLD && captured!=0) || length!=0 || w!=0 || h!=0 || rotation!=0 || format!=0 || (type==HELLO && seq!=0)) {
            throw new IOException("Invalid control packet");
        }
    }

    private static byte[] sessionBytes(String value) throws IOException {
        if(value==null || !value.matches("[a-f0-9]{32}")) throw new IOException("Invalid session value");
        byte[] bytes=new byte[16];
        for(int i=0;i<16;i++) bytes[i]=(byte)Integer.parseInt(value.substring(i*2,i*2+2),16);
        return bytes;
    }
}
