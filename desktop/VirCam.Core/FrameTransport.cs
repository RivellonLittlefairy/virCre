using System.Net.Sockets;

namespace VirCam.Core;

/// <summary>Serial pump-owned input connection. Reconnect never carries a client's encoded frame cache.</summary>
public sealed class FrameTransport(ConnectionInfo info,Action<string>? waiting=null,Func<bool>? isAlive=null,Func<bool>? isPaused=null) : IDisposable {
    private FrameClient? client;
    public int Generation {get;private set;}
    public bool SupportsRepeat=>info.SupportsRepeat;
    public bool SupportsHoldLastFrame=>info.SupportsHoldLastFrame;
    public async Task ConnectAsync(CancellationToken token) {
        client=new(info);
        try {await client.ConnectAsync(token).ConfigureAwait(false);}
        catch {client.Dispose();client=null;throw;}
    }
    public Task<bool> SendFrameAsync(byte[] image,long captured,CancellationToken token)=>ExchangeAsync(async (connection,cancel)=>{await connection.SendFrameAsync(image,captured,cancel).ConfigureAwait(false);},token);
    public Task<bool> RepeatFrameAsync(long captured,CancellationToken token)=>ExchangeAsync(async (connection,cancel)=>{await connection.RepeatFrameAsync(captured,cancel).ConfigureAwait(false);},token);
    public Task<bool> HoldAsync(CancellationToken token)=>ExchangeAsync((connection,cancel)=>connection.HoldAsync(cancel),token);
    public Task<bool> PauseAsync(CancellationToken token)=>ExchangeAsync((connection,cancel)=>connection.PauseAsync(cancel),token);
    private async Task<bool> ExchangeAsync(Func<FrameClient,CancellationToken,Task> exchange,CancellationToken token) {
        try {await exchange(client??throw new InvalidOperationException("Input is not connected."),token).ConfigureAwait(false);return true;}
        catch(Exception error) when(info.SupportsHoldLastFrame && Transient(error,token)) {
            client?.Dispose();client=null;
            waiting?.Invoke((error is OperationCanceledException?"输入传输超时":"输入连接暂时中断")+"，保持上一帧，正在重连；恢复后等待新的完整画面。");
            while(true) {
                token.ThrowIfCancellationRequested();
                if(isAlive!=null && !isAlive())throw new EndOfStreamException("投屏窗口已结束，无法继续输出相机画面。");
                await Task.Delay(500,token).ConfigureAwait(false);
                var candidate=new FrameClient(info);
                try {
                    await candidate.ConnectAsync(token).ConfigureAwait(false);
                    // Pause requested while the old ACK/connect was pending owns the first
                    // post-HELLO control. It must reach the phone before capture resumes.
                    if(isPaused?.Invoke()==true)await candidate.PauseAsync(token).ConfigureAwait(false);
                    client=candidate;Generation++;return false;
                }
                catch(Exception retry) when(Transient(retry,token)) {candidate.Dispose();}
                catch {candidate.Dispose();throw;}
            }
        }
    }
    private static bool Transient(Exception error,CancellationToken token)=>!token.IsCancellationRequested &&
        (error is OperationCanceledException or SocketException || error is IOException);
    public void Dispose() {client?.Dispose();client=null;}
}
