package com.example.vircam.core;

/** Counts only forward-moving 16-bit frame markers, including a normal wrap. */
public final class FrameEvidence {
    private int last = -1;
    private int count;

    public boolean accept(int value) {
        if (value < 0 || value > 0xffff) return false;
        if (last >= 0) {
            int delta = (value - last) & 0xffff;
            if (delta == 0 || delta >= 0x8000) return false;
        }
        last = value;
        count++;
        return true;
    }

    public int accepted() { return count; }
}
