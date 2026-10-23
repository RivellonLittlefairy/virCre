package com.example.vircam.core;

import java.util.function.Consumer;

/** Production render boundary: only pixel upload borrows the current frame; surface queues and diagnostics never do. */
public final class DesktopFrameRenderer {
    public interface Target<T> {
        void beginDesktop();
        void uploadDesktop(T image,long revision);
        void presentDesktop();
    }
    private DesktopFrameRenderer() {}
    /** Automatic stalls freeze this mailbox only; pause/close still remove its image. */
    public static <T> void renderHolding(LatestFrame<T> frames,long now,Target<T> target,Consumer<Boolean> onPresented) {
        render(frames,now,Long.MAX_VALUE,target,onPresented);
    }
    public static <T> void render(LatestFrame<T> frames,long now,long maxAge,Target<T> target,Consumer<Boolean> onPresented) {
        target.beginDesktop();
        boolean[] hasInput={false};
        frames.use(now,maxAge,image->{
            target.uploadDesktop(image==null ? null : image.value,image==null ? 0 : image.revision);
            hasInput[0]=image!=null;
        });
        target.presentDesktop();
        onPresented.accept(hasInput[0]);
    }
}
