package com.example.vircam.core;

import org.junit.Test;
import java.io.*;
import java.nio.ByteBuffer;
import static org.junit.Assert.*;

public class FrameProtocolTest {
    private static final String SESSION = "00112233445566778899aabbccddeeff";
    private byte[] hello() {
        String hex = "56435231000100010000000000112233445566778899aabbccddeeff000000090000000000000000000000000000000000000000000000000000000000000000";
        byte[] data = new byte[hex.length()/2];
        for(int i=0;i<data.length;i++) data[i]=(byte)Integer.parseInt(hex.substring(i*2,i*2+2),16);
        return data;
    }
    @Test public void readsIndependentHeaderEvenWhenInputIsFragmented() throws Exception {
        byte[] fixture = hello();
        InputStream fragmented = new ByteArrayInputStream(fixture) {
            @Override public synchronized int read(byte[] b,int off,int len) { return super.read(b,off,Math.min(1,len)); }
        };
        FrameProtocol.Packet packet = FrameProtocol.read(fragmented,SESSION,9);
        assertEquals(1,packet.type);
        assertEquals(0,packet.sequence);
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        FrameProtocol.write(output,packet);
        assertArrayEquals(fixture,output.toByteArray());
    }
    @Test public void rejectsLengthIdentityAndTruncationBeforePayloadAllocation() {
        assertThrows(IOException.class,()->FrameProtocol.read(new ByteArrayInputStream(hello()),SESSION,10));
        assertThrows(IOException.class,()->FrameProtocol.read(new ByteArrayInputStream(hello()),"00000000000000000000000000000000",9));
        assertThrows(IOException.class,()->FrameProtocol.read(new ByteArrayInputStream(new byte[63]),SESSION,9));
        for(int length:new int[]{-1,1048577}) {
            byte[] data=hello(); ByteBuffer.wrap(data).putShort(6,(short)2).putInt(8,length);
            assertThrows(IOException.class,()->FrameProtocol.read(new ByteArrayInputStream(data),SESSION,9));
        }
    }
    @Test public void latestFrameExpiresAndPauseDoesNotAllowReplay() {
        LatestFrame<String> frames = new LatestFrame<>();
        assertTrue(frames.offer(1,1000,"a"));
        assertTrue(frames.offer(2,1100,"b"));
        assertEquals("b",frames.snapshot(1200,1500).value);
        assertNull(frames.snapshot(2601,1500));
        frames.pause();
        assertNull(frames.snapshot(1200,1500));
        assertFalse(frames.offer(1,1200,"old"));
        assertTrue(frames.offer(3,1300,"new"));
        assertEquals("new",frames.snapshot(1400,1500).value);
    }
}
