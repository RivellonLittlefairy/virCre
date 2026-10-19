package com.example.vircam.core;

/** Single-slot mailbox: pause drops pixels without resetting the replay guard. */
public final class LatestFrame<T> {
    public static final class Snapshot<T> {
        public final long sequence, receivedAt;
        public final T value;
        Snapshot(long seq,long at,T value) { this.sequence=seq;this.receivedAt=at;this.value=value; }
    }
    private long lastSequence;
    private Snapshot<T> latest;
    public synchronized boolean offer(long sequence,long receivedAt,T value) {
        if(sequence<=lastSequence || receivedAt<0 || value==null) return false;
        lastSequence=sequence; latest=new Snapshot<>(sequence,receivedAt,value); return true;
    }
    public synchronized Snapshot<T> snapshot(long now,long maxAge) {
        if(latest==null || now<latest.receivedAt || now-latest.receivedAt>maxAge) return null;
        return latest;
    }
    public synchronized void pause() { latest=null; }
    public synchronized void resetConnection() { latest=null; lastSequence=0; }
}
