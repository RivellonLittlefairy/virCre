// Modified for virCre (2026); see docs/scrcpy-changes.html in the distribution.
package com.genymobile.scrcpy.control;

import java.io.File;
import java.io.IOException;
import java.io.InterruptedIOException;
import java.io.RandomAccessFile;
import java.nio.channels.FileLock;
import java.nio.channels.OverlappingFileLockException;
import java.util.concurrent.TimeUnit;

/** Serializes the entire clipboard-and-dispatch transaction across shell server processes. */
public final class ImeCommitter {
    public interface Action { void run() throws IOException; }
    private ImeCommitter() { }
    public static void run(File lockFile, long timeoutMillis, Action action) throws IOException {
        long deadline = System.nanoTime() + TimeUnit.MILLISECONDS.toNanos(timeoutMillis);
        try (RandomAccessFile file = new RandomAccessFile(lockFile, "rw")) {
            while (true) {
                if (Thread.currentThread().isInterrupted()) {throw new InterruptedIOException("IME transaction cancelled");}
                FileLock acquired = null;
                try {acquired = file.getChannel().tryLock();} catch (OverlappingFileLockException busy) { /* Same JVM, another controller. */ }
                if (acquired != null) {
                    try {action.run();} finally {acquired.release();}
                    return;
                }
                if (System.nanoTime() >= deadline) {throw new IOException("IME transaction lock timed out");}
                try {Thread.sleep(5);} catch (InterruptedException interrupted) {
                    Thread.currentThread().interrupt();throw new InterruptedIOException("IME transaction cancelled");
                }
            }
        }
    }
}
