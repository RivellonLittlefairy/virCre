package com.vircre.icons;

import android.content.Context;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.drawable.Drawable;
import android.os.Looper;
import android.util.Base64;
import java.io.ByteArrayOutputStream;

/** Read-only icon exporter. Runs as the explicitly authorized ADB shell, never installs an app. */
public final class IconMain {
    public static void main(String[] packages) throws Exception {
        if (android.os.Process.myUid() != 2000 || packages.length > 60) throw new IllegalArgumentException("ADB shell only, maximum 60 packages");
        Thread watchdog = new Thread(() -> {
            try {Thread.sleep(45000);} catch (InterruptedException ignored) {return;}
            android.os.Process.killProcess(android.os.Process.myPid());
        }, "vircre-icon-deadline");
        watchdog.setDaemon(true);
        watchdog.start();
        if (Looper.getMainLooper() == null) Looper.prepareMainLooper();
        Class<?> activityThread = Class.forName("android.app.ActivityThread");
        Object thread = activityThread.getMethod("systemMain").invoke(null);
        Context system = (Context) activityThread.getMethod("getSystemContext").invoke(thread);
        Context context = system.createPackageContext("com.android.shell", 0);
        PackageManager manager = context.getPackageManager();
        for (String name : packages) {
            if (!name.matches("[a-zA-Z_][a-zA-Z0-9_]*(\\.[a-zA-Z_][a-zA-Z0-9_]*)+")) throw new IllegalArgumentException("Invalid package");
            Bitmap bitmap = null;
            try {
                // Read the application's own resource. OEM loadIcon hooks may read theme providers
                // using the synthetic system context's identity and fail shell UID checks.
                android.content.pm.ApplicationInfo info = manager.getApplicationInfo(name, 0);
                Drawable icon = info.icon == 0 ? manager.getDefaultActivityIcon() : manager.getDrawable(name, info.icon, info);
                if (icon == null) throw new IllegalStateException("Icon resource missing");
                bitmap = Bitmap.createBitmap(96, 96, Bitmap.Config.ARGB_8888);
                icon.setBounds(0, 0, 96, 96);
                icon.draw(new Canvas(bitmap));
                ByteArrayOutputStream bytes = new ByteArrayOutputStream();
                bitmap.compress(Bitmap.CompressFormat.PNG, 100, bytes);
                System.out.println("{\"package\":\"" + name + "\",\"png\":\"" + Base64.encodeToString(bytes.toByteArray(), Base64.NO_WRAP) + "\"}");
            } catch (Exception error) {
                // One broken resource must not prevent the other applications from appearing.
                System.err.println("Icon unavailable: " + name + " (" + error + ")");
            } finally {if (bitmap != null) bitmap.recycle();}
        }
        System.out.println("{\"type\":\"complete\"}");
        System.out.flush();
        System.exit(0);
    }
}
