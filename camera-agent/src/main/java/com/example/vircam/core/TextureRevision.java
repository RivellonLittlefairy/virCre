package com.example.vircam.core;

/** Per-stream upload state. Content revision changes even when the same Bitmap storage is reused. */
public final class TextureRevision {
    private long revision=-1;
    private int width,height;
    /** 0: keep uploaded pixels; 1: update existing storage; 2: allocate a new size. */
    public int update(long nextRevision,int nextWidth,int nextHeight) {
        int action=width!=nextWidth || height!=nextHeight ? 2 : revision!=nextRevision ? 1 : 0;
        revision=nextRevision;width=nextWidth;height=nextHeight;return action;
    }
}
