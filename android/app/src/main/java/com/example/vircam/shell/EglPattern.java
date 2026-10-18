package com.example.vircam.shell;

import android.opengl.EGL14;
import android.opengl.EGLConfig;
import android.opengl.EGLContext;
import android.opengl.EGLDisplay;
import android.opengl.EGLSurface;
import android.opengl.EGLExt;
import android.opengl.GLES20;
import android.view.Surface;
import com.example.vircam.core.FrameMarker;

/** One instance per configured stream. All calls belong to the callback executor. */
final class EglPattern implements AutoCloseable {
    private final int width;
    private final int height;
    private EGLDisplay display = EGL14.EGL_NO_DISPLAY;
    private EGLContext context = EGL14.EGL_NO_CONTEXT;
    private EGLSurface window = EGL14.EGL_NO_SURFACE;
    private final Surface surface;

    EglPattern(Surface surface, int width, int height) {
        this.surface = surface;
        this.width = width;
        this.height = height;
        try {
            display = EGL14.eglGetDisplay(EGL14.EGL_DEFAULT_DISPLAY);
            int[] version = new int[2];
            check(EGL14.eglInitialize(display, version, 0, version, 1), "initialize");
            EGLConfig[] configs = new EGLConfig[1];
            int[] count = new int[1];
            int[] attributes = {EGL14.EGL_RENDERABLE_TYPE, EGL14.EGL_OPENGL_ES2_BIT,
                    EGL14.EGL_SURFACE_TYPE, EGL14.EGL_WINDOW_BIT,
                    EGL14.EGL_RED_SIZE, 8, EGL14.EGL_GREEN_SIZE, 8,
                    EGL14.EGL_BLUE_SIZE, 8, EGL14.EGL_ALPHA_SIZE, 8, EGL14.EGL_NONE};
            check(EGL14.eglChooseConfig(display, attributes, 0, configs, 0, 1, count, 0)
                    && count[0] > 0, "chooseConfig");
            context = EGL14.eglCreateContext(display, configs[0], EGL14.EGL_NO_CONTEXT,
                    new int[]{EGL14.EGL_CONTEXT_CLIENT_VERSION, 2, EGL14.EGL_NONE}, 0);
            check(context != EGL14.EGL_NO_CONTEXT, "createContext");
            window = EGL14.eglCreateWindowSurface(display, configs[0], surface,
                    new int[]{EGL14.EGL_NONE}, 0);
            check(window != EGL14.EGL_NO_SURFACE, "createWindowSurface");
            current();
        } catch (RuntimeException e) { close(); throw e; }
    }

    void render(int counter) {
        current();
        GLES20.glViewport(0, 0, width, height);
        GLES20.glDisable(GLES20.GL_SCISSOR_TEST);
        GLES20.glClearColor(0.08f, 0.12f, 0.1f, 1);
        GLES20.glClear(GLES20.GL_COLOR_BUFFER_BIT);
        GLES20.glEnable(GLES20.GL_SCISSOR_TEST);
        float[][] colors = {{1,1,1},{1,1,0},{0,1,1},{0,1,0},{1,0,1},{1,0,0},{0,0,1},{0,0,0}};
        for (int i = 0; i < 8; i++) rect(i * 80, 20, 80, 110, colors[i]);
        int word = FrameMarker.word(counter);
        for (int row = 0; row < 2; row++) {
            int bits = row == 0 ? word : ~word;
            for (int bit = 0; bit < 32; bit++) {
                float value = ((bits >>> (31 - bit)) & 1);
                rect(40 + bit * 18, row == 0 ? 170 : 230, 18, 40,
                        new float[]{value, value, value});
            }
        }
        // Red upper-left and green lower-right distinguish rotation and mirror errors.
        rect(10, 140, 20, 20, new float[]{1,0,0});
        rect(610, 430, 20, 20, new float[]{0,1,0});
        GLES20.glDisable(GLES20.GL_SCISSOR_TEST);
        check(GLES20.glGetError() == GLES20.GL_NO_ERROR, "render");
        check(EGLExt.eglPresentationTimeANDROID(display, window, System.nanoTime()), "presentationTime");
        check(EGL14.eglSwapBuffers(display, window), "swapBuffers");
    }

    private void rect(int x, int y, int w, int h, float[] color) {
        int left = x * width / 640, right = (x + w) * width / 640;
        int top = y * height / 480, bottom = (y + h) * height / 480;
        GLES20.glScissor(left, height - bottom, right - left, bottom - top);
        GLES20.glClearColor(color[0], color[1], color[2], 1);
        GLES20.glClear(GLES20.GL_COLOR_BUFFER_BIT);
    }

    private void current() { check(EGL14.eglMakeCurrent(display, window, window, context), "makeCurrent"); }
    private static void check(boolean valid, String operation) {
        if (!valid) throw new IllegalStateException("EGL " + operation + " failed: 0x" + Integer.toHexString(EGL14.eglGetError()));
    }

    @Override public void close() {
        if (display != EGL14.EGL_NO_DISPLAY) {
            EGL14.eglMakeCurrent(display, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_CONTEXT);
            if (window != EGL14.EGL_NO_SURFACE) EGL14.eglDestroySurface(display, window);
            if (context != EGL14.EGL_NO_CONTEXT) EGL14.eglDestroyContext(display, context);
            EGL14.eglTerminate(display);
            EGL14.eglReleaseThread();
        }
        display = EGL14.EGL_NO_DISPLAY;
        context = EGL14.EGL_NO_CONTEXT;
        window = EGL14.EGL_NO_SURFACE;
        surface.release();
    }
}
