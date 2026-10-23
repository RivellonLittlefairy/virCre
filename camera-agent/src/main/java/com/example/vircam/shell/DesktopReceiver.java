package com.example.vircam.shell;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.net.LocalServerSocket;
import android.net.LocalSocket;
import android.os.SystemClock;
import com.example.vircam.core.FrameProtocol;
import com.example.vircam.core.LatestFrame;
import java.io.IOException;

/** One authenticated local client and one decoded image. No network listener on the phone. */
final class DesktopReceiver implements AutoCloseable {
    final LatestFrame<Bitmap> frames=new LatestFrame<>(image->{if(!image.isRecycled())image.recycle();});
    final String socketName;
    private final String session;
    private final int deviceId;
    private final int inputWidth,inputHeight,inputFormat;
    private final LocalServerSocket server;
    private final Thread worker;
    private volatile LocalSocket client;
    private volatile boolean closed;

    DesktopReceiver(String session,int deviceId) throws IOException {
        this(session,deviceId,640,480,FrameProtocol.FORMAT_PNG);
    }
    DesktopReceiver(String session,int deviceId,int width,int height,int format) throws IOException {
        inputWidth=width;inputHeight=height;inputFormat=format;
        this.session=session;this.deviceId=deviceId;socketName="vircre-"+session;
        server=new LocalServerSocket(socketName);
        worker=new Thread(this::acceptLoop,"vircre-desktop-input");worker.setDaemon(true);worker.start();
    }
    private void acceptLoop() {
        while(!closed) {
            String phase="accept";
            try(LocalSocket socket=server.accept()) {
                client=socket;socket.setSoTimeout(2000);
                phase="hello";
                FrameProtocol.Packet hello=FrameProtocol.read(socket.getInputStream(),session,deviceId);
                if(hello.type!=FrameProtocol.HELLO) throw new IOException("HELLO required");
                frames.beginConnection();ack(socket,0);
                AgentLog.event("input-connected");
                long lastSequence=0;boolean active=false;
                while(!closed) {
                    phase="packet";
                    FrameProtocol.Packet packet=FrameProtocol.read(socket.getInputStream(),session,deviceId);
                    if(packet.sequence<=lastSequence) throw new IOException("Non-increasing sequence");
                    lastSequence=packet.sequence;
                    if(packet.type==FrameProtocol.FRAME) {
                        BitmapFactory.Options bounds=new BitmapFactory.Options();bounds.inJustDecodeBounds=true;
                        BitmapFactory.decodeByteArray(packet.payload,0,packet.payload.length,bounds);
                        String mime=inputFormat==FrameProtocol.FORMAT_JPEG ? "image/jpeg" : "image/png";
                        if(packet.width!=inputWidth || packet.height!=inputHeight || packet.format!=inputFormat
                                || bounds.outWidth!=inputWidth || bounds.outHeight!=inputHeight || !mime.equals(bounds.outMimeType))
                            throw new IOException("Invalid image dimensions/format");
                        Bitmap image=decodeImage(packet.payload);
                        if(image==null || !frames.offer(packet.sequence,SystemClock.elapsedRealtime(),image))
                            throw new IOException("Image decode/replay rejected");
                        active=true;
                        AgentLog.event("input-frame","sequence",packet.sequence,"capturedUtcMs",packet.capturedUtcMs,
                                "bytes",packet.payload.length);
                    } else if(packet.type==FrameProtocol.REPEAT) {
                        if(!frames.repeat(packet.sequence,SystemClock.elapsedRealtime()))throw new IOException("Repeat requires a current frame");
                    } else if(packet.type==FrameProtocol.HOLD) {
                        if(!frames.hold(packet.sequence))throw new IOException("Hold sequence rejected");
                    } else if(packet.type==FrameProtocol.PAUSE || packet.type==FrameProtocol.STOP) {
                        frames.pause();
                        if(active) AgentLog.event("input-paused","sequence",packet.sequence);
                        active=false;
                    } else throw new IOException("Unexpected input message");
                    phase="ack";
                    ack(socket,packet.sequence);
                    if(packet.type==FrameProtocol.STOP) break;
                }
            } catch(Exception e) {
                if(!closed) AgentLog.event("input-disconnected","reason",e.getClass().getSimpleName(),"phase",phase);
            } finally { client=null; } // Automatic transport failure retains this session's image.
        }
    }
    private Bitmap decodeImage(byte[] payload) {
        // Only the spare is writable. GL holds the current frame's mailbox lock
        // until tex(Sub)Image2D has finished reading pixels; presentation never holds it.
        Bitmap spare=frames.takeReusable(),image=null;
        BitmapFactory.Options decode=new BitmapFactory.Options();
        decode.inPreferredConfig=Bitmap.Config.ARGB_8888;decode.inMutable=true;decode.inBitmap=spare;
        try {
            try {image=BitmapFactory.decodeByteArray(payload,0,payload.length,decode);}
            catch(IllegalArgumentException incompatible) {
                if(spare==null)throw incompatible;
                // Some ROM decoders reject reusable storage. Retry once without it.
                spare.recycle();spare=null;decode.inBitmap=null;
                image=BitmapFactory.decodeByteArray(payload,0,payload.length,decode);
            }
            return image;
        } finally {if(spare!=null && image!=spare && !spare.isRecycled())spare.recycle();}
    }
    private void ack(LocalSocket socket,long sequence) throws IOException {
        FrameProtocol.write(socket.getOutputStream(),FrameProtocol.control(FrameProtocol.ACK,session,deviceId,sequence));
    }
    @Override public void close() {
        closed=true;frames.pause();
        try { if(client!=null) client.close(); } catch(IOException ignored) { }
        try { server.close(); } catch(IOException ignored) { }
        try { worker.join(1000); } catch(InterruptedException e) { Thread.currentThread().interrupt(); }
        frames.close();
    }
}
