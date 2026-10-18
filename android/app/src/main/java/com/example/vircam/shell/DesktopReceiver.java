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
    final LatestFrame<Bitmap> frames=new LatestFrame<>();
    final String socketName;
    private final String session;
    private final int deviceId;
    private final LocalServerSocket server;
    private final Thread worker;
    private volatile LocalSocket client;
    private volatile boolean closed;

    DesktopReceiver(String session,int deviceId) throws IOException {
        this.session=session;this.deviceId=deviceId;socketName="vircre-"+session;
        server=new LocalServerSocket(socketName);
        worker=new Thread(this::acceptLoop,"vircre-desktop-input");worker.setDaemon(true);worker.start();
    }
    private void acceptLoop() {
        while(!closed) {
            try(LocalSocket socket=server.accept()) {
                client=socket;socket.setSoTimeout(2000);
                FrameProtocol.Packet hello=FrameProtocol.read(socket.getInputStream(),session,deviceId);
                if(hello.type!=FrameProtocol.HELLO) throw new IOException("HELLO required");
                frames.resetConnection();ack(socket,0);
                AgentLog.event("input-connected");
                long lastSequence=0;boolean active=false;
                while(!closed) {
                    FrameProtocol.Packet packet=FrameProtocol.read(socket.getInputStream(),session,deviceId);
                    if(packet.sequence<=lastSequence) throw new IOException("Non-increasing sequence");
                    lastSequence=packet.sequence;
                    if(packet.type==FrameProtocol.FRAME) {
                        BitmapFactory.Options bounds=new BitmapFactory.Options();bounds.inJustDecodeBounds=true;
                        BitmapFactory.decodeByteArray(packet.payload,0,packet.payload.length,bounds);
                        if(bounds.outWidth!=640 || bounds.outHeight!=480 || !"image/png".equals(bounds.outMimeType))
                            throw new IOException("Invalid PNG dimensions/format");
                        Bitmap image=BitmapFactory.decodeByteArray(packet.payload,0,packet.payload.length);
                        if(image==null || !frames.offer(packet.sequence,SystemClock.elapsedRealtime(),image))
                            throw new IOException("Image decode/replay rejected");
                        active=true;
                        AgentLog.event("input-frame","sequence",packet.sequence,"capturedUtcMs",packet.capturedUtcMs,
                                "bytes",packet.payload.length);
                    } else if(packet.type==FrameProtocol.PAUSE || packet.type==FrameProtocol.STOP) {
                        frames.pause();
                        if(active) AgentLog.event("input-paused","sequence",packet.sequence);
                        active=false;
                    } else throw new IOException("Unexpected input message");
                    ack(socket,packet.sequence);
                    if(packet.type==FrameProtocol.STOP) break;
                }
            } catch(Exception e) {
                if(!closed) AgentLog.event("input-disconnected","reason",e.getClass().getSimpleName());
            } finally { frames.pause();client=null; }
        }
    }
    private void ack(LocalSocket socket,long sequence) throws IOException {
        FrameProtocol.write(socket.getOutputStream(),FrameProtocol.control(FrameProtocol.ACK,session,deviceId,sequence));
    }
    @Override public void close() {
        closed=true;frames.pause();
        try { if(client!=null) client.close(); } catch(IOException ignored) { }
        try { server.close(); } catch(IOException ignored) { }
        try { worker.join(1000); } catch(InterruptedException e) { Thread.currentThread().interrupt(); }
    }
}
