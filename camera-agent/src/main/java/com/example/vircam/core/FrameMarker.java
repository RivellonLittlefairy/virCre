package com.example.vircam.core;

import java.nio.ByteBuffer;

/** Fixed 640x480 reference geometry, scaled to the actual camera image. */
public final class FrameMarker {
    private FrameMarker() {}

    public static int word(int counter) {
        int value = counter & 0xffff;
        return 0xa5000000 | (value << 8) | (((value >>> 8) ^ value ^ 0x5a) & 0xff);
    }

    public static int decode(int normal, int inverse) {
        if (normal != ~inverse || (normal >>> 24) != 0xa5) return -1;
        int counter = (normal >>> 8) & 0xffff;
        return word(counter) == normal ? counter : -1;
    }

    public static int sample(ByteBuffer y, int width, int height, int rowStride, int pixelStride) {
        if (y == null || width < 64 || height < 48 || pixelStride < 1 || rowStride < 1
                || ((long) width - 1) * pixelStride >= rowStride) return -1;
        long last = (long) y.position() + (long) (height - 1) * rowStride
                + (long) (width - 1) * pixelStride;
        if (last >= y.limit()) return -1;
        int[] words = new int[2];
        for (int row = 0; row < 2; row++) {
            int sampleY = (row == 0 ? 190 : 250) * height / 480;
            for (int bit = 0; bit < 32; bit++) {
                int x = (int) ((long) (49 + bit * 18) * width / 640);
                int offset = (int) ((long) y.position() + (long) sampleY * rowStride + (long) x * pixelStride);
                int value = y.get(offset) & 0xff;
                if (value > 80 && value < 175) return -1;
                words[row] = (words[row] << 1) | (value >= 175 ? 1 : 0);
            }
        }
        return decode(words[0], words[1]);
    }
}
