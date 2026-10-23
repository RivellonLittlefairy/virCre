namespace VirCam.Core;

/// <summary>Silence is valid for a static desktop. Periodically obtain a new session's first frame to bound silent failures.</summary>
public sealed class CaptureHealth(long startedMilliseconds) {
    private long lastFrame=startedMilliseconds,probeStarted;
    private bool hasFrame,probing;
    public bool Observe(long nowMilliseconds,bool receivedFrame) {
        if(receivedFrame) {lastFrame=nowMilliseconds;hasFrame=true;probing=false;return false;}
        if(!hasFrame && nowMilliseconds-lastFrame>=5000 || probing && nowMilliseconds-probeStarted>=5000)
            throw new IOException("暂未收到显示器新画面，正在重建采集。");
        if(hasFrame && !probing && nowMilliseconds-lastFrame>=10000) {
            probing=true;probeStarted=nowMilliseconds;return true;
        }
        return false;
    }
}
