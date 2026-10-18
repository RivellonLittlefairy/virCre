package com.example.vircam.core;

import org.junit.Test;
import java.nio.ByteBuffer;
import static org.junit.Assert.*;

public class FrameMarkerTest {
    @Test public void validatesChecksumAndInverseAgainstIndependentFixture() {
        // 0x7C = 0x12 xor 0x34 xor 0x5A.
        assertEquals(0x1234, FrameMarker.decode(0xA512347C, 0x5AEDCB83));
        assertEquals(-1, FrameMarker.decode(0xA512347D, 0x5AEDCB82));
        assertEquals(-1, FrameMarker.decode(0xA512347C, 0x5AEDCB82));
        assertEquals(-1, FrameMarker.decode(0xB512347C, 0x4AEDCB83));
        assertEquals(0xA512347C, FrameMarker.word(0x1234));
    }

    @Test public void readsPaddedYPlaneWithoutMovingPosition() {
        int width = 640, height = 480, rowStride = 1344, pixelStride = 2;
        ByteBuffer y = ByteBuffer.allocate(rowStride * height + 11);
        y.position(11);
        int[] words = {0xA512347C, 0x5AEDCB83};
        for (int row = 0; row < 2; row++) {
            for (int bit = 0; bit < 32; bit++) {
                int x = 49 + bit * 18;
                int sampleY = row == 0 ? 190 : 250;
                y.put(11 + sampleY * rowStride + x * pixelStride,
                        (byte) (((words[row] >>> (31 - bit)) & 1) == 1 ? 235 : 16));
            }
        }
        assertEquals(0x1234, FrameMarker.sample(y, width, height, rowStride, pixelStride));
        assertEquals(11, y.position());
        assertEquals(-1, FrameMarker.sample(y, 0, height, rowStride, pixelStride));
        assertEquals(-1, FrameMarker.sample(ByteBuffer.allocate(16), width, height, rowStride, pixelStride));
        assertEquals(-1, FrameMarker.sample(y, width, height, 1, pixelStride));
        assertEquals(-1, FrameMarker.sample(y, Integer.MAX_VALUE, height, rowStride, pixelStride));
    }

    @Test public void rejectsFlatGrayInsteadOfInventingAFrame() {
        ByteBuffer y = ByteBuffer.allocate(640 * 480);
        for (int i = 0; i < y.capacity(); i++) y.put(i, (byte) 128);
        assertEquals(-1, FrameMarker.sample(y, 640, 480, 640, 1));
    }

    @Test public void rejectsStaleFramesButAcceptsCounterWrap() {
        FrameEvidence evidence = new FrameEvidence();
        assertFalse(evidence.accept(-1));
        assertTrue(evidence.accept(65534));
        assertFalse(evidence.accept(65534));
        assertFalse(evidence.accept(65533));
        assertTrue(evidence.accept(65535));
        assertTrue(evidence.accept(0));
        assertEquals(3, evidence.accepted());
    }

    @Test public void refusesPhysicalDeviceAndMissingCamera() {
        assertNotNull(ProbeGuard.problem(0, 0, 1, "12", new String[]{"12"}));
        assertNotNull(ProbeGuard.problem(3, 0, 1, "12", new String[]{"12"}));
        assertNotNull(ProbeGuard.problem(3, 2, 1, "12", new String[]{"12"}));
        assertNotNull(ProbeGuard.problem(3, 1, 1, "12", new String[]{"0"}));
        assertNull(ProbeGuard.problem(3, 1, 1, "12", new String[]{"12"}));
        // Android 16 maps each virtual back camera to app-visible ID "0".
        assertNull(ProbeGuard.problem(19, 6, 6, "0", new String[]{"0"}));
        assertNotNull(ProbeGuard.problem(0, 0, 6, "0", new String[]{"0"}));
    }
}
