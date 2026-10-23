namespace VirCam.Core;

public sealed class JpegPayloadTooLargeException() : IOException("桌面帧降低 JPEG 质量后仍超过 4 MiB 传输上限；请更换画面内容后重试。") { }

/// <summary>Single-owner exact region cache. Failed oversize content is not recompressed on every pump tick.</summary>
public sealed class FrameEncodingCache {
    private static readonly float[] Qualities=[0.88f,0.78f,0.65f,0.50f];
    private byte[]? previous,encoded;
    private string? geometry;
    private JpegPayloadTooLargeException? failure;

    public async Task<byte[]> GetAsync(byte[] pixels,string region,Func<float,CancellationToken,Task<byte[]>> encode,CancellationToken token) {
        token.ThrowIfCancellationRequested();
        bool unchanged=geometry==region && previous!=null && pixels.AsSpan().SequenceEqual(previous);
        if(unchanged && encoded!=null)return encoded;
        if(unchanged && failure!=null)throw failure;
        geometry=region;
        if(previous==null || previous.Length!=pixels.Length)previous=new byte[pixels.Length];
        pixels.CopyTo(previous,0);encoded=null;failure=null;
        foreach(float quality in Qualities) {
            token.ThrowIfCancellationRequested();
            byte[] candidate;
            try {candidate=await encode(quality,token).ConfigureAwait(false);}
            catch(JpegPayloadTooLargeException) {continue;}
            token.ThrowIfCancellationRequested();
            if(candidate.Length>0 && candidate.Length<=FrameProtocol.MaxPayload) {encoded=candidate;return candidate;}
            if(candidate.Length==0)throw new IOException("桌面 JPEG 编码结果为空。");
        }
        token.ThrowIfCancellationRequested();
        failure=new();throw failure;
    }
}
