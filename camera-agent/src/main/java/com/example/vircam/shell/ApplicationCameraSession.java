package com.example.vircam.shell;

import android.hardware.display.VirtualDisplay;
import android.os.Looper;
import android.view.Surface;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.Consumer;

/** Embedded in the bundled scrcpy server. The VDM display feeds its encoder directly. */
public final class ApplicationCameraSession implements AutoCloseable {
    private final VirtualDeviceAdapter adapter = new VirtualDeviceAdapter();
    private final ScheduledExecutorService timers = Executors.newScheduledThreadPool(2);
    private final AtomicBoolean closed = new AtomicBoolean();
    private int association;
    private String sessionId;
    private boolean initialized;

    private ApplicationCameraSession() {}

    public static ApplicationCameraSession open(String config, Consumer<String> output) throws Exception {
        if (android.os.Process.myUid() != 2000 || !config.matches("[1-9][0-9]*:[a-f0-9]{32}")) {
            throw new IllegalArgumentException("Invalid virtual camera session");
        }
        String[] parts = config.split(":", 2);
        AgentLog.setOutput(output);
        int association = Integer.parseInt(parts[0]);
        ApplicationCameraSession session = new ApplicationCameraSession();
        session.association = association;
        session.sessionId = parts[1];
        Thread watchdog = new Thread(() -> {
            long end = android.os.SystemClock.elapsedRealtime() + 615000L;
            while (true) {
                long remaining = end - android.os.SystemClock.elapsedRealtime();
                if (remaining <= 0) break;
                try { Thread.sleep(remaining); }
                catch (InterruptedException ignored) { /* The process deadline must survive session cleanup. */ }
            }
            android.os.Process.killProcess(android.os.Process.myPid());
        }, "vircre-camera-watchdog");
        // Session close must not cancel this: codec stop/join happens afterwards
        // and may block too. Normal System.exit ends the daemon automatically.
        watchdog.setDaemon(true);
        watchdog.start();
        Runtime.getRuntime().addShutdownHook(new Thread(session::close, "vircre-camera-cleanup"));
        try {
            AgentLog.event("started", "pid", android.os.Process.myPid(), "session", parts[1]);
            // Wait for scrcpy to resolve codec alignment and actual display size.
            // Camera registration and ready metadata must use that same size.
            session.timers.schedule(session::endServer, 600, TimeUnit.SECONDS);
            session.timers.scheduleWithFixedDelay(() -> {
                if (!session.closed.get() && session.adapter.renderFailure() != null) session.endServer();
            }, 500, 500, TimeUnit.MILLISECONDS);
            return session;
        } catch (Throwable failure) {
            session.close();
            throw failure;
        }
    }

    public VirtualDisplay getDisplay() { return adapter.applicationDisplay(); }

    public synchronized void attachEncoder(Surface surface, int width, int height, int dpi) throws Exception {
        if (closed.get()) throw new IllegalStateException("Virtual camera session already closed");
        if (!initialized) {
            adapter.create(association, 0, sessionId, true, true, width, height, dpi);
            initialized = true;
        }
        adapter.attachApplicationEncoder(surface, width, height, dpi);
        AgentLog.event("encoder-attached", "displayId", adapter.displayId());
    }

    private void endServer() {
        try { close(); }
        finally { Looper.getMainLooper().quitSafely(); }
    }

    @Override public synchronized void close() {
        if (!closed.compareAndSet(false, true)) return;
        try { adapter.close(); }
        finally { timers.shutdownNow(); }
    }
}
