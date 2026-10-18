package com.example.vircam.shell;

import android.content.Context;
import android.content.ContextWrapper;
import android.content.AttributionSource;
import android.graphics.PixelFormat;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.media.Image;
import android.media.ImageReader;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.IBinder;
import android.os.SystemClock;
import android.view.Surface;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

/** Android 16 hidden API boundary. No framework stubs or global policy changes. */
final class VirtualDeviceAdapter implements AutoCloseable {
    private Object device;
    private Object camera;
    private VirtualDisplay display;
    private ImageReader displayReader;
    private HandlerThread drainThread;
    private final ExecutorService callbacks = Executors.newSingleThreadExecutor();
    private final Map<Integer, EglPattern> streams = new HashMap<>();
    private final Map<Integer, Long> lastFrames = new HashMap<>();
    private final AtomicBoolean closed = new AtomicBoolean();
    private int counter;
    private volatile Throwable renderFailure;
    private DesktopReceiver desktop;

    void create(int associationId, int seed, String session,boolean desktopMode) throws Exception {
        counter = seed;
        AgentLog.event("stage", "name", "shell-context");
        Class<?> activityThread = Class.forName("android.app.ActivityThread");
        Object thread = activityThread.getMethod("systemMain").invoke(null);
        Context system = (Context) activityThread.getMethod("getSystemContext").invoke(thread);
        Context shell = system.createPackageContext("com.android.shell", 0);
        Context context = new ContextWrapper(shell) {
            @Override public Context getApplicationContext() { return this; }
            @Override public String getOpPackageName() { return "com.android.shell"; }
            @Override public AttributionSource getAttributionSource() {
                // ActivityThread's system context retains package "android" after createPackageContext.
                // Attribute calls to the actual shell UID and its real package, checked by VDM.
                return new AttributionSource.Builder(android.os.Process.myUid())
                        .setPackageName("com.android.shell").build();
            }
        };
        AgentLog.event("caller-context", "attributionPackage", context.getAttributionSource().getPackageName(),
                "attributionUid", context.getAttributionSource().getUid(), "package", context.getPackageName(),
                "uid", android.os.Process.myUid());
        android.companion.CompanionDeviceManager cdm = context.getSystemService(android.companion.CompanionDeviceManager.class);
        for (android.companion.AssociationInfo association : cdm.getMyAssociations()) {
            AgentLog.event("caller-association", "id", association.getId(), "profile", association.getDeviceProfile());
        }
        IBinder binder = (IBinder) Class.forName("android.os.ServiceManager")
                .getMethod("getService", String.class).invoke(null, "virtualdevice");
        if (binder == null) throw new IllegalStateException("virtualdevice service missing");
        Class<?> serviceType = Class.forName("android.companion.virtual.IVirtualDeviceManager");
        Object service = Class.forName("android.companion.virtual.IVirtualDeviceManager$Stub")
                .getMethod("asInterface", IBinder.class).invoke(null, binder);
        Class<?> managerType = Class.forName("android.companion.virtual.VirtualDeviceManager");
        Object manager = managerType.getConstructor(serviceType, Context.class).newInstance(service, context);
        Class<?> paramsType = Class.forName("android.companion.virtual.VirtualDeviceParams");
        Class<?> builderType = Class.forName("android.companion.virtual.VirtualDeviceParams$Builder");
        Object builder = builderType.getConstructor().newInstance();
        invoke(builder, "setName", new Class<?>[]{String.class}, "virCre-" + session);
        int policy = paramsType.getField("POLICY_TYPE_CAMERA").getInt(null);
        int custom = paramsType.getField("DEVICE_POLICY_CUSTOM").getInt(null);
        invoke(builder, "setDevicePolicy", new Class<?>[]{int.class, int.class}, policy, custom);
        Object params = invoke(builder, "build", new Class<?>[0]);
        AgentLog.event("stage", "name", "create-device", "associationId", associationId,
                "uid", android.os.Process.myUid(), "package", context.getPackageName());
        device = invoke(manager, "createVirtualDevice", new Class<?>[]{int.class, paramsType}, associationId, params);
        AgentLog.event("device-created", "deviceId", deviceId());
        if(desktopMode) desktop=new DesktopReceiver(session,deviceId());

        AgentLog.event("stage", "name", "create-display");
        drainThread = new HandlerThread("vircre-display-drain");
        drainThread.start();
        displayReader = ImageReader.newInstance(960, 720, PixelFormat.RGBA_8888, 2);
        displayReader.setOnImageAvailableListener(reader -> {
            try (Image ignored = reader.acquireLatestImage()) { /* Drain display buffers; no desktop recording. */ }
        }, new Handler(drainThread.getLooper()));
        int trusted = DisplayManager.class.getField("VIRTUAL_DISPLAY_FLAG_TRUSTED").getInt(null);
        display = (VirtualDisplay) invoke(device, "createVirtualDisplay",
                new Class<?>[]{int.class, int.class, int.class, Surface.class, int.class,
                        java.util.concurrent.Executor.class, VirtualDisplay.Callback.class},
                960, 720, 200, displayReader.getSurface(), trusted | DisplayManager.VIRTUAL_DISPLAY_FLAG_PRESENTATION,
                callbacks, null);
        if (display == null) throw new IllegalStateException("Virtual display returned null");
        AgentLog.event("display-created", "displayId", displayId());

        AgentLog.event("stage", "name", "create-camera");
        Class<?> callbackType = Class.forName("android.companion.virtual.camera.VirtualCameraCallback");
        Object callback = Proxy.newProxyInstance(callbackType.getClassLoader(), new Class<?>[]{callbackType},
                (proxy, method, args) -> {
                    if (method.getDeclaringClass() == Object.class) {
                        switch (method.getName()) {
                            case "hashCode": return System.identityHashCode(proxy);
                            case "equals": return proxy == args[0];
                            case "toString": return "virCreVirtualCameraCallback";
                            default: return null;
                        }
                    }
                    if (closed.get()) return null;
                    try { onCallback(method.getName(), args); }
                    catch (Throwable failure) {
                        renderFailure = failure;
                        AgentLog.event("render-error", "error", failure.toString());
                    }
                    return null;
                });
        Class<?> cameraBuilder = Class.forName("android.companion.virtual.camera.VirtualCameraConfig$Builder");
        Object configBuilder = cameraBuilder.getConstructor(String.class).newInstance("virCre desktop input");
        invoke(configBuilder, "addStreamConfig", new Class<?>[]{int.class, int.class, int.class, int.class},
                640, 480, PixelFormat.RGBA_8888, 5);
        invoke(configBuilder, "setSensorOrientation", new Class<?>[]{int.class}, 0);
        invoke(configBuilder, "setLensFacing", new Class<?>[]{int.class}, CameraCharacteristics.LENS_FACING_BACK);
        invoke(configBuilder, "setVirtualCameraCallback", new Class<?>[]{java.util.concurrent.Executor.class, callbackType}, callbacks, callback);
        Object config = invoke(configBuilder, "build", new Class<?>[0]);
        camera = invoke(device, "createVirtualCamera", new Class<?>[]{config.getClass()}, config);
        AgentLog.event("ready", "session", session, "deviceId", deviceId(), "displayId", displayId(),
                // Android 16 CameraService maps the virtual back camera to "0" for device contexts.
                "cameraId", "0", "registeredCameraId", cameraId(), "seed", seed,
                "inputMode",desktopMode ? "desktop" : "pattern", "inputSocket",desktopMode ? desktop.socketName : "");
    }

    private void onCallback(String name, Object[] args) {
        int streamId = (Integer) args[0];
        if (name.equals("onStreamConfigured")) {
            EglPattern old = streams.remove(streamId);
            if (old != null) old.close();
            streams.put(streamId, new EglPattern((Surface) args[1], (Integer) args[2], (Integer) args[3]));
            AgentLog.event("stream-configured", "streamId", streamId, "width", args[2], "height", args[3], "format", args[4]);
        } else if (name.equals("onProcessCaptureRequest")) {
            EglPattern pattern = streams.get(streamId);
            if (pattern == null) throw new IllegalStateException("Request for unconfigured stream " + streamId);
            long now = SystemClock.elapsedRealtime();
            Long last = lastFrames.get(streamId);
            if (last != null && now - last < 190) return;
            int marker = counter++ & 0xffff;
            if(desktop==null) pattern.render(marker);
            else {
                com.example.vircam.core.LatestFrame.Snapshot<android.graphics.Bitmap> image=desktop.frames.snapshot(now,1500);
                pattern.renderDesktop(image==null ? null : image.value);
            }
            lastFrames.put(streamId, now);
            if(desktop==null) AgentLog.event("frame-submitted", "streamId", streamId, "requestId", args[1], "marker", marker);
        } else if (name.equals("onStreamClosed")) {
            EglPattern pattern = streams.remove(streamId);
            if (pattern != null) pattern.close();
            lastFrames.remove(streamId);
            AgentLog.event("stream-closed", "streamId", streamId);
        }
    }

    int deviceId() throws Exception { return (Integer) invoke(device, "getDeviceId", new Class<?>[0]); }
    int displayId() { return display.getDisplay().getDisplayId(); }
    String cameraId() throws Exception { return (String) invoke(camera, "getId", new Class<?>[0]); }
    Throwable renderFailure() { return renderFailure; }

    private static Object invoke(Object target, String name, Class<?>[] types, Object... args) throws Exception {
        try {
            Method method = target.getClass().getMethod(name, types);
            return method.invoke(target, args);
        } catch (InvocationTargetException e) {
            Throwable cause = e.getCause();
            if (cause instanceof Exception) throw (Exception) cause;
            if (cause instanceof Error) throw (Error) cause;
            throw e;
        }
    }

    @Override public synchronized void close() {
        if (!closed.compareAndSet(false, true)) return;
        if(desktop!=null) desktop.close();
        closeHidden(camera, "camera");
        if (display != null) display.release();
        closeHidden(device, "device");
        try {
            callbacks.submit(() -> {
                for (EglPattern pattern : streams.values()) pattern.close();
                streams.clear();
            }).get(3, TimeUnit.SECONDS);
        } catch (Exception e) { AgentLog.event("cleanup-error", "resource", "egl", "error", e.toString()); }
        callbacks.shutdownNow();
        if (displayReader != null) displayReader.close();
        if (drainThread != null) drainThread.quitSafely();
        AgentLog.event("closed");
    }

    private static void closeHidden(Object resource, String name) {
        if (resource == null) return;
        try { invoke(resource, "close", new Class<?>[0]); }
        catch (Exception e) { AgentLog.event("cleanup-error", "resource", name, "error", e.toString()); }
    }
}
