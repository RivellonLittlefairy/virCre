package com.example.vircam.core;

import java.util.function.Consumer;

/** Single-slot mailbox: pause drops pixels without resetting the replay guard.
 * One spare is reusable; use() holds ownership until GL has finished reading pixels. */
public final class LatestFrame<T> implements AutoCloseable {
    public static final class Snapshot<T> {
        public final long sequence, revision, receivedAt;
        public final T value;
        Snapshot(long seq,long revision,long at,T value) {this.sequence=seq;this.revision=revision;this.receivedAt=at;this.value=value;}
    }
    private final Consumer<T> dispose;
    private long lastSequence,revision;
    private Snapshot<T> latest;
    private T spare;
    private boolean closed,connectionFrame;
    public LatestFrame() {this(value->{});}
    public LatestFrame(Consumer<T> dispose) {this.dispose=dispose;}
    public synchronized T takeReusable() {T value=spare;spare=null;return value;}
    public synchronized boolean offer(long sequence,long receivedAt,T value) {
        if(closed || sequence<=lastSequence || receivedAt<0 || value==null) {if(value!=null)dispose.accept(value);return false;}
        if(latest!=null)keepSpare(latest.value);
        lastSequence=sequence;connectionFrame=true;latest=new Snapshot<>(sequence,++revision,receivedAt,value);return true;
    }
    public synchronized boolean repeat(long sequence,long receivedAt) {
        if(closed || !connectionFrame || latest==null || sequence<=lastSequence || receivedAt<latest.receivedAt)return false;
        lastSequence=sequence;latest=new Snapshot<>(latest.sequence,latest.revision,receivedAt,latest.value);return true;
    }
    public synchronized Snapshot<T> snapshot(long now,long maxAge) {
        // The caller may sample now before locking, while a receiver refreshes the
        // mailbox. A locally newer receive time is fresh, never evidence of expiry.
        if(latest==null || Math.max(0,now-latest.receivedAt)>maxAge)return null;
        return latest;
    }
    /** Native-resource callers must read only inside this callback, never retain its value. */
    public synchronized void use(long now,long maxAge,Consumer<Snapshot<T>> reader) {reader.accept(snapshot(now,maxAge));}
    public synchronized void pause() {connectionFrame=false;if(latest!=null) {keepSpare(latest.value);latest=null;}}
    /** Transport reconnect within this receiver/session: retain image, require a new full frame. */
    public synchronized void beginConnection() {lastSequence=0;connectionFrame=false;}
    public synchronized boolean hold(long sequence) {
        if(closed || sequence<=lastSequence)return false;
        lastSequence=sequence;return true;
    }
    public synchronized void resetConnection() {pause();lastSequence=0;}
    private void keepSpare(T value) {if(spare!=null && spare!=value)dispose.accept(spare);spare=value;}
    @Override public synchronized void close() {
        if(closed)return;closed=true;
        if(latest!=null) {dispose.accept(latest.value);latest=null;}
        if(spare!=null) {dispose.accept(spare);spare=null;}
    }
}
