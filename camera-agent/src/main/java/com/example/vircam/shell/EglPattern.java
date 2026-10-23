package com.example.vircam.shell;

import android.opengl.EGL14;
import android.opengl.EGLConfig;
import android.opengl.EGLContext;
import android.opengl.EGLDisplay;
import android.opengl.EGLSurface;
import android.opengl.EGLExt;
import android.opengl.GLES20;
import android.view.Surface;
import android.graphics.Bitmap;
import android.opengl.GLUtils;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.FloatBuffer;
import com.example.vircam.core.FrameMarker;
import com.example.vircam.core.TextureRevision;
import com.example.vircam.core.DesktopFrameRenderer;

/** One instance per configured stream. All calls belong to the callback executor. */
final class EglPattern implements AutoCloseable, DesktopFrameRenderer.Target<Bitmap> {
    private final int width;
    private final int height;
    private EGLDisplay display = EGL14.EGL_NO_DISPLAY;
    private EGLContext context = EGL14.EGL_NO_CONTEXT;
    private EGLSurface window = EGL14.EGL_NO_SURFACE;
    private final Surface surface;
    private int texture,program;
    private boolean closed;
    private Bitmap placeholder;
    private final TextureRevision uploaded=new TextureRevision();
    private final FloatBuffer vertices=buffer(new float[]{-1,-1,1,-1,-1,1,1,1});
    private final FloatBuffer coordinates=buffer(new float[]{0,1,1,1,0,0,1,0});

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
        present();
    }

    @Override public void beginDesktop() {
        current();
        if(program==0) {
            int vertex=shader(GLES20.GL_VERTEX_SHADER,"attribute vec2 aPosition;attribute vec2 aUv;varying vec2 uv;void main(){gl_Position=vec4(aPosition,0.,1.);uv=aUv;}");
            int fragment=shader(GLES20.GL_FRAGMENT_SHADER,"precision mediump float;varying vec2 uv;uniform sampler2D picture;void main(){gl_FragColor=texture2D(picture,uv);}");
            program=GLES20.glCreateProgram();GLES20.glAttachShader(program,vertex);GLES20.glAttachShader(program,fragment);GLES20.glLinkProgram(program);
            int[] linked=new int[1];GLES20.glGetProgramiv(program,GLES20.GL_LINK_STATUS,linked,0);
            GLES20.glDeleteShader(vertex);GLES20.glDeleteShader(fragment);
            if(linked[0]==0) throw new IllegalStateException("Texture program link failed");
            int[] textures=new int[1];GLES20.glGenTextures(1,textures,0);texture=textures[0];
            GLES20.glBindTexture(GLES20.GL_TEXTURE_2D,texture);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D,GLES20.GL_TEXTURE_MIN_FILTER,GLES20.GL_LINEAR);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D,GLES20.GL_TEXTURE_MAG_FILTER,GLES20.GL_LINEAR);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D,GLES20.GL_TEXTURE_WRAP_S,GLES20.GL_CLAMP_TO_EDGE);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D,GLES20.GL_TEXTURE_WRAP_T,GLES20.GL_CLAMP_TO_EDGE);
        }
        GLES20.glViewport(0,0,width,height);GLES20.glDisable(GLES20.GL_SCISSOR_TEST);
        GLES20.glUseProgram(program);GLES20.glActiveTexture(GLES20.GL_TEXTURE0);GLES20.glBindTexture(GLES20.GL_TEXTURE_2D,texture);
    }

    @Override public void uploadDesktop(Bitmap image,long revision) {
        if(image==null) {
            if(placeholder==null)placeholder=pauseBitmap();
            image=placeholder;
        }
        int upload=uploaded.update(revision,image.getWidth(),image.getHeight());
        if(upload==2)GLUtils.texImage2D(GLES20.GL_TEXTURE_2D,0,image,0);
        else if(upload==1)GLUtils.texSubImage2D(GLES20.GL_TEXTURE_2D,0,0,0,image);
    }

    @Override public void presentDesktop() {
        int position=GLES20.glGetAttribLocation(program,"aPosition"),uv=GLES20.glGetAttribLocation(program,"aUv");
        GLES20.glEnableVertexAttribArray(position);GLES20.glEnableVertexAttribArray(uv);
        GLES20.glVertexAttribPointer(position,2,GLES20.GL_FLOAT,false,0,vertices);
        GLES20.glVertexAttribPointer(uv,2,GLES20.GL_FLOAT,false,0,coordinates);
        GLES20.glUniform1i(GLES20.glGetUniformLocation(program,"picture"),0);
        GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP,0,4);present();
    }

    private static FloatBuffer buffer(float[] values) {
        FloatBuffer result=ByteBuffer.allocateDirect(values.length*4).order(ByteOrder.nativeOrder()).asFloatBuffer();
        result.put(values).position(0);return result;
    }
    private static Bitmap pauseBitmap() {
        // app_process has no normal application font bootstrap on some ROMs.
        // A tiny bitmap alphabet avoids Canvas/Typeface native initialization entirely.
        int[] pixels=new int[640*480];java.util.Arrays.fill(pixels,0xff172921);
        String alphabet="PCINUTASED";
        int[][] glyphs={{30,17,17,30,16,16,16},{15,16,16,16,16,16,15},
            {31,4,4,4,4,4,31},{17,25,25,21,19,19,17},{17,17,17,17,17,17,14},
            {31,4,4,4,4,4,4},{14,17,17,31,17,17,17},{15,16,16,14,1,1,30},
            {31,16,16,30,16,16,31},{30,17,17,17,17,17,30}};
        String text="PC INPUT PAUSED";int scale=5,left=(640-text.length()*6*scale)/2;
        for(int i=0;i<text.length();i++) {
            int index=alphabet.indexOf(text.charAt(i));if(index<0) continue;
            for(int y=0;y<7;y++) for(int x=0;x<5;x++) if((glyphs[index][y] & (1<<(4-x)))!=0)
                for(int dy=0;dy<scale;dy++) for(int dx=0;dx<scale;dx++)
                    pixels[(220+y*scale+dy)*640+left+i*6*scale+x*scale+dx]=0xffeeeeee;
        }
        return Bitmap.createBitmap(pixels,640,480,Bitmap.Config.ARGB_8888);
    }
    private static int shader(int type,String text) {
        int shader=GLES20.glCreateShader(type);GLES20.glShaderSource(shader,text);GLES20.glCompileShader(shader);
        int[] status=new int[1];GLES20.glGetShaderiv(shader,GLES20.GL_COMPILE_STATUS,status,0);
        if(status[0]==0) throw new IllegalStateException("Texture shader compile failed");
        return shader;
    }
    private void present() {
        check(GLES20.glGetError()==GLES20.GL_NO_ERROR,"render");
        check(EGLExt.eglPresentationTimeANDROID(display,window,System.nanoTime()),"presentationTime");
        check(EGL14.eglSwapBuffers(display,window),"swapBuffers");
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
        if (closed) return;
        closed = true;
        try {
            if (display != EGL14.EGL_NO_DISPLAY) {
                if(window!=EGL14.EGL_NO_SURFACE && context!=EGL14.EGL_NO_CONTEXT) {
                    AgentLog.cleanup("egl-gl-objects", () -> {
                        current();
                        if(texture!=0) GLES20.glDeleteTextures(1,new int[]{texture},0);
                        if(program!=0) GLES20.glDeleteProgram(program);
                    });
                }
                AgentLog.cleanup("egl-unbind", () -> check(EGL14.eglMakeCurrent(display, EGL14.EGL_NO_SURFACE,
                        EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_CONTEXT), "unbind"));
                if (window != EGL14.EGL_NO_SURFACE)
                    AgentLog.cleanup("egl-surface", () -> check(EGL14.eglDestroySurface(display, window), "destroySurface"));
                if (context != EGL14.EGL_NO_CONTEXT)
                    AgentLog.cleanup("egl-context", () -> check(EGL14.eglDestroyContext(display, context), "destroyContext"));
                AgentLog.cleanup("egl-display", () -> check(EGL14.eglTerminate(display), "terminate"));
                AgentLog.cleanup("egl-thread", () -> check(EGL14.eglReleaseThread(), "releaseThread"));
            }
        } finally {
            display = EGL14.EGL_NO_DISPLAY;
            context = EGL14.EGL_NO_CONTEXT;
            window = EGL14.EGL_NO_SURFACE;
            if(placeholder!=null) {AgentLog.cleanup("camera-placeholder", placeholder::recycle);placeholder=null;}
            AgentLog.cleanup("camera-surface", surface::release);
        }
    }
}
