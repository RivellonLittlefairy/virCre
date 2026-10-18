package com.example.vircam.shell;

import android.os.Looper;
import android.os.SystemClock;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

/** Run only through ADB app_process, never from the ordinary application's UID. */
public final class ShellMain {
    private ShellMain() {}
    public static void main(String[] args) {
        if (android.os.Process.myUid() != 2000 || (args.length != 4 && args.length != 5)) {
            System.err.println("Usage (ADB shell only): ShellMain associationId seconds session seed [desktop]");
            System.exit(2);
            return;
        }
        int associationId = Integer.parseInt(args[0]);
        int seconds = Integer.parseInt(args[1]);
        int seed = Integer.parseInt(args[3]);
        if (associationId <= 0 || seconds < 5 || seconds > 600 || seed < 0 || seed > 65535
                || !args[2].matches("[a-f0-9]{32}")) throw new IllegalArgumentException("Invalid session parameters");
        if (Looper.getMainLooper() == null) Looper.prepareMainLooper();
        VirtualDeviceAdapter adapter = new VirtualDeviceAdapter();
        Runtime.getRuntime().addShutdownHook(new Thread(adapter::close, "vircre-cleanup"));
        ScheduledExecutorService watchdog = Executors.newSingleThreadScheduledExecutor();
        watchdog.schedule(() -> {
            AgentLog.event("watchdog-timeout", "seconds", seconds + 15);
            // Exit kills the creator Binder even when a system call is stuck.
            android.os.Process.killProcess(android.os.Process.myPid());
        }, seconds + 15L, TimeUnit.SECONDS);
        int exitCode = 0;
        try {
            AgentLog.event("started", "pid", android.os.Process.myPid(), "session", args[2]);
            if(args.length==5 && !args[4].equals("desktop")) throw new IllegalArgumentException("Unknown input mode");
            adapter.create(associationId, seed, args[2],args.length==5);
            long end = SystemClock.elapsedRealtime() + seconds * 1000L;
            while (SystemClock.elapsedRealtime() < end) {
                if (adapter.renderFailure() != null) throw new IllegalStateException("Rendering failed", adapter.renderFailure());
                Thread.sleep(100);
            }
        } catch (Throwable e) {
            exitCode = 1;
            AgentLog.event("failed", "error", e.toString());
            e.printStackTrace(System.err);
        } finally {
            adapter.close();
            watchdog.shutdownNow();
        }
        System.exit(exitCode);
    }
}
